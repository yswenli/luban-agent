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
using System.Diagnostics;
using LuBan.AIAgent.Abstractions;
using LubanAgent.App;
using LubanAgent.Models;
using LubanAgent.Models.Blocks;
using LubanAgent.ViewModels;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgent.Views;

/// <summary>
/// 顶层容器视图。持有 Document + ViewModel + 三个子 View，
/// 负责全局快捷键、输入提交派发与 agent 生命周期协调。
/// </summary>
internal sealed class RootView : Runnable
{
    // DIAGNOSTIC: 全局性能计时器，排查 3s/字符输入延迟用，排查完成后移除
    public static readonly Stopwatch PerfWatch = Stopwatch.StartNew();
    private static long _lastKeyMs;

    private readonly IUiDispatcher _dispatcher;
    private readonly ConversationDocument _doc;
    private readonly ConversationViewModel _vm;
    private readonly CommandViewModel _commandVm;
    private readonly AgentViewViewModel _agentVm;
    private readonly ConversationView _conversation;
    private readonly FooterView _footer;
    private readonly InputBarView _inputBar;
    private bool _vmInitialized;
    private readonly Action<ToolPermissionMode> _onPermissionModeChanged;
    private readonly Action _onExitRequested;

    /// <summary>
    /// 初始化顶层容器、文档模型、ViewModel 与三区域布局。
    /// </summary>
    /// <param name="services">根级 DI 容器。</param>
    /// <param name="dispatcher">UI 线程调度器。</param>
    /// <param name="startupNotices">启动提示。</param>
    public RootView(
        IServiceProvider services,
        IUiDispatcher dispatcher,
        IReadOnlyList<string>? startupNotices = null)
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        SetScheme(TuiTheme.BuildScheme());

        _dispatcher = dispatcher;
        _doc = new ConversationDocument();
        _vm = new ConversationViewModel(services, dispatcher, _doc);
        _commandVm = new CommandViewModel(_doc, _vm, services);
        _onExitRequested = () => RequestStop();
        _commandVm.ExitRequested += _onExitRequested;
        _agentVm = new AgentViewViewModel(new TaskRegistry(), _doc);

        // 启动横幅
        _doc.AppendBlock(new SystemBlock("✻ LuBan Agent CLI", isBold: true, foreground: BlockColors.Accent));
        _doc.AppendBlock(new SystemBlock(
            "  输入内容后回车发送，/exit 退出，Ctrl+Q 强退，Esc 取消，Ctrl+L 重绘，Shift+Tab 切换模式。"));
        _doc.AppendBlock(new SystemBlock("  首次输入前将自动初始化 Agent..."));

        if (startupNotices is not null)
        {
            foreach (var notice in startupNotices)
            {
                _doc.AppendBlock(new SystemBlock(notice));
            }
        }

        _doc.AppendBlock(new SystemBlock(string.Empty));

        _conversation = new ConversationView(_doc)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2)
        };

        _footer = new FooterView
        {
            X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 1
        };
        var footerProvider = new LubanAgent.Services.FooterDataProvider();
        _footer.SetProvider(footerProvider);
        _footer.SetMode(_vm.PermissionModeDisplay);

        _inputBar = new InputBarView
        {
            X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1
        };
        _inputBar.Submitted += OnInputSubmitted;
        _onPermissionModeChanged = mode => _footer.SetMode(_vm.PermissionModeDisplay);
        _vm.PermissionModeChanged += _onPermissionModeChanged;

        Add(_conversation, _footer, _inputBar);
    }

    /// <inheritdoc/>
    public override void EndInit()
    {
        base.EndInit();
        _inputBar.FocusInput();
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
            _inputBar.Submitted -= OnInputSubmitted;
            _commandVm.ExitRequested -= _onExitRequested;
            _vm.PermissionModeChanged -= _onPermissionModeChanged;
        }
        base.Dispose(disposing);
    }

    // ── 全局快捷键 ──

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        // DIAGNOSTIC: 记录每次按键的时间戳和与前一次的间隔
        var now = PerfWatch.ElapsedMilliseconds;
        var gap = _lastKeyMs > 0 ? now - _lastKeyMs : 0;
        _lastKeyMs = now;
        var msg = $"[Perf] {now}ms KeyDown:{key} gap={gap}ms";
        Console.Error.WriteLine(msg);
        Logger.Error(msg);

        if (key == Key.Q.WithCtrl)
        {
            RequestStop();
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
            if (_vm.IsRunning)
            {
                _doc.AppendBlock(new SystemBlock("Agent 运行中无法切换权限模式"));
                return true;
            }

            var newMode = _vm.CyclePermissionMode();

            // BypassPermissions 需二次确认
            if (newMode == ToolPermissionMode.BypassPermissions)
            {
                var confirmBlock = ChoiceBlocks.BypassConfirm(confirmed =>
                {
                    if (!confirmed)
                    {
                        _vm.SetPermissionMode(ToolPermissionMode.Default);
                    }
                    _doc.AppendBlock(new SystemBlock(
                        confirmed ? "⚠ Bypass Permissions 已启用" : "已恢复 Default 模式",
                        foreground: confirmed ? BlockColors.Failure : BlockColors.Success));
                });
                _doc.AppendBlock(confirmBlock);
                return true;
            }

            _doc.AppendBlock(new SystemBlock(
                $"权限模式: {_vm.PermissionModeDisplay}", foreground: BlockColors.Accent));
            return true;
        }

        if (key == Key.Esc)
        {
            if (_vm.IsRunning)
            {
                _vm.Cancel();
                _doc.AppendBlock(new SystemBlock("⌛ 正在取消当前任务...", foreground: BlockColors.Accent));
                return true;
            }
        }

        return base.OnKeyDown(key);
    }

    // ── 输入提交 ──

    /// <summary>
    /// 处理用户提交输入：路由命令、初始化 Agent 或执行流式对话。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    private void OnInputSubmitted(string text)
    {
        if (string.Equals(text, "/exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "/quit", StringComparison.OrdinalIgnoreCase))
        {
            RequestStop();
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

        if (_vm.IsRunning)
        {
            _doc.AppendBlock(new SystemBlock("Agent 正在运行中，请等待完成或按 Esc 取消"));
            return;
        }

        // 首次输入时初始化 Agent
        if (!_vmInitialized)
        {
            _ = InitializeAndProcessAsync(text).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    var msg = task.Exception?.InnerException?.Message ?? "未知错误";
                    _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                        $"Agent 异常: {msg}",
                        foreground: BlockColors.Failure)));
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            _ = _vm.ProcessInputAsync(text).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    var msg = task.Exception?.InnerException?.Message ?? "未知错误";
                    _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                        $"Agent 异常: {msg}",
                        foreground: BlockColors.Failure)));
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    /// <summary>
    /// 首次输入时初始化 Agent 并处理文本。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    private async Task InitializeAndProcessAsync(string text)
    {
        try
        {
            await _vm.InitializeAsync();
            _vmInitialized = true; // 成功后才标记
            await _vm.ProcessInputAsync(text);
        }
        catch (Exception ex)
        {
            _doc.AppendBlock(new SystemBlock(
                $"Agent 初始化失败: {ex.Message}", foreground: BlockColors.Failure));
        }
    }
}
