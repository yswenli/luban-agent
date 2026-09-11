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
*描述：顶层容器视图，创建 ConversationDocument 与 ConversationViewModel，
*划分三区域布局并承载全局快捷键与输入提交
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
/// 顶层容器视图。持有 Document + ViewModel + 三个子 View，
/// 负责全局快捷键、输入提交派发与 agent 生命周期协调。
/// </summary>
internal sealed class RootView : Runnable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ITuiUiService _ui;
    private readonly ConversationDocument _doc;
    private readonly ConversationViewModel _vm;
    private readonly CommandViewModel _commandVm;
    private readonly AgentViewViewModel _agentVm;
    private readonly ConversationView _conversation;
    private readonly FooterView _footer;
    private readonly InputBarView _inputBar;
    private bool _vmInitialized;
    private volatile bool _initializing;

    // 待执行队列：Agent 运行期间提交的输入在此排队，本轮结束后顺序执行
    private readonly System.Collections.Generic.Queue<string> _inputQueue = new();
    private volatile bool _runActive;
    private readonly Action<ToolPermissionMode> _onPermissionModeChanged;
    private readonly Action _onExitRequested;
    private Terminal.Gui.App.IKeyboard? _keyboard;
    private EventHandler<Terminal.Gui.Input.Key>? _onGlobalKeyDown;

    /// <summary>
    /// 初始化顶层容器、文档模型、ViewModel 与三区域布局。
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

        _dispatcher = dispatcher;
        _ui = ui;
        _doc = new ConversationDocument();
        _vm = new ConversationViewModel(services, dispatcher, _doc);
        _commandVm = new CommandViewModel(_doc, _vm, services, dispatcher, ui);
        _onExitRequested = ConfirmExit;
        _commandVm.ExitRequested += _onExitRequested;
        _agentVm = new AgentViewViewModel(new TaskRegistry(), _doc);

        // 启动横幅
        _doc.AppendBlock(new SystemBlock("✻ LuBan Agent CLI", isBold: true, foreground: BlockColors.Accent));
        _doc.AppendBlock(new SystemBlock(
            "  Enter 发送，Shift+Enter 或 Ctrl+Enter 换行，/exit 退出，Ctrl+Q 退出，Esc 取消，Ctrl+L 重绘，Shift+Tab 切换模式。"));
        _doc.AppendBlock(new SystemBlock("  首次输入前将自动初始化 Agent..."));

        if (startupNotices is not null)
        {
            foreach (var notice in startupNotices)
            {
                _doc.AppendBlock(new SystemBlock(notice));
            }
        }

        _doc.AppendBlock(new SystemBlock(string.Empty));

        // 会话区：从顶部开始，高度=填充到底部（footer 1 + inputBar 4 = 5）
        _conversation = new ConversationView(_doc, _vm)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(5)
        };

        _footer = new FooterView
        {
            X = 0, Y = Pos.Bottom(_conversation), Width = Dim.Fill(), Height = 1
        };
        var footerProvider = new FooterDataProvider();
        _footer.SetProvider(footerProvider);
        _footer.SetMode(_vm.PermissionModeDisplay);
        _vm.SetFooter(_footer, footerProvider);

        // 输入栏初始高度=4（2行内容 + 边框上下各1），随内容视觉折行动态扩展
        _inputBar = new InputBarView
        {
            X = 0, Y = Pos.Bottom(_footer), Width = Dim.Fill(), Height = 4
        };
        _inputBar.Submitted += OnInputSubmitted;
        // 当存在挂起的确认块（工具确认 / 授权二次确认）时，把输入编辑器的按键优先转发给它：
        // agent 阻塞等待期间焦点仍在输入编辑器，确认块本身收不到键，必须由这里转发，否则会等到超时。
        _inputBar.KeyPreRouter = key =>
        {
            var pc = _vm.PendingChoice;
            return pc is not null && pc.Selected is null && pc.HandleKey(key);
        };
        _inputBar.OnPreRoutedKey = () => _conversation.SetNeedsDraw();
        _onPermissionModeChanged = mode => _footer.SetMode(_vm.PermissionModeDisplay);
        _vm.PermissionModeChanged += _onPermissionModeChanged;
        _vm.PendingChoiceChanged += OnPendingChoiceChanged;

        Add(_conversation, _footer, _inputBar);
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
    /// Application 主循环开始运行后，将焦点设置到输入栏。
    /// </summary>
    private void OnIsRunningChanged(object? sender, EventArgs e)
    {
        if (IsRunning)
        {
            _inputBar.FocusInput();
            IsRunningChanged -= OnIsRunningChanged;
        }
    }

    /// <summary>
    /// 确认块挂起状态变化：挂起时聚焦会话区（会话区可把按键转发给确认块），
    /// 结束后聚焦回输入编辑器（便于继续输入/排队）。
    /// 修复确认块因焦点缺失而收不到键、导致 2 分钟超时的问题。
    /// </summary>
    private void OnPendingChoiceChanged()
    {
        _dispatcher.Invoke(() =>
        {
            if (_vm.PendingChoice is not null)
            {
                _conversation.SetFocus();
            }
            else
            {
                _inputBar.FocusInput();
            }
        });
    }

    /// <summary>会话文档模型。</summary>
    public ConversationDocument Document => _doc;

    /// <summary>会话 ViewModel。</summary>
    public ConversationViewModel ViewModel => _vm;

    /// <summary>会话区视图。</summary>
    public ConversationView Conversation => _conversation;

    /// <summary>页脚视图。</summary>
    public FooterView Footer => _footer;

    /// <summary>输入区视图。</summary>
    public InputBarView InputBar => _inputBar;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_keyboard is not null && _onGlobalKeyDown is not null)
            {
                _keyboard.KeyDown -= _onGlobalKeyDown;
                _keyboard = null;
                _onGlobalKeyDown = null;
            }
            _inputBar.Submitted -= OnInputSubmitted;
            _commandVm.ExitRequested -= _onExitRequested;
            _vm.PermissionModeChanged -= _onPermissionModeChanged;
            _vm.PendingChoiceChanged -= OnPendingChoiceChanged;
            _vm.Dispose();
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

        if (_vm.IsRunning)
        {
            _doc.AppendBlock(new SystemBlock("Agent 运行中无法切换权限模式"));
            return;
        }

        var newMode = _vm.CyclePermissionMode();

        // BypassPermissions 需二次确认
        if (newMode == ToolPermissionMode.BypassPermissions)
        {
            var confirmBlock = ChoiceBlocks.BypassConfirm(confirmed =>
            {
                _vm.PendingChoice = null;
                if (!confirmed)
                {
                    _vm.SetPermissionMode(ToolPermissionMode.Default);
                }
                _doc.AppendBlock(new SystemBlock(
                    confirmed ? "⚠ Bypass Permissions 已启用" : "已恢复 Default 模式",
                    foreground: confirmed ? BlockColors.Failure : BlockColors.Success));
            });
            _doc.AppendBlock(confirmBlock);
            _vm.PendingChoice = confirmBlock;
            return;
        }

        _doc.AppendBlock(new SystemBlock(
            $"权限模式: {_vm.PermissionModeDisplay}", foreground: BlockColors.Accent));
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
            _doc.AppendBlock(new SystemBlock(label, foreground: BlockColors.Accent, isBold: true));
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            HandleShiftTab(key);
            return true;
        }

        if (key == Key.Esc)
        {
            if (_vm.IsRunning)
            {
                _vm.Cancel();
                _doc.AppendBlock(new SystemBlock("⌛ 正在取消当前任务...", foreground: BlockColors.Accent));
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
        var message = _vm.IsRunning
            ? "Agent 正在运行中，退出将中断当前任务。\n确定要退出吗？"
            : "确定要退出 LuBan Agent CLI 吗？";

        if (_ui.Confirm("退出确认", message))
        {
            RequestStop();
        }
    }

    // ── 输入提交 ──

    /// <summary>
    /// 处理用户提交输入：运行期间（或一次提交正在派发 / 初始化中）入队排队，否则立即执行。
    /// 首次输入会先初始化 Agent。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    private void OnInputSubmitted(string text)
    {
        if (string.Equals(text, "/exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "/quit", StringComparison.OrdinalIgnoreCase))
        {
            ConfirmExit();
            return;
        }

        // Agent 运行期间（或一次提交正在派发 / 初始化中）：所有输入（命令与对话）进入待执行队列，
        // 本轮结束后自动顺序执行。避免打断在途流式对话，也避免重复提交被静默丢弃。
        if (_runActive || _vm.IsRunning || _initializing)
        {
            _inputQueue.Enqueue(text);
            var preview = PreviewInput(text);
            _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                $"🕓 已加入待执行队列（共 {_inputQueue.Count} 条）: {preview}",
                foreground: BlockColors.Accent)));
            return;
        }

        // `/` 命令路由给 CommandViewModel
        if (text.StartsWith('/'))
        {
            if (_commandVm.TryExecute(text))
            {
                return;
            }
        }

        SubmitAsync(text);
    }

    /// <summary>为待执行队列生成单行预览文本。</summary>
    private static string PreviewInput(string text)
    {
        var t = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length > 40 ? t[..40] + "…" : t;
    }

    /// <summary>
    /// 提交一条输入到 Agent：首次输入会先初始化 Agent，之后直接执行流式对话。
    /// 全程在后台线程运行，确保 UI 线程始终自由（渲染、键盘路由、队列不受影响）。
    /// </summary>
    private void SubmitAsync(string text)
    {
        // 标记“提交派发中”，防止在 Agent 真正进入运行态(IsRunning)前的空隙里重复派发
        _runActive = true;

        if (!_vmInitialized)
        {
            if (_initializing)
            {
                _runActive = false;
                _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock("Agent 正在初始化中，请稍候...")));
                return;
            }

            _initializing = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _vm.InitializeAsync();
                    _vmInitialized = true;
                    await _vm.ProcessInputAsync(text);
                }
                catch (Exception ex)
                {
                    Logger.Error("InitializeAndProcessAsync 异常", ex);
                    _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                        $"Agent 初始化失败: {ex.Message}", foreground: BlockColors.Failure)));
                }
                finally
                {
                    _initializing = false;
                    DrainQueue();
                }
            });
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _vm.ProcessInputAsync(text);
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                    $"Agent 异常: {msg}", foreground: BlockColors.Failure)));
            }
            finally
            {
                DrainQueue();
            }
        });
    }

    /// <summary>
    /// 当前一轮对话结束后，若待执行队列非空则顺序执行下一条；否则释放“派发中”标志。
    /// 递归触发，保证队列中的命令/对话按提交顺序依次执行。
    /// </summary>
    private void DrainQueue()
    {
        if (_inputQueue.TryDequeue(out var next))
        {
            SubmitAsync(next);
        }
        else
        {
            _runActive = false;
        }
    }
}
