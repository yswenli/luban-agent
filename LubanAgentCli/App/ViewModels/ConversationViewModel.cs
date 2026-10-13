/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.ViewModels
*文件名： ConversationViewModel
*版本号： V1.0.0.0
*唯一标识：会话 ViewModel
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：单个会话的 ViewModel：绑定固定 sessionId，经 AgentHostService 按会话路由流式对话，
*消费 UI 无关的 StreamEvent 序列并编排为 Block 文档；所有 UI 更新通过 IUiDispatcher
*编组到 UI 线程
*
*****************************************************************************/
using LuBan.AIAgent.Orchestration;
using LuBan.Common.IO;

using LubanAgentCli.App.Services;

namespace LubanAgentCli.App.ViewModels;

/// <summary>
/// 会话 ViewModel。每个活动会话持有独立实例，绑定固定 <c>sessionId</c>，
/// 经 <see cref="AgentHostService"/> 按会话路由执行流式对话（会话隔离、后台并发互不干扰），
/// 消费 UI 无关的 <see cref="StreamEvent"/> 序列并编排为 Block 追加到本会话文档。
/// 所有 UI 更新通过 <see cref="IUiDispatcher.Invoke"/> 编组到 UI 线程，本类不直接持有 View 引用。
/// </summary>
internal sealed class ConversationViewModel : IDisposable, IAgentContextReloadTrigger
{
    private readonly string _sessionId;
    private readonly IServiceProvider _services;
    private readonly IUiDispatcher _dispatcher;
    private readonly ConversationDocument _doc;
    private readonly ConfigManager _configManager;
    private readonly TitleService _titleService;
    private readonly ISessionManager _iSessionManager;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly AgentHostService _agentHost;
    private readonly SpinnerService _spinner;

    // 由 DI 解析并缓存的页脚引用（会话切换/初始化时刷新状态栏）
    private FooterView? _footerView;
    private FooterDataProvider? _footerProvider;

    private CancellationTokenSource? _currentCts;

    // 运行序号 + 取消覆盖机制（参考 Codex MainWindowViewModel 的 _activeRunSeq）：
    // 每次发起对话自增；取消(Esc)或新对话会作废旧序号，使旧运行的迟到事件/挂起任务被丢弃。
    private int _activeRunSeq;
    private int _currentRunSeq;
    private bool _cancelRequested;

    // 本轮是否因用户拒绝工具确认而终止：用于收尾时区分「已拒绝，本轮终止」与「任务已取消」
    private volatile bool _deniedByUser;

    // 终止文案是否已输出：拒绝钩子与取消分支都可能尝试提示，用 Interlocked 保证只显示一次
    private int _denialMessageShown;

    // Plan 模式下本轮收集的计划项数量（回合结束时汇总提示，工具均不执行）
    private int _plannedCount;

    // 待重建 Agent（模型切换 / AGENTS.md 变更）；运行中置位、本轮结束后消费
    private volatile bool _pendingAgentReset;

    // 流式 token 合批缓冲：消费线程追加、节流冲刷时取出，替代逐 token Invoke 洪峰
    private readonly object _streamLock = new();
    private readonly StringBuilder _pendingThinking = new();
    private readonly StringBuilder _pendingAnswer = new();
    private FlushThrottle? _streamThrottle;

    // 当前会话的流式状态（每次 ConsumeStreamAsync 开始重置）
    private ThinkingBlock? _thinkingBlock;
    private bool _thinkingCompleted;
    private int _currentThinkingRound; // 当前思考块所属的 LLM 轮次号（0=未标注）
    private ActionSpinnerBlock? _currentSpinner;

    /// <summary>
    /// 本轮正在执行的工具块（按调用先后顺序）。同一轮可能出现多个工具调用，
    /// 结果需按 CallId 精确匹配，故用列表而非单槽字段。仅在 UI 线程读写。
    /// </summary>
    private readonly List<ToolCallBlock> _runningToolBlocks = new();

    /// <summary>
    /// 本轮正在执行的编排节点块（按启动先后顺序）。同层节点可并行执行，
    /// 结束后由对应完成/失败事件按 NodeId 就地收尾。仅在 UI 线程读写。
    /// </summary>
    private readonly List<OrchestrationNodeBlock> _runningNodeBlocks = new();

    /// <summary>当前权限模式。</summary>
    public ToolPermissionMode PermissionMode { get; private set; } = ToolPermissionMode.Default;

    /// <summary>权限模式变更事件（订阅者更新页脚等 UI）。</summary>
    public event Action<ToolPermissionMode>? PermissionModeChanged;

    /// <summary>本会话标题（初始化后可用，供多会话视图展示）。</summary>
    public string SessionTitle { get; private set; } = string.Empty;

    /// <summary>本会话绑定的会话标识。</summary>
    public string SessionId => _sessionId;

    /// <summary>
    /// 循环切换到下一权限模式（Default → Plan → AcceptEdits → BypassPermissions → Default）。
    /// </summary>
    /// <returns>新的权限模式。</returns>
    public ToolPermissionMode CyclePermissionMode()
    {
        PermissionMode = PermissionMode switch
        {
            ToolPermissionMode.Default => ToolPermissionMode.Plan,
            ToolPermissionMode.Plan => ToolPermissionMode.AcceptEdits,
            ToolPermissionMode.AcceptEdits => ToolPermissionMode.BypassPermissions,
            ToolPermissionMode.BypassPermissions => ToolPermissionMode.Default,
            _ => ToolPermissionMode.Default
        };

        PermissionModeChanged?.Invoke(PermissionMode);
        return PermissionMode;
    }

    /// <summary>
    /// 设置权限模式并通知。
    /// </summary>
    /// <param name="mode">目标模式。</param>
    public void SetPermissionMode(ToolPermissionMode mode)
    {
        PermissionMode = mode;
        PermissionModeChanged?.Invoke(mode);
    }

    /// <summary>
    /// 当前权限模式的可读名称。
    /// </summary>
    public string PermissionModeDisplay => PermissionMode switch
    {
        ToolPermissionMode.Default => "default",
        ToolPermissionMode.Plan => "plan",
        ToolPermissionMode.AcceptEdits => "accept-edits",
        ToolPermissionMode.BypassPermissions => "bypass",
        _ => "default"
    };

    /// <summary>当前是否正在运行 agent 对话。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 当前挂起的确认块（工具确认 / 权限模式二次确认）。非空时，焦点会被切换到会话区，
    /// 由会话区把按键转发给确认块。置空即表示确认已结束。设置时触发 <see cref="PendingChoiceChanged"/>。
    /// </summary>
    private InlineChoiceBlock? _pendingChoice;

    /// <summary>当前挂起的确认块。</summary>
    public InlineChoiceBlock? PendingChoice
    {
        get => _pendingChoice;
        internal set
        {
            if (_pendingChoice == value) return;
            var oldTitle = _pendingChoice?.Title;
            var newTitle = value?.Title;
            _pendingChoice = value;
            Logger.Warn($"[TuiDiag-PendingChoice] set: old=({(oldTitle is null ? "null" : $"'{oldTitle}'")}) -> new=({(newTitle is null ? "null" : $"'{newTitle}'")}) Selected={(value?.Selected?.Key.ToString() ?? "null")}");
            PendingChoiceChanged?.Invoke();
        }
    }

    /// <summary>确认块挂起状态变化事件（RootView 订阅以切换键盘焦点）。</summary>
    public event Action? PendingChoiceChanged;

    /// <summary>历史回放的消息条数限制。</summary>
    private const int HistoryLoadLimit = 20;

    /// <summary>
    /// 初始化会话 ViewModel。
    /// </summary>
    /// <param name="sessionId">绑定的会话标识。</param>
    /// <param name="services">根级 DI 容器，用于解析全局服务。</param>
    /// <param name="dispatcher">UI 线程调度器。</param>
    /// <param name="doc">会话文档模型。</param>
    /// <param name="spinner">会话级加载指示器。</param>
    public ConversationViewModel(
        string sessionId,
        IServiceProvider services,
        IUiDispatcher dispatcher,
        ConversationDocument doc,
        SpinnerService spinner)
    {
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _spinner = spinner ?? throw new ArgumentNullException(nameof(spinner));
        _configManager = services.GetRequiredService<ConfigManager>();
        _titleService = services.GetRequiredService<TitleService>();
        _iSessionManager = services.GetRequiredService<ISessionManager>();
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        _agentHost = services.GetRequiredService<AgentHostService>();

        _configManager.SelectedModelChanged += OnSelectedModelChanged;
    }

    private void OnSelectedModelChanged(string? model)
    {
        // 模型切换：置空该会话 Agent 缓存，下次对话按最新模型重建；运行中延迟到本轮结束
        if (IsRunning)
        {
            _pendingAgentReset = true;
            return;
        }
        _agentHost.Reset(_sessionId);
    }

    /// <summary>
    /// 请求重载 Agent 上下文（<see cref="IAgentContextReloadTrigger"/> 实现）。
    /// 用于 AGENTS.md 落盘后让新内容进入系统提示词；运行中会被延迟到本轮结束后执行。
    /// </summary>
    public void RequestReload()
    {
        if (IsRunning)
        {
            _pendingAgentReset = true;
            return;
        }
        _agentHost.Reset(_sessionId);
    }

    /// <summary>
    /// 注入页脚视图与数据提供者，使上下文切换能刷新状态栏。
    /// </summary>
    public void SetFooter(FooterView footer, FooterDataProvider provider)
    {
        _footerView = footer;
        _footerProvider = provider;
    }

    /// <summary>
    /// 初始化本会话：解析会话自身绑定的工作区、预热 Agent、加载历史并刷新状态栏/页脚。
    /// 首次对话前调用一次（由 RootView 的会话槽位保证）。
    /// </summary>
    public async Task InitializeAsync()
    {
        // 无会话（未选择工作区时的占位槽位）：给出提示并跳过 Agent 预热
        if (string.IsNullOrEmpty(_sessionId))
        {
            _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                "尚未进入任何会话。请先用 /w -n <路径> 创建工作区，或 /session -new 新建会话。",
                foreground: BlockColors.Accent)));
            return;
        }

        var session = await GetSessionAsync(_sessionId);
        var ws = await ResolveWorkspaceAsync(session);
        var model = _configManager.SelectedModel;
        var title = session?.Title ?? "新会话";
        SessionTitle = title;

        // 预热：按本会话自身绑定的工作区构建 Agent（失败向上抛出，由 RootView 提示）
        await _agentHost.InitializeAsync(_sessionId);

        _titleService.SetWorkspace(ws?.Name ?? "-");
        _titleService.SetModel(model ?? "-");
        _titleService.SetSessionTitle(title);

        if (_footerProvider is not null)
        {
            _footerProvider.WorkspaceName = ws?.Name ?? "-";
            _footerProvider.ModelName = model ?? "-";
            _footerProvider.SessionTitle = title;
        }

        if (!string.IsNullOrEmpty(_sessionId))
        {
            await LoadHistoryAsync(_sessionId);
        }

        _dispatcher.Invoke(() =>
        {
            _doc.AppendBlock(new SystemBlock(
                $"模型: {model ?? "-"}  |  工作区: {ws?.Name ?? "-"}  |  会话: {title}",
                foreground: BlockColors.Success));
            _footerView?.SetNeedsDraw();
        });
    }

    /// <summary>
    /// 取会话实体（用于标题/工作区解析）。
    /// </summary>
    private async Task<DbSession?> GetSessionAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        var repo = _services.GetRequiredService<SessionRepository>();
        return await repo.GetBySessionIdAsync(sessionId);
    }

    /// <summary>
    /// 解析会话自身绑定的工作区；未绑定或已删除时回退到当前工作区。
    /// </summary>
    private async Task<WorkspaceInfo?> ResolveWorkspaceAsync(DbSession? session)
    {
        var workspaceId = session?.WorkspaceId;
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            var ws = await _workspaceManager.GetWorkspaceAsync(workspaceId!);
            if (ws is not null) return ws;
        }
        return _workspaceManager.CurrentWorkspace;
    }

    /// <summary>
    /// 加载会话历史到显示区（最近 N 条 user/assistant 消息）。
    /// </summary>
    public async Task LoadHistoryAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        var messages = await _iSessionManager.GetLatestMessagesAsync(sessionId, HistoryLoadLimit + 1);

        var list = messages.ToList();
        var hasMore = list.Count > HistoryLoadLimit;
        if (hasMore)
        {
            list = list.Take(HistoryLoadLimit).ToList();
        }

        _dispatcher.Invoke(() =>
        {
            if (hasMore)
            {
                _doc.AppendBlock(new SystemBlock(
                    $"…更早消息未显示（已加载最近 {HistoryLoadLimit} 条）",
                    foreground: BlockColors.System));
            }

            foreach (var msg in list)
            {
                if (msg.Role == "user")
                {
                    _doc.AppendBlock(new UserMessageBlock(
                        msg.Content, AttachmentService.DisplayInfosFromJson(msg.Attachments)));
                }
                else if (msg.Role == "assistant")
                {
                    if (!string.IsNullOrWhiteSpace(msg.Thinking))
                    {
                        var tBlock = new ThinkingBlock();
                        tBlock.AppendContent(msg.Thinking);
                        tBlock.MarkComplete();
                        _doc.AppendBlock(tBlock);
                    }
                    var block = new AssistantMessageBlock();
                    block.AppendContent(msg.Content);
                    block.MarkComplete();
                    _doc.AppendBlock(block);
                }
            }
        });
    }

    /// <summary>
    /// 处理用户输入。经 <see cref="AgentHostService"/> 启动本会话流式对话，输出以 Block 形式追加到文档。
    /// </summary>
    /// <param name="input">用户输入文本。</param>
    /// <param name="attachments">附件草稿（含路径与文件名）。</param>
    public async Task ProcessInputAsync(string input, IReadOnlyList<AttachmentDraft>? attachments = null)
    {
        attachments ??= Array.Empty<AttachmentDraft>();

        // 允许在“已请求取消（Esc）”时覆盖旧的挂起运行；否则运行中直接返回，避免并发跑两条对话。
        if (IsRunning && !_cancelRequested) return;
        if (string.IsNullOrWhiteSpace(input) && attachments.Count == 0) return;

        // 作废旧运行并开启新一轮：捕获本次运行序号，供流处理中丢弃迟到事件
        if (_cancelRequested) _cancelRequested = false;
        _deniedByUser = false;
        Interlocked.Exchange(ref _denialMessageShown, 0);
        var runSeq = Interlocked.Increment(ref _activeRunSeq);
        _currentRunSeq = runSeq;

        IsRunning = true;
        TuiDiag.AgentRunning = true;
        _currentCts = new CancellationTokenSource();

        _spinner.Start("AI 正在思考… (Esc 取消)");

        try
        {
            // 工作区授权检查（基于本会话自身绑定的工作区）
            var session = await GetSessionAsync(_sessionId);
            var ws = await ResolveWorkspaceAsync(session);
            await EnsureAuthorizedWithNoticeAsync(ws);

            // 附件预检：含图片且当前模型不支持视觉 → 前置报错，不发送
            var modelId = _configManager.SelectedModel ?? "";
            if (HasUnsupportedImage(modelId, attachments))
            {
                _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                    $"当前模型 {modelId} 不支持图片附件，请切换支持视觉的模型或移除图片。",
                    foreground: BlockColors.Failure)));
                return;
            }

            // 追加用户消息（显示信息由草稿轻量构造，完整处理交由 AgentHostService）
            _dispatcher.Invoke(() => _doc.AppendBlock(
                new UserMessageBlock(input, BuildDisplayInfos(attachments))));

            var attachmentPaths = attachments.Select(a => a.Path).ToList();
            await ConsumeStreamAsync(input, attachmentPaths, _currentCts.Token, runSeq);
        }
        catch (OperationCanceledException)
        {
            if (Interlocked.Exchange(ref _denialMessageShown, 1) == 0)
            {
                var message = _deniedByUser ? "已拒绝，本轮终止" : "任务已取消";
                _dispatcher.Invoke(() =>
                    _doc.AppendBlock(new SystemBlock(message, foreground: BlockColors.Failure)));
            }
        }
        catch (AgentApiException apiEx)
        {
            Logger.Error("Agent 对话异常", apiEx, apiEx.Error.Category.ToString());
            _dispatcher.Invoke(() =>
                _doc.AppendBlock(new SystemBlock(apiEx.Error.FriendlyMessage, foreground: BlockColors.Failure)));
        }
        catch (Exception ex)
        {
            Logger.Error("Agent 对话异常", ex);
            _dispatcher.Invoke(() =>
                _doc.AppendBlock(new SystemBlock($"错误: {ex.Message}", foreground: BlockColors.Failure)));
        }
        finally
        {
            // 仅当本次运行仍是“当前活动运行”时才复位全局态；否则不复位，避免误伤新运行。
            if (runSeq == Volatile.Read(ref _activeRunSeq))
            {
                IsRunning = false;
                TuiDiag.AgentRunning = false;
                _spinner.Stop();
                _currentCts?.Dispose();
                _currentCts = null;
                ReportPlannedActions();

                // 运行期间发生的模型切换 / 上下文重载被延迟，本轮结束后按最新配置重建 Agent
                if (_pendingAgentReset)
                {
                    _pendingAgentReset = false;
                    _agentHost.Reset(_sessionId);
                }
            }
        }
    }

    /// <summary>
    /// 未授权工作区：提交任务前主动弹授权确认；拒绝时提示但继续运行（闲聊不受影响）。
    /// </summary>
    private async Task EnsureAuthorizedWithNoticeAsync(WorkspaceInfo? ws)
    {
        if (ws is null || ws.IsAuthorized) return;
        try
        {
            var authorized = await _workspaceManager.EnsureAuthorizedAsync(ws);
            if (!authorized)
            {
                _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                    "⚠ 当前工作区未授权，涉及文件的操作可能受限。可执行 /work -authorize 授权后重试。",
                    foreground: BlockColors.Accent)));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("工作区授权检查失败", ex);
        }
    }

    // ────────────── 附件显示轻量构造 ──────────────

    private bool HasUnsupportedImage(string modelId, IReadOnlyList<AttachmentDraft> attachments)
        => attachments.Any(a => MediaTypeUtil.IsImage(a.Path)) && !_services.GetModelCapabilityService().SupportsVision(modelId);

    private static IReadOnlyList<AttachmentDisplayInfo> BuildDisplayInfos(IReadOnlyList<AttachmentDraft> attachments)
    {
        if (attachments.Count == 0) return Array.Empty<AttachmentDisplayInfo>();

        var list = new List<AttachmentDisplayInfo>(attachments.Count);
        foreach (var a in attachments)
        {
            long size = 0;
            try
            {
                var fi = new FileInfo(a.Path);
                if (fi.Exists) size = fi.Length;
            }
            catch
            {
                // 文件不存在/不可访问时体积记为 0，不阻断发送（真实处理由 AgentHostService 完成）
            }

            var isImage = MediaTypeUtil.IsImage(a.Path);
            list.Add(new AttachmentDisplayInfo(
                a.FileName, MediaTypeUtil.ResolveMediaType(a.Path, "application/octet-stream"), size, null, isImage, null, a.Path));
        }
        return list;
    }

    /// <summary>
    /// 取消当前对话（Esc）。先作废当前运行序号（使迟到事件/挂起任务被丢弃），再取消令牌；
    /// 并标记 _cancelRequested，使随后到来的新输入可干净地覆盖旧的挂起运行。
    /// </summary>
    public void Cancel()
    {
        _cancelRequested = true;
        Interlocked.Increment(ref _activeRunSeq); // 作废当前运行：其迟到事件/append 将被丢弃

        var cancelledSeq = _currentRunSeq;
        IsRunning = false;
        TuiDiag.AgentRunning = false;
        _spinner.Stop();

        // 先摘除引用再取消：在途旧运行仍持有该 Token（ConfirmTool 曾 ct.Register），
        // 故此处只置空不 Dispose，避免 "已释放的 CancellationTokenSource" 异常。
        var cts = _currentCts;
        _currentCts = null;
        try
        {
            cts?.Cancel();
        }
        catch (Exception ex)
        {
            Logger.Warn("取消当前任务令牌失败", ex);
        }

        // 收尾 UI 状态：完成 spinner/工具块动画，避免动画 Timer 永久运行（一直显示"正在…"）。
        _dispatcher.Invoke(() =>
        {
            if (_currentRunSeq != cancelledSeq) return;

            CompleteSpinner();

            foreach (var pending in _runningToolBlocks)
            {
                if (_deniedByUser)
                    pending.MarkDeniedByUser();
                else
                    pending.MarkCancelled();
            }
            _runningToolBlocks.Clear();

            foreach (var pending in _runningNodeBlocks)
                pending.MarkFailed("已取消", null, null);
            _runningNodeBlocks.Clear();

            _doc.RelayoutLastBlock();
            _doc.MarkLastComplete();
        });
    }

    /// <summary>
    /// 人工确认回调：阻塞宿主回合线程，等待用户在 TUI 内联选择块上作出决定。
    /// 授权记忆（本轮/本会话）由框架 ToolConfirmationService 依据返回值处理，此处仅回传选择。
    /// 后台会话照常在自己的文档弹出确认块并阻塞，不打扰当前视图。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="args">工具参数。</param>
    /// <returns>用户的选择结果。</returns>
    private ConfirmResult ConfirmTool(string toolName, IReadOnlyDictionary<string, object?> args)
    {
        if (TuiDiag.Enabled) Logger.Warn($"[TuiDiag] confirm enter: {toolName}");

        using var done = new ManualResetEventSlim(false);
        var result = ConfirmResult.Deny;

        var ct = _currentCts?.Token ?? default;
        CancellationTokenRegistration ctr = default;
        if (ct.CanBeCanceled)
        {
            ctr = ct.Register(() => done.Set());
        }

        _dispatcher.Invoke(() =>
        {
            // 运行已作废（Esc/新对话）：不弹确认块；必须唤醒等待（结果保持 Deny），否则无限挂起
            if (_currentRunSeq != Volatile.Read(ref _activeRunSeq))
            {
                done.Set();
                return;
            }

            var confirmBlock = ChoiceBlocks.Confirm(toolName, args, cr =>
            {
                result = cr;
                PendingChoice = null;
                done.Set();
            });
            _doc.AppendBlock(confirmBlock);
            PendingChoice = confirmBlock;
        });

        // 等待用户选择（无限期；Esc/终止时经取消令牌注册提前唤醒）
        done.Wait();
        ctr.Dispose();
        PendingChoice = null;

        if (TuiDiag.Enabled) Logger.Warn($"[TuiDiag] confirm exit: {toolName} -> {result}");
        return result;
    }

    /// <summary>
    /// Plan 模式回合结束时汇总：本轮计划项均未执行，提示切换模式后重新发起。
    /// </summary>
    private void ReportPlannedActions()
    {
        var count = Volatile.Read(ref _plannedCount);
        if (PermissionMode != ToolPermissionMode.Plan || count == 0) return;

        _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
            $"📋 Plan 模式：本轮记录 {count} 个计划项，均未执行。"
            + "按 Shift+Tab 切到 default / accept-edits 后重新发起即可执行。",
            foreground: BlockColors.Accent)));
    }

    /// <summary>
    /// 运行流式对话循环——消费 <see cref="AgentHostService"/> 输出的 <see cref="StreamEvent"/> 序列并转换为 Block。
    /// 流式文本 token 先入缓冲并按 50ms 节流合批编组到 UI 线程；工具调用/结果先冲刷缓冲再追加，保证文档顺序。
    /// </summary>
    private async Task ConsumeStreamAsync(string input, IReadOnlyList<string> attachmentPaths, CancellationToken ct, int runSeq)
    {
        _currentRunSeq = runSeq;

        // 重置当前会话的流式状态（字段级，供跨会话复用的节流回调使用）
        _thinkingBlock = null;
        _thinkingCompleted = false;
        _currentThinkingRound = 0;
        _currentSpinner = null;
        _runningToolBlocks.Clear();
        // 上一轮若异常退出可能残留未收尾的节点块：先停掉动画再清空，避免 Timer 泄漏
        foreach (var pending in _runningNodeBlocks)
            pending.MarkFailed("已中断", null, null);
        _runningNodeBlocks.Clear();
        Interlocked.Exchange(ref _plannedCount, 0);

        _streamThrottle ??= new FlushThrottle(() => FlushPendingTokens(), TimeSpan.FromMilliseconds(50));

        // 立即反馈：提交后立刻插入 spinner，避免 planner/编排等非流式阶段长时间"死屏"
        _dispatcher.Invoke(() =>
        {
            if (_currentSpinner is null)
            {
                _currentSpinner = new ActionSpinnerBlock("AI 正在思考… (Esc 取消)", _doc, _dispatcher);
                _doc.AppendBlock(_currentSpinner);
            }
        });

        bool firstToken = false;
        if (TuiDiag.Enabled)
        {
            _ = Task.Delay(20000).ContinueWith(_ =>
            {
                if (!firstToken)
                    Logger.Warn("[TuiDiag-Watchdog] PI: 20s 内无任何流 token —— 很可能卡在 agent/LLM/工具调用（非 UI 线程）");
            });
        }

        try
        {
            await foreach (var evt in _agentHost.RunStreamingAsync(
                input,
                attachmentPaths,
                ConfirmTool,
                PermissionMode,
                onPlannedAction: OnPlannedAction,
                onUserDenied: OnUserDenied,
                sessionId: _sessionId,
                ct: ct))
            {
                // 运行已被取消/被新对话覆盖：停止消费流，丢弃后续（迟到的旧事件）
                if (runSeq != Volatile.Read(ref _activeRunSeq))
                {
                    if (TuiDiag.Enabled)
                        Logger.Warn($"[TuiDiag] stream stale aborted: runSeq={runSeq} active={Volatile.Read(ref _activeRunSeq)}");
                    break;
                }

                if (!firstToken)
                {
                    firstToken = true;
                    if (TuiDiag.Enabled) Logger.Warn("[TuiDiag-Phase] PI: first stream event received");
                }

                if (TuiDiag.Enabled)
                    Logger.Warn($"[TuiDiag] event: {evt.GetType().Name}");

                HandleStreamEvent(evt);
            }

            if (TuiDiag.Enabled) Logger.Warn("[TuiDiag] stream completed normally");
        }
        finally
        {
            // 仅当仍是当前活动运行才操作文档；否则（已被取消/覆盖）跳过，避免污染新运行
            var stale = _currentRunSeq != Volatile.Read(ref _activeRunSeq);
            if (!stale)
            {
                // 取消/异常路径也冲刷剩余 token，保证已产出内容不丢失；并关闭最后的思考块
                CloseThinkingBlock();

                _dispatcher.Invoke(() =>
                {
                    CompleteSpinner();

                    // 流结束时仍有工具块在执行（流中断/取消/结果缺失），全部标记失败：
                    // 既避免误显"完成"，也避免残留块的动画 Timer 永久运行。
                    foreach (var pending in _runningToolBlocks)
                        pending.MarkFailed("流中断");
                    _runningToolBlocks.Clear();

                    foreach (var pending in _runningNodeBlocks)
                        pending.MarkFailed("流中断", null, null);
                    _runningNodeBlocks.Clear();

                    // 补一次最终布局：合批期间追加的尾部 token 需要进入 LineCount/TotalLines 账本
                    _doc.RelayoutLastBlock();
                    _doc.MarkLastComplete();
                });
            }
            else if (TuiDiag.Enabled)
            {
                Logger.Warn($"[TuiDiag] ConsumeStream finally stale: current={_currentRunSeq} active={Volatile.Read(ref _activeRunSeq)} —— 跳过文档清理");
            }
        }
    }

    /// <summary>
    /// Plan 模式回调：上报一个未执行的计划项。
    /// </summary>
    private void OnPlannedAction(string tool, IReadOnlyDictionary<string, object?> args)
    {
        if (_currentRunSeq != Volatile.Read(ref _activeRunSeq)) return;
        Interlocked.Increment(ref _plannedCount);
        _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
            $"  📋 计划项（Plan 模式，未执行）: {tool} {ToolArgsFormatter.Summarize(args)}",
            foreground: BlockColors.Thinking)));
    }

    /// <summary>
    /// 用户明确拒绝工具调用回调：标记并立即终止本轮。
    /// </summary>
    private void OnUserDenied(string tool, IReadOnlyDictionary<string, object?> args)
    {
        if (_currentRunSeq != Volatile.Read(ref _activeRunSeq)) return;

        _deniedByUser = true;
        Logger.Warn($"用户拒绝工具调用，终止本轮: {tool}");

        if (Interlocked.Exchange(ref _denialMessageShown, 1) == 0)
        {
            _dispatcher.Invoke(() => _doc.AppendBlock(
                new SystemBlock("已拒绝，本轮终止", foreground: BlockColors.Failure)));
        }

        Cancel();
    }

    /// <summary>
    /// 处理单个流式事件，转换为 Block 追加。
    /// </summary>
    private void HandleStreamEvent(StreamEvent evt)
    {
        switch (evt)
        {
            // ─── 思考过程（跨轮边界收尾上一轮思考块）───
            case ThinkingDeltaEvent t:
                if (t.LlmRound > 0 && t.LlmRound != _currentThinkingRound)
                {
                    CloseThinkingBlock();
                    _currentThinkingRound = t.LlmRound;
                }
                EnsureSpinnerOnUi("AI 正在思考…");
                lock (_streamLock) _pendingThinking.Append(t.Delta);
                _streamThrottle!.Schedule();
                break;

            // ─── 正文回复 ───
            case TextDeltaEvent t:
                EnsureSpinnerOnUi("正在生成回复…");
                lock (_streamLock) _pendingAnswer.Append(t.Delta);
                _streamThrottle!.Schedule();
                break;

            // ─── 工具调用 ───
            case ToolCallStartedEvent tc:
                CloseThinkingBlock();
                _dispatcher.Invoke(() =>
                {
                    CompleteSpinner();
                    var toolBlock = new ToolCallBlock(tc.Name, tc.CallId, _doc, _dispatcher);
                    _doc.AppendBlock(toolBlock);
                    _runningToolBlocks.Add(toolBlock);
                });
                break;

            // ─── 工具结果 ───
            case ToolCallCompletedEvent tcc:
                _dispatcher.Invoke(() => ResolveToolBlock(tcc.CallId, ToolOutcome.Completed, null));
                break;

            case ToolCallFailedEvent tcf:
                _dispatcher.Invoke(() =>
                {
                    if (_deniedByUser)
                    {
                        ResolveToolBlock(tcf.CallId, ToolOutcome.Denied, null);
                    }
                    else if (tcf.Error == "工具调用被拒绝或已取消")
                    {
                        ResolveToolBlock(tcf.CallId, ToolOutcome.Cancelled, null);
                    }
                    else
                    {
                        ResolveToolBlock(tcf.CallId, ToolOutcome.Failed, tcf.Error);
                        _doc.AppendBlock(new SystemBlock(
                            $"❌ 工具执行失败: {tcf.Error}", foreground: BlockColors.Failure));
                    }
                });
                break;

            // ─── 流内错误 ───
            case ErrorEvent e:
                CloseThinkingBlock();
                Logger.Warn($"[TuiDiag] ErrorEvent: {e.Message}");
                _dispatcher.Invoke(() => _doc.AppendBlock(
                    new SystemBlock($"错误: {e.Message}", foreground: BlockColors.Failure)));
                break;

            // ─── 编排进度（本会话流内事件，天然按会话隔离）───
            case OrchestrationProgressEvent p:
                HandleOrchestrationProgress(p);
                break;

            // ─── 附件已加入 / 其余事件：无需渲染 ───
        }
    }

    private enum ToolOutcome { Completed, Failed, Denied, Cancelled }

    /// <summary>
    /// 按 CallId 匹配运行中的工具块并收尾。须在 UI 线程调用。
    /// </summary>
    private void ResolveToolBlock(string? callId, ToolOutcome outcome, string? error)
    {
        var target = !string.IsNullOrEmpty(callId)
            ? _runningToolBlocks.FirstOrDefault(b => string.Equals(b.CallId, callId, StringComparison.Ordinal))
            : _runningToolBlocks.FirstOrDefault(b => !b.IsComplete);

        if (target is null) return;

        switch (outcome)
        {
            case ToolOutcome.Failed: target.MarkFailed(error); break;
            case ToolOutcome.Denied: target.MarkDeniedByUser(); break;
            case ToolOutcome.Cancelled: target.MarkCancelled(); break;
            default: target.MarkComplete(); break;
        }
        _runningToolBlocks.Remove(target);
    }

    /// <summary>
    /// 若尚无 spinner 则在 UI 线程插入一个（避免 planner/编排等非流式阶段死屏）。
    /// </summary>
    private void EnsureSpinnerOnUi(string description)
    {
        if (_currentSpinner is not null) return;
        _dispatcher.Invoke(() =>
        {
            if (_currentSpinner is null)
            {
                _currentSpinner = new ActionSpinnerBlock(description, _doc, _dispatcher);
                _doc.AppendBlock(_currentSpinner);
            }
        });
    }

    /// <summary>
    /// 处理编排进度事件：逐行追加系统提示，让规划/节点执行等长耗时阶段有实时反馈。
    /// 规划阶段复用等待中的 spinner 并切换文案；节点执行阶段每节点一条独立动画行，
    /// 由对应完成/失败事件就地收尾。
    /// </summary>
    private void HandleOrchestrationProgress(OrchestrationProgressEvent progress)
    {
        _dispatcher.Invoke(() =>
        {
            // 运行已作废（Esc/新对话）：丢弃迟到进度，避免污染新对话
            if (_currentRunSeq != Volatile.Read(ref _activeRunSeq)) return;

            // 规划阶段：复用等待中的动画 spinner，只切换阶段文案
            if (progress.EventType == ProgressEventType.PlanningStarted)
            {
                if (_currentSpinner is null)
                {
                    _currentSpinner = new ActionSpinnerBlock("正在规划任务…", _doc, _dispatcher);
                    _doc.AppendBlock(_currentSpinner);
                }
                else
                {
                    _currentSpinner.SetDescription("正在规划任务…");
                }
                return;
            }

            // 节点内部活动：路由到对应运行中的节点块
            if (progress.EventType == ProgressEventType.NodeActivity)
            {
                if (progress.Activity is null) return;

                var target = !string.IsNullOrEmpty(progress.NodeId)
                    ? _runningNodeBlocks.FirstOrDefault(b => string.Equals(b.NodeId, progress.NodeId, StringComparison.Ordinal))
                    : _runningNodeBlocks.LastOrDefault(b => !b.IsComplete);

                target?.AppendActivity(progress.Activity);
                return;
            }

            // 节点开始：收尾规划 spinner，插入该节点的独立动画行
            if (progress.EventType == ProgressEventType.NodeStarted)
            {
                CompleteSpinner();

                var nodeId = progress.NodeId ?? "";
                var description = progress.Message ?? (string.IsNullOrEmpty(nodeId) ? "子任务" : nodeId);
                var nodeBlock = new OrchestrationNodeBlock(nodeId, description, _doc, _dispatcher);
                _doc.AppendBlock(nodeBlock);
                _runningNodeBlocks.Add(nodeBlock);
                return;
            }

            // 节点完成/失败/跳过：就地收尾对应动画行（该行保留为历史），并行节点互不影响
            if (progress.EventType is ProgressEventType.NodeCompleted or ProgressEventType.NodeFailed or ProgressEventType.NodeSkipped)
            {
                CompleteSpinner();

                var target = !string.IsNullOrEmpty(progress.NodeId)
                    ? _runningNodeBlocks.FirstOrDefault(b => string.Equals(b.NodeId, progress.NodeId, StringComparison.Ordinal))
                    : _runningNodeBlocks.FirstOrDefault(b => !b.IsComplete);

                if (target is not null)
                {
                    var elapsed = progress.ElapsedSeconds;
                    switch (progress.EventType)
                    {
                        case ProgressEventType.NodeFailed:
                            target.MarkFailed(progress.Error ?? progress.Message ?? "失败", elapsed, progress.Message);
                            break;
                        case ProgressEventType.NodeSkipped:
                            target.MarkSkipped(progress.Message);
                            break;
                        default:
                            target.MarkSucceeded(elapsed, progress.Message);
                            break;
                    }
                    _runningNodeBlocks.Remove(target);
                    return;
                }

                // 未匹配到活动行（如因前驱失败被跳过、未发 NodeStarted）：退化为单行摘要
                AppendNodeSummary(progress);
                return;
            }

            // 反思重规划：复用等待中的动画 spinner 并切换文案
            if (progress.EventType == ProgressEventType.ReflectionStarted)
            {
                var description = progress.Message ?? "正在反思重规划…";
                if (_currentSpinner is null)
                {
                    _currentSpinner = new ActionSpinnerBlock(description, _doc, _dispatcher);
                    _doc.AppendBlock(_currentSpinner);
                }
                else
                {
                    _currentSpinner.SetDescription(description);
                }
                return;
            }

            string? text = progress.EventType switch
            {
                ProgressEventType.PlanningCompleted => $" {progress.Message ?? "任务图谱已生成"}",
                _ => null
            };

            CompleteSpinner();
            if (text is null) return;

            _doc.AppendBlock(new SystemBlock(text, foreground: BlockColors.System));
        });
    }

    /// <summary>
    /// 为未匹配到活动动画行的节点追加单行摘要。依据 EventType 区分成功/跳过/失败。
    /// 须在 UI 线程调用。
    /// </summary>
    private void AppendNodeSummary(OrchestrationProgressEvent progress)
    {
        var name = progress.Message ?? progress.NodeId ?? "子任务";
        var elapsed = progress.ElapsedSeconds;

        var (text, color) = progress.EventType switch
        {
            ProgressEventType.NodeSkipped => ($"− {name} · 已跳过", BlockColors.System),
            ProgressEventType.NodeFailed => ($"✗ {name} · {progress.Error ?? "失败"}", BlockColors.Failure),
            _ => ($"✓ {name} · {elapsed.GetValueOrDefault():F1}s", BlockColors.Success)
        };

        _doc.AppendBlock(new SystemBlock(text, foreground: color));
    }

    /// <summary>
    /// 完成并清空当前 spinner（若存在）。须在 UI 线程调用。
    /// </summary>
    private void CompleteSpinner()
    {
        if (_currentSpinner is null) return;
        _currentSpinner.MarkComplete();
        _currentSpinner = null;
    }

    /// <summary>
    /// 关闭当前思考块：先冲刷缓冲的思考 token，再将思考块标记为完成并置空，
    /// 使下一轮思考（工具调用后的第二轮 CoT）能创建新块并追加到当前位置，实现顺序交织。
    /// </summary>
    private void CloseThinkingBlock()
    {
        FlushPendingTokens();

        if (_thinkingBlock is not null)
        {
            if (!_thinkingCompleted)
            {
                _thinkingBlock.MarkComplete();
                _doc.NotifyBlockChanged(_thinkingBlock);
            }
            _thinkingBlock = null;
            _thinkingCompleted = false;
        }
    }

    /// <summary>
    /// 将缓冲的思考/正文 token 一次性编组到 UI 线程追加。
    /// 实例方法（非闭包）：会话状态通过字段访问，避免跨会话状态泄漏。
    /// </summary>
    private void FlushPendingTokens()
    {
        string thinking;
        string answer;

        lock (_streamLock)
        {
            thinking = _pendingThinking.ToString();
            _pendingThinking.Clear();
            answer = _pendingAnswer.ToString();
            _pendingAnswer.Clear();
        }

        if (thinking.Length == 0 && answer.Length == 0) return;

        // 运行已被作废（Esc/新对话）：丢弃缓冲内容，不再追加到文档，避免污染新对话
        if (_currentRunSeq != Volatile.Read(ref _activeRunSeq))
        {
            if (TuiDiag.Enabled)
                Logger.Warn($"[TuiDiag] FlushPendingTokens stale: current={_currentRunSeq} active={Volatile.Read(ref _activeRunSeq)} dropped={thinking.Length + answer.Length}");
            return;
        }

        TuiDiag.Record("StreamFlush.chars", thinking.Length + answer.Length, thresholdMs: 0);

        using var flushScope = TuiDiag.Measure("StreamFlush.total", thresholdMs: 0);

        _dispatcher.Invoke(() =>
        {
            using var invokeScope = TuiDiag.Measure("StreamFlush.Invoke", thresholdMs: 0);

            if (thinking.Length > 0)
            {
                if (_thinkingBlock is null)
                {
                    _thinkingBlock = new ThinkingBlock();
                    _doc.AppendBlock(_thinkingBlock);
                }

                _thinkingBlock.AppendContent(thinking);
                _doc.NotifyBlockChanged(_thinkingBlock);
            }

            if (answer.Length > 0)
            {
                // 正文出现仅标记思考块“已完成”（停指示），但不清空引用：
                // 同一轮内紧随其后的思考仍并入同一块；思考块只在轮次边界才收尾另起。
                if (_thinkingBlock is not null && !_thinkingCompleted)
                {
                    _thinkingCompleted = true;
                    _thinkingBlock.MarkComplete();
                    _doc.NotifyBlockChanged(_thinkingBlock);
                }

                _doc.AppendToAnswerBlock(answer);
                _doc.RelayoutLastBlock();
            }
        });
    }

    /// <summary>
    /// 取消事件订阅，释放资源。
    /// </summary>
    public void Dispose()
    {
        _configManager.SelectedModelChanged -= OnSelectedModelChanged;
        _streamThrottle?.Dispose();
        _streamThrottle = null;
        _currentCts?.Cancel();
        _currentCts?.Dispose();
        _currentCts = null;
        _spinner.Dispose();
    }
}