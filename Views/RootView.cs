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
    private readonly ConversationDocument _doc;
    private readonly ConversationViewModel _vm;
    private readonly ConversationView _conversation;
    private readonly FooterView _footer;
    private readonly InputBarView _inputBar;
    private bool _vmInitialized;

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

        _doc = new ConversationDocument();
        _vm = new ConversationViewModel(services, dispatcher, _doc);

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

        _inputBar = new InputBarView
        {
            X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1
        };
        _inputBar.Submitted += OnInputSubmitted;
        _vm.PermissionModeChanged += mode => _footer.SetMode(_vm.PermissionModeDisplay);

        Add(_conversation, _footer, _inputBar);
    }

    public override void EndInit()
    {
        base.EndInit();
        _inputBar.FocusInput();
    }

    public ConversationDocument Document => _doc;
    public ConversationViewModel ViewModel => _vm;
    public ConversationView Conversation => _conversation;
    public FooterView Footer => _footer;
    public InputBarView InputBar => _inputBar;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inputBar.Submitted -= OnInputSubmitted;
        }
        base.Dispose(disposing);
    }

    // ── 全局快捷键 ──

    protected override bool OnKeyDown(Key key)
    {
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

    private void OnInputSubmitted(string text)
    {
        if (string.Equals(text, "/exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "/quit", StringComparison.OrdinalIgnoreCase))
        {
            RequestStop();
            return;
        }

        if (_vm.IsRunning)
        {
            _doc.AppendBlock(new SystemBlock("Agent 正在运行中，请等待完成或按 Ctrl+C 取消"));
            return;
        }

        // 首次输入时初始化 Agent
        if (!_vmInitialized)
        {
            _ = InitializeAndProcessAsync(text).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _doc.AppendBlock(new SystemBlock(
                        $"Agent 异常: {task.Exception?.InnerException?.Message ?? "未知错误"}",
                        foreground: BlockColors.Failure));
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            _ = _vm.ProcessInputAsync(text).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Logger.Error("ProcessInputAsync faulted", task.Exception);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

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
