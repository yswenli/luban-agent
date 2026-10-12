/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Views
*文件名： RootView
*版本号： V1.0.0.0
*唯一标识：顶层容器视图
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：顶层容器视图，按会话维护独立槽位（文档/ViewModel/命令/输入队列/加载指示器），
*划分三区域布局并承载全局快捷键、会话切换与输入提交；会话切换仅重绑视图，不打断后台运行
*
*****************************************************************************/
using LubanAgentCli.App.Models;
using LubanAgentCli.App.Models.Blocks;
using LubanAgentCli.App.ViewModels;

using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgentCli.App.Views;

/// <summary>
/// 顶层容器视图。为每个会话维护独立槽位（文档 + ViewModel + 命令 VM + 输入队列 + 加载指示器 + 页脚数据），
/// 负责全局快捷键、会话切换、输入提交派发与各会话 agent 生命周期协调。
/// 会话切换只重绑视图（文档/VM/页脚），后台会话的运行不被打断。
/// </summary>
internal sealed class RootView : Runnable
{
    /// <summary>
    /// 单个会话的 UI 槽位：各自独立的文档、ViewModel、命令 VM、输入队列、运行态与加载指示器。
    /// </summary>
    private sealed class SessionUi
    {
        public required string SessionId { get; init; }
        public required ConversationDocument Doc { get; init; }
        public required ConversationViewModel Vm { get; init; }
        public required CommandViewModel CommandVm { get; init; }
        public required SpinnerService Spinner { get; init; }
        public required FooterDataProvider FooterProvider { get; init; }

        // 待执行队列：该会话运行期间提交的输入在此排队（含附件），本轮结束后顺序执行
        public readonly Queue<(string Text, IReadOnlyList<AttachmentDraft> Attachments)> InputQueue = new();

        // 是否处于运行/派发中（含初始化空档），用于决定新输入立即执行还是入队
        public volatile bool RunActive;

        // 是否已完成初始化（解析工作区/预热 Agent/回放历史）
        public volatile bool Initialized;

        // 初始化去重：同一会话的启动预热与首次输入共用同一任务
        public readonly object InitLock = new();
        public Task? InitTask;

        // 后台会话等待确认时，是否已向活动会话提示过一次
        public bool HintShown;
    }

    private readonly IServiceProvider _services;
    private readonly IUiDispatcher _dispatcher;
    private readonly ITuiUiService _ui;
    private readonly SessionManager _sessionManager;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly AgentViewViewModel _agentVm;
    private readonly ConversationView _conversation;
    private readonly FooterView _footer;
    private readonly InputBarView _inputBar;

    // 按会话标识（Ordinal）索引的槽位表；切换会话只改 _active，不销毁任何槽位。
    // 但空闲槽位会按 LRU 驱逐（见 EvictIdleSlots），数据库仍保留历史，再次访问时回放。
    private readonly Dictionary<string, SessionUi> _sessions = new(StringComparer.Ordinal);
    private SessionUi _active = null!;

    // 槽位 LRU（队首最新）：空闲槽位数超上限时从队尾驱逐，避免多会话长期驻留无界增长。
    private readonly LinkedList<string> _slotLru = new();
    private const int MaxCachedSlots = 10;

    // 后台知识库任务（索引 / wiki ingest / rebuild）进行中的状态块：按任务 Id 复用同一条系统消息节流刷新
    private readonly Dictionary<string, (SystemBlock Block, DateTime At)> _taskBlocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan TaskProgressInterval = TimeSpan.FromMilliseconds(500);

    private readonly Action<string> _onCurrentSessionChanged;
    private readonly Action _onExitRequested;
    private Terminal.Gui.App.IKeyboard? _keyboard;
    private EventHandler<Terminal.Gui.Input.Key>? _onGlobalKeyDown;
    private bool _disposed;

    /// <summary>
    /// 初始化顶层容器、按当前会话建立槽位与三区域布局。
    /// </summary>
    /// <param name="services">根级 DI 容器。</param>
    /// <param name="dispatcher">UI 线程调度器。</param>
    /// <param name="ui">TUI 模态交互服务。</param>
    /// <param name="startupNotices">启动提示。</param>
    public RootView(
        IServiceProvider services,
        IUiDispatcher dispatcher,
        ITuiUiService ui,
        IReadOnlyList<string>? startupNotices = null)
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        SetScheme(TuiTheme.BuildScheme());

        _services = services;
        _dispatcher = dispatcher;
        _ui = ui;
        // 单例；ISessionManager 由 SessionManager 实现（同一实例），转为具体类型以订阅 CurrentSessionChanged
        _sessionManager = (SessionManager)services.GetRequiredService<ISessionManager>();
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        _onExitRequested = ConfirmExit;

        // 自动重试进度提示：框架回调运行在调用线程，需编组到 UI 线程（投递到当前活动会话文档）
        services.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.OnApiRetry = notice =>
            _dispatcher.Invoke(() => _active.Doc.AppendBlock(new SystemBlock(
                $"⟳ {ApiErrorClassifier.GetCategoryLabel(notice.Category)}：{notice.Delay.TotalSeconds:F0}s 后重试（第 {notice.Attempt} 次，最多 {notice.MaxAttempts - 1} 次）",
                foreground: BlockColors.Accent)));

        // 当前会话（启动期 StartupWorkspaceResolver 已恢复/新建）；无工作区时为空串占位槽位
        var initialSessionId = _sessionManager.CurrentSession?.SessionId ?? string.Empty;
        _active = GetOrCreateSlot(initialSessionId);
        _agentVm = new AgentViewViewModel(new TaskRegistry(), _active.Doc);

        // 启动横幅
        _active.Doc.AppendBlock(new SystemBlock("✻ LuBan Agent CLI", isBold: true, foreground: BlockColors.Accent));
        _active.Doc.AppendBlock(new SystemBlock(
            "  Enter 发送，Shift+Enter 或 Ctrl+Enter 换行，/exit 退出，Ctrl+Q 退出，Esc 取消，Ctrl+L 重绘，Shift+Tab 切换模式。"));
        _active.Doc.AppendBlock(new SystemBlock("  正在初始化 Agent 并进入默认会话..."));

        if (startupNotices is not null)
        {
            foreach (var notice in startupNotices)
            {
                _active.Doc.AppendBlock(new SystemBlock(notice));
            }
        }

        _active.Doc.AppendBlock(new SystemBlock(string.Empty));

        // 会话区：从顶部开始，高度=填充到底部（footer 1 + inputBar 4 = 5）
        _conversation = new ConversationView(_active.Doc, _active.Vm)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(5)
        };

        _footer = new FooterView
        {
            X = 0, Y = Pos.Bottom(_conversation), Width = Dim.Fill(), Height = 1
        };
        _footer.SetProvider(_active.FooterProvider);
        _footer.SetMode(_active.Vm.PermissionModeDisplay);

        // 所有槽位（含后续创建的）的页脚数据提供者指向同一 FooterView
        foreach (var slot in _sessions.Values)
        {
            slot.Vm.SetFooter(_footer, slot.FooterProvider);
        }

        // 输入栏初始高度=4（2行内容 + 边框上下各1），随内容视觉折行动态扩展
        _inputBar = new InputBarView
        {
            X = 0, Y = Pos.Bottom(_footer), Width = Dim.Fill(), Height = 4
        };
        _inputBar.SetServiceProvider(services);
        _inputBar.SetUiService(ui);
        _inputBar.SubmittedWithAttachments += OnInputSubmitted;
        _inputBar.Submitted += text => OnInputSubmitted(text, Array.Empty<AttachmentDraft>());
        // 当活动会话存在挂起的确认块（工具确认 / 授权二次确认）时，把输入编辑器的按键优先转发给它：
        // agent 阻塞等待期间焦点仍在输入编辑器，确认块本身收不到键，必须由这里转发，否则会一直等待用户选择。
        _inputBar.KeyPreRouter = key =>
        {
            var pc = _active.Vm.PendingChoice;
            var consumed = pc is not null && pc.Selected is null && pc.HandleKey(key);
            if (Infrastructure.TuiDiag.Enabled && consumed)
            {
                Logger.Warn($"[TuiDiag-Enter] KeyPreRouter consumed key={key} PendingChoice.Title='{pc!.Title}' Selected=null");
            }
            return consumed;
        };
        _inputBar.OnPreRoutedKey = () => _conversation.SetNeedsDraw();
        _active.Vm.PermissionModeChanged += OnActivePermissionModeChanged;
        _active.Vm.PendingChoiceChanged += OnActivePendingChoiceChanged;

        Add(_conversation, _footer, _inputBar);

        // 后台知识库任务通知：/llmwiki -index 等转入后台后，进度在活动会话显示区节流刷新、终态追加结果。
        // KnowledgeTaskService 仅在 embedder 可用时注册，缺失时静默跳过。
        if (services.GetService<KnowledgeTaskService>() is { } knowledgeTasks)
        {
            knowledgeTasks.Updated += OnKnowledgeTaskUpdated;
        }

        _onCurrentSessionChanged = OnCurrentSessionChanged;
        _sessionManager.CurrentSessionChanged += _onCurrentSessionChanged;

        var completionPopup = _inputBar.CompletionPopup;
        completionPopup.Width = 55;
        completionPopup.Height = 20;
        completionPopup.X = 2;
        completionPopup.Y = Pos.Func(_ => _inputBar.Frame.Y - completionPopup.VisibleRows);
        Add(completionPopup);

        _inputBar.CompletionStateChanged += () =>
        {
            completionPopup.SetNeedsDraw();
            SetNeedsDraw();
        };
    }

    /// <inheritdoc/>
    public override void EndInit()
    {
        base.EndInit();
        // 强制布局更新以确保全屏显示
        SetNeedsLayout();
        // 延迟设置焦点：EndInit 阶段 Application 主循环尚未开始，
        // SetFocus 不会立即生效。在 IsRunningChanged 时设置焦点。
        IsRunningChanged += OnIsRunningChanged;
        // 全局按键兜底：Editor 等焦点视图可能消费按键导致冒泡链断裂，
        // 订阅应用级 Keyboard.KeyDown 确保 Shift+Tab 等全局快捷键始终可达
        var app = GetApp();
        if (app?.Keyboard is { } keyboard)
        {
            _keyboard = keyboard;
            _onGlobalKeyDown = OnGlobalKeyDown;
            keyboard.KeyDown += _onGlobalKeyDown;
        }
    }

    /// <summary>
    /// Application 主循环开始运行后，将焦点设置到输入栏，并预热当前会话。
    /// </summary>
    private void OnIsRunningChanged(object? sender, EventArgs e)
    {
        if (IsRunning)
        {
            _inputBar.FocusInput();
            IsRunningChanged -= OnIsRunningChanged;
            StartBackgroundInit(_active);
        }
    }

    // ── 会话槽位管理 ──

    /// <summary>
    /// 取（或创建）指定会话的槽位。创建时构造独立文档/ViewModel/命令VM/加载指示器，并以闭包捕获槽位订阅其事件。
    /// </summary>
    private SessionUi GetOrCreateSlot(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var existing)) return existing;

        var doc = new ConversationDocument();
        var spinner = new SpinnerService();
        var vm = new ConversationViewModel(sessionId, _services, _dispatcher, doc, spinner);
        var commandVm = new CommandViewModel(doc, vm, _services, _dispatcher, _ui, spinner);
        var slot = new SessionUi
        {
            SessionId = sessionId,
            Doc = doc,
            Vm = vm,
            CommandVm = commandVm,
            Spinner = spinner,
            FooterProvider = new FooterDataProvider()
        };

        commandVm.ExitRequested += _onExitRequested;
        vm.PendingChoiceChanged += () => OnSlotPendingChoiceChanged(slot);

        // 页脚视图已建成时立即绑定（运行期新建的槽位）；构造期的首个槽位由构造尾部统一绑定
        if (_footer is not null)
        {
            vm.SetFooter(_footer, slot.FooterProvider);
        }

        _sessions[sessionId] = slot;
        TouchSlot(sessionId);
        EvictIdleSlots();
        return slot;
    }

    /// <summary>把会话槽位标记为最近使用（移到 LRU 队首）。</summary>
    private void TouchSlot(string sessionId)
    {
        var node = _slotLru.Find(sessionId);
        if (node is not null)
        {
            _slotLru.Remove(node);
        }
        _slotLru.AddFirst(sessionId);
    }

    /// <summary>
    /// 空闲槽位超过 <see cref="MaxCachedSlots"/> 时，从 LRU 队尾驱逐可回收槽位。
    /// 跳过活动会话、运行中/排队中/等待确认/初始化未完成的槽位；驱逐即退订并 Dispose，
    /// 再次切换到该会话时由 GetOrCreateSlot 重建并回放历史。
    /// </summary>
    private void EvictIdleSlots()
    {
        if (_sessions.Count <= MaxCachedSlots) return;

        var node = _slotLru.Last;
        while (node is not null && _sessions.Count > MaxCachedSlots)
        {
            var prev = node.Previous;
            var id = node.Value;
            if (_sessions.TryGetValue(id, out var slot) && CanEvict(slot))
            {
                _sessions.Remove(id);
                _slotLru.Remove(node);
                slot.CommandVm.ExitRequested -= _onExitRequested;
                slot.Vm.Dispose();
            }
            node = prev;
        }
    }

    /// <summary>槽位是否可安全驱逐（非活动、未运行、无排队、无待确认、已完成初始化）。</summary>
    private bool CanEvict(SessionUi slot)
        => _active is not null
            && !ReferenceEquals(slot, _active)
            && !slot.RunActive
            && !slot.Vm.IsRunning
            && slot.InputQueue.Count == 0
            && slot.Vm.PendingChoice is null
            && slot.Initialized;

    /// <summary>
    /// 会话切换（CurrentSessionChanged）。在 UI 线程重绑活动槽位为指定会话，并预热/排空其队列。
    /// 不取消后台会话的运行。
    /// </summary>
    private void OnCurrentSessionChanged(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _dispatcher.Invoke(() => SwitchToSession(sessionId));
    }

    /// <summary>
    /// 切换到指定会话槽位：重绑会话区/页脚/任务视图，并启动其后台预热与队列排空。必须在 UI 线程调用。
    /// </summary>
    private void SwitchToSession(string sessionId)
    {
        var slot = GetOrCreateSlot(sessionId);
        if (ReferenceEquals(slot, _active)) return;

        // 退订旧活动槽位的活动态事件，改订新活动槽位
        _active.Vm.PermissionModeChanged -= OnActivePermissionModeChanged;
        _active.Vm.PendingChoiceChanged -= OnActivePendingChoiceChanged;
        slot.Vm.PermissionModeChanged += OnActivePermissionModeChanged;
        slot.Vm.PendingChoiceChanged += OnActivePendingChoiceChanged;

        _active = slot;
        TouchSlot(sessionId);

        _conversation.Rebind(slot.Doc, slot.Vm);
        _footer.SetProvider(slot.FooterProvider);
        _footer.SetMode(slot.Vm.PermissionModeDisplay);
        _agentVm.Rebind(slot.Doc);

        StartBackgroundInit(slot);
        DrainQueue(slot);
    }

    /// <summary>
    /// 启动指定会话的后台初始化：解析工作区、预热 Agent、回放历史。
    /// 初始化期间提交的输入会进入该会话的待执行队列，初始化完成后自动顺序执行。
    /// </summary>
    private void StartBackgroundInit(SessionUi slot)
    {
        if (slot.Initialized) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureSlotInitializedAsync(slot);
            }
            catch (Exception ex)
            {
                Logger.Error("启动初始化 Agent 失败", ex);
                _dispatcher.Invoke(() => slot.Doc.AppendBlock(new SystemBlock(
                    $"Agent 初始化失败: {ex.Message}", foreground: BlockColors.Failure)));
            }
            finally
            {
                DrainQueue(slot);
            }
        });
    }

    /// <summary>
    /// 确保指定会话槽位已初始化。启动预热与首次输入复用同一任务；失败时不缓存任务，允许下次输入重试。
    /// </summary>
    private Task EnsureSlotInitializedAsync(SessionUi slot)
    {
        if (slot.Initialized) return Task.CompletedTask;

        lock (slot.InitLock)
        {
            if (slot.Initialized) return Task.CompletedTask;
            return slot.InitTask ??= InitializeSlotAsync(slot);
        }
    }

    private async Task InitializeSlotAsync(SessionUi slot)
    {
        try
        {
            await slot.Vm.InitializeAsync();
            slot.Initialized = true;
        }
        catch
        {
            lock (slot.InitLock) slot.InitTask = null;
            throw;
        }
    }

    /// <summary>
    /// 活动会话确认块挂起状态变化：挂起时聚焦会话区（会话区可把按键转发给确认块），
    /// 结束后聚焦回输入编辑器（便于继续输入/排队）。
    /// </summary>
    private void OnActivePendingChoiceChanged()
    {
        _dispatcher.Invoke(() =>
        {
            if (_active.Vm.PendingChoice is not null)
            {
                _conversation.SetFocus();
            }
            else
            {
                _inputBar.FocusInput();
            }
        });
    }

    /// <summary>
    /// 活动会话权限模式变化：仅更新页脚（模式本身由活动 VM 持有）。
    /// </summary>
    private void OnActivePermissionModeChanged(ToolPermissionMode mode)
        => _footer.SetMode(_active.Vm.PermissionModeDisplay);

    /// <summary>
    /// 非活动槽位的权限模式变化：不打扰当前页脚。
    /// </summary>
    private void OnSlotPendingChoiceChanged(SessionUi slot)
    {
        if (ReferenceEquals(slot, _active)) return;

        _dispatcher.Invoke(() =>
        {
            if (slot.Vm.PendingChoice is not null && !slot.HintShown)
            {
                slot.HintShown = true;
                _active.Doc.AppendBlock(new SystemBlock(
                    $"⏳ 会话「{slot.Vm.SessionTitle}」等待确认，/session -switch {slot.SessionId} 处理",
                    foreground: BlockColors.Accent));
            }
            else if (slot.Vm.PendingChoice is null)
            {
                slot.HintShown = false;
            }
        });
    }

    /// <summary>活动会话文档模型。</summary>
    public ConversationDocument Document => _active.Doc;

    /// <summary>活动会话 ViewModel。</summary>
    public ConversationViewModel ViewModel => _active.Vm;

    /// <summary>会话区视图。</summary>
    public ConversationView Conversation => _conversation;

    /// <summary>页脚视图。</summary>
    public FooterView Footer => _footer;

    /// <summary>输入区视图。</summary>
    public InputBarView InputBar => _inputBar;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (_keyboard is not null && _onGlobalKeyDown is not null)
            {
                _keyboard.KeyDown -= _onGlobalKeyDown;
                _keyboard = null;
                _onGlobalKeyDown = null;
            }
            _sessionManager.CurrentSessionChanged -= _onCurrentSessionChanged;
            if (_services.GetService<KnowledgeTaskService>() is { } knowledgeTasks)
            {
                knowledgeTasks.Updated -= OnKnowledgeTaskUpdated;
            }
            _inputBar.SubmittedWithAttachments -= OnInputSubmitted;
            foreach (var slot in _sessions.Values)
            {
                slot.CommandVm.ExitRequested -= _onExitRequested;
                slot.Vm.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// 应用级键盘事件兜底。仅处理 Shift+Tab（权限模式切换）：
    /// RootView 为顶层 Runnable 时才生效，模态对话框运行期间不抢键；
    /// 已被视图链正常处理的事件（Handled）不重复处理。
    /// </summary>
    private void OnGlobalKeyDown(object? sender, Key key)
    {
        if (Infrastructure.TuiDiag.Enabled)
        {
            Logger.Warn($"[TuiDiag-Key] App.Keyboard.KeyDown key={key} Handled={key.Handled}");
        }

        if (key != Key.Tab.WithShift || key.Handled)
        {
            return;
        }

        var app = GetApp();
        if (app is null || !ReferenceEquals(app.TopRunnable, this))
        {
            return;
        }

        HandleShiftTab(key);
    }

    /// <summary>
    /// Shift+Tab 权限模式切换（供 RootView.OnKeyDown 与全局兜底共用）。
    /// 应用级 Keyboard.KeyDown 早于视图派发触发，同一次按键可能经两条路径到达，
    /// 用 <see cref="Key.Handled"/> 去重，避免模式跳两级、Bypass 确认弹两次。
    /// </summary>
    private void HandleShiftTab(Key key)
    {
        if (key.Handled)
        {
            return;
        }

        key.Handled = true;

        var vm = _active.Vm;
        var doc = _active.Doc;

        if (vm.IsRunning)
        {
            doc.AppendBlock(new SystemBlock("Agent 运行中无法切换权限模式"));
            return;
        }

        // 防御性清残留：用户若在 BypassConfirm 等待中又按 Shift+Tab 试图跳过确认，
        // 必须把上一次的 PendingChoice 清掉，否则 KeyPreRouter 会持续吞掉所有 Enter，
        // 直到本回合被某种流程清掉为止，导致"回车毫无反应"的隐性 bug。
        if (vm.PendingChoice is not null && vm.PendingChoice.Selected is null)
        {
            Logger.Warn($"[TuiDiag-PendingChoice] HandleShiftTab clearing stale PendingChoice.Title='{vm.PendingChoice.Title}'");
            vm.PendingChoice = null;
        }

        var newMode = vm.CyclePermissionMode();
        Logger.Warn($"[TuiDiag-PermMode] HandleShiftTab cycle -> {vm.PermissionModeDisplay} (newMode={newMode})");

        // BypassPermissions 需二次确认
        if (newMode == ToolPermissionMode.BypassPermissions)
        {
            var confirmBlock = ChoiceBlocks.BypassConfirm(confirmed =>
            {
                vm.PendingChoice = null;
                if (!confirmed)
                {
                    vm.SetPermissionMode(ToolPermissionMode.Default);
                }
                doc.AppendBlock(new SystemBlock(
                    confirmed ? "⚠ Bypass Permissions 已启用" : "已恢复 Default 模式",
                    foreground: confirmed ? BlockColors.Failure : BlockColors.Success));
            });
            doc.AppendBlock(confirmBlock);
            vm.PendingChoice = confirmBlock;
            Logger.Warn($"[TuiDiag-PendingChoice] HandleShiftTab set PendingChoice='{confirmBlock.Title}' (BypassConfirm)");
            return;
        }

        doc.AppendBlock(new SystemBlock(
            $"权限模式: {vm.PermissionModeDisplay}", foreground: BlockColors.Accent));
    }

    // ── 全局快捷键 ──

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            ConfirmExit();
            return true;
        }

        if (key == Key.L.WithCtrl)
        {
            GetApp()?.LayoutAndDraw(true);
            return true;
        }

        if (key == Key.Tab && !key.IsShift)
        {
            _agentVm.ToggleView();
            var label = _agentVm.IsTaskViewActive ? "Agent View · 任务表" : "Conversation View";
            _active.Doc.AppendBlock(new SystemBlock(label, foreground: BlockColors.Accent, isBold: true));
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            HandleShiftTab(key);
            return true;
        }

        if (key == Key.Esc)
        {
            if (_active.Vm.IsRunning)
            {
                _active.Vm.Cancel();
                _active.Doc.AppendBlock(new SystemBlock("⌛ 正在取消当前任务...", foreground: BlockColors.Accent));
            }
            else
            {
                // 空闲时 Esc 会触发 Runnable 默认退出：统一走确认对话框防误触
                ConfirmExit();
            }
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>
    /// 所有退出路径的统一入口：弹出模态确认对话框，确认后才退出，避免误操作。
    /// </summary>
    private void ConfirmExit()
    {
        var message = _active.Vm.IsRunning
            ? "Agent 正在运行中，退出将中断当前任务。\n确定要退出吗？"
            : "确定要退出 LuBan Agent CLI 吗？";

        if (_ui.Confirm("退出确认", message))
        {
            RequestStop();
        }
    }

    // ── 输入提交 ──

    /// <summary>
    /// 处理用户提交输入（作用于活动会话）。
    /// `/` 命令立即在活动会话执行（不进队列，确保 /session -switch 等不被卡住）；
    /// 普通文本在活动会话忙碌（运行中/初始化中/派发中）时入队，否则立即执行。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    /// <param name="attachments">附件草稿。</param>
    private void OnInputSubmitted(string text, IReadOnlyList<AttachmentDraft> attachments)
    {
        var slot = _active;
        if (Infrastructure.TuiDiag.Enabled)
        {
            Logger.Warn($"[TuiDiag-Submit] OnInputSubmitted textLen={text.Length} session={slot.SessionId} runActive={slot.RunActive} isRunning={slot.Vm.IsRunning} queueCount={slot.InputQueue.Count} PendingChoice={(slot.Vm.PendingChoice is null ? "null" : $"'{slot.Vm.PendingChoice.Title}'(Selected={slot.Vm.PendingChoice.Selected?.Key.ToString() ?? "null"})")}");
        }

        if (string.Equals(text, "/exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "/quit", StringComparison.OrdinalIgnoreCase))
        {
            ConfirmExit();
            return;
        }

        // `/` 命令立即执行（用活动槽位的命令 VM，输出进各自文档）；不排队，避免 /session -switch 被运行会话卡住
        if (TryDispatchCommand(slot, text))
        {
            return;
        }

        // 普通文本：活动会话忙碌时入队，本轮结束后自动顺序执行
        if (slot.RunActive || slot.Vm.IsRunning)
        {
            slot.InputQueue.Enqueue((text, attachments));
            var preview = PreviewInput(text);
            _dispatcher.Invoke(() => slot.Doc.AppendBlock(new SystemBlock(
                $"🕓 已加入待执行队列（共 {slot.InputQueue.Count} 条）: {preview}",
                foreground: BlockColors.Accent)));
            return;
        }

        SubmitAsync(slot, text, attachments);
    }

    // ── 后台知识库任务通知 ──

    /// <summary>
    /// 后台知识库任务更新：进行中在活动会话同一条系统消息上节流刷新，终态追加结果消息；
    /// wiki ingest 提取完成后自动把产物交给 Agent 整理（走正常输入提交路径，忙碌时排队）。
    /// 事件来自后台线程，所有文档变更编组到 UI 线程。
    /// </summary>
    /// <param name="info">任务信息快照。</param>
    private void OnKnowledgeTaskUpdated(KnowledgeTaskInfo info)
    {
        if (info.State == KnowledgeTaskState.Running)
        {
            _dispatcher.Invoke(() => RenderTaskProgress(info));
            return;
        }

        _dispatcher.Invoke(() => RenderTaskTerminal(info));

        if (info.Kind == KnowledgeTaskKind.WikiIngest
            && info.State == KnowledgeTaskState.Completed
            && !string.IsNullOrWhiteSpace(info.ArtifactPath))
        {
            var artifact = info.ArtifactPath!;
            var root = _workspaceManager.GetKnownRootPath(info.WorkspaceId);
            var rel = string.IsNullOrWhiteSpace(root)
                ? artifact
                : Path.GetRelativePath(root, artifact).Replace('\\', '/');
            var instruction =
                $"请读取附件中的源文件汇总（{rel}），按 wiki schema 将其沉淀为 wiki 页面（先读 index，必要时并页），并为相关实体/概念建页互链。";
            _dispatcher.Invoke(() => OnInputSubmitted(instruction,
                new[] { new AttachmentDraft(artifact, Path.GetFileName(artifact)) }));
        }
    }

    /// <summary>进行中：同一任务复用同一条系统消息，按 <see cref="TaskProgressInterval"/> 节流刷新。必须在 UI 线程调用。</summary>
    /// <param name="info">任务信息快照。</param>
    private void RenderTaskProgress(KnowledgeTaskInfo info)
    {
        var text = info.Total > 0
            ? $"{info.Label}：{info.Done}/{info.Total} · {info.Detail}"
            : $"{info.Label}：{info.Detail}";

        if (_taskBlocks.TryGetValue(info.Id, out var entry))
        {
            if (DateTime.UtcNow - entry.At < TaskProgressInterval) return;
            _taskBlocks[info.Id] = (entry.Block, DateTime.UtcNow);
            entry.Block.SetText(text);
            _active.Doc.NotifyBlockChanged(entry.Block);
            return;
        }

        var block = new SystemBlock(text);
        _taskBlocks[info.Id] = (block, DateTime.UtcNow);
        _active.Doc.AppendBlock(block);
    }

    /// <summary>终态：追加一条结果消息并清理进度块追踪。必须在 UI 线程调用。</summary>
    /// <param name="info">任务信息快照。</param>
    private void RenderTaskTerminal(KnowledgeTaskInfo info)
    {
        var text = info.State switch
        {
            KnowledgeTaskState.Completed => $"{info.Label} 已完成：{info.Detail}",
            KnowledgeTaskState.Canceled => $"{info.Label} 已取消",
            _ => $"{info.Label} 失败：{info.Detail}"
        };
        _taskBlocks.Remove(info.Id);
        _active.Doc.AppendBlock(new SystemBlock(text,
            foreground: info.State == KnowledgeTaskState.Failed ? BlockColors.Failure : BlockColors.Success));
    }

    /// <summary>
    /// 尝试把输入作为 `/` 命令派发给指定会话的命令 VM。
    /// 非命令或未识别的命令返回 false，由调用方按普通对话提交。
    /// 命令可能直接操作文档/视图（如 /clear），**必须在 UI 线程调用**。
    /// </summary>
    /// <param name="slot">目标会话槽位。</param>
    /// <param name="text">用户输入文本。</param>
    private static bool TryDispatchCommand(SessionUi slot, string text)
    {
        return text.StartsWith('/') && slot.CommandVm.TryExecute(text);
    }

    /// <summary>为待执行队列生成单行预览文本。</summary>
    private static string PreviewInput(string text)
    {
        var t = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length > 40 ? t[..40] + "…" : t;
    }

    /// <summary>
    /// 提交一条输入到指定会话：若该会话尚未初始化（如启动预热仍在进行）会先复用同一初始化任务，再执行流式对话。
    /// 全程在后台线程运行，确保 UI 线程始终自由（渲染、键盘路由、队列不受影响）。
    /// </summary>
    private void SubmitAsync(SessionUi slot, string text, IReadOnlyList<AttachmentDraft> attachments)
    {
        // 标记“提交派发中”，防止在 Agent 真正进入运行态(IsRunning)前的空隙里重复派发
        slot.RunActive = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureSlotInitializedAsync(slot);
                await slot.Vm.ProcessInputAsync(text, attachments);
            }
            catch (Exception ex)
            {
                Logger.Error("初始化或处理输入异常", ex);
                _dispatcher.Invoke(() => slot.Doc.AppendBlock(new SystemBlock(
                    $"Agent 异常: {ex.Message}", foreground: BlockColors.Failure)));
            }
            finally
            {
                DrainQueue(slot);
            }
        });
    }

    /// <summary>
    /// 指定会话一轮对话结束后，若其待执行队列非空则顺序执行下一条；否则释放“派发中”标志。
    /// 队列中的 `/` 命令必须走命令路由（否则会被当作普通对话发给 Agent），
    /// 且命令可能直接操作文档/视图（如 /clear），因此整个排空过程编组到 UI 线程执行，
    /// 保证命令与对话严格按提交顺序处理。
    /// </summary>
    private void DrainQueue(SessionUi slot)
    {
        _dispatcher.Invoke(() => DrainQueueOnUiThread(slot));
    }

    /// <summary>
    /// 在 UI 线程排空指定会话的待执行队列：命令同步执行后继续排空，普通对话提交后等待其结束再排空。
    /// </summary>
    private void DrainQueueOnUiThread(SessionUi slot)
    {
        // 若该会话当前不是活动会话，仍按原槽位继续处理（命令输出进其自身文档），不打扰当前视图
        while (slot.InputQueue.TryDequeue(out var next))
        {
            if (TryDispatchCommand(slot, next.Text))
            {
                // 管理型命令自身异步执行、不占用对话运行态，继续排空剩余队列
                continue;
            }

            SubmitAsync(slot, next.Text, next.Attachments);
            return;
        }

        slot.RunActive = false;
    }
}