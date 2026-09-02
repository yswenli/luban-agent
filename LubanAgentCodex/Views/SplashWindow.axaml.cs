/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SplashWindow
*版本号： V1.0.0.0
*唯一标识：启动闪屏
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/2
*描述：Luban AI Agent 启动闪屏。居中展示 530x300 启动图，并显示初始化进度；
*      由 App.OnFrameworkInitializationCompleted 驱动状态更新，初始化完成且主窗体就绪后延迟 1s 关闭。
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace LubanAgentCodex.Views;

/// <summary>
/// 启动闪屏窗口
/// </summary>
public partial class SplashWindow : Window
{
    private TextBlock? _statusText;
    private ProgressBar? _progress;

    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText");
        _progress = this.FindControl<ProgressBar>("InitProgress");
    }

    /// <summary>
    /// 更新初始化状态文本（线程安全：会切回 UI 线程）
    /// </summary>
    public void SetStatus(string text)
    {
        var text1 = text;
        Dispatcher.UIThread.Post(() =>
        {
            if (_statusText != null) _statusText.Text = text1;
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 标记初始化已完成，停止进度条动画
    /// </summary>
    public void MarkReady()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_progress != null) _progress.IsIndeterminate = false;
            if (_statusText != null) _statusText.Text = "准备就绪";
        }, DispatcherPriority.Background);
    }
}
