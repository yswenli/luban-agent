/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： UserMessageView
*版本号： V1.0.0.0
*唯一标识：用户消息视图
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：用户消息显示控件
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using LubanAgentCore.Models;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 用户消息视图
/// </summary>
public partial class UserMessageView : UserControl
{
    public UserMessageView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 点击图片附件缩略图：弹出大图查看窗口。源文件不存在（历史回放）时回退缩略图。
    /// </summary>
    private void OnAttachmentPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not AttachmentDisplayInfo info || !info.IsImage)
            return;

        var path = !string.IsNullOrEmpty(info.SourcePath) && File.Exists(info.SourcePath)
            ? info.SourcePath
            : info.ThumbnailPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        try
        {
            var viewer = new ImageViewerWindow(path, info.FileName);
            if (owner != null) viewer.Show(owner);
            else viewer.Show();
        }
        catch (Exception ex)
        {
            Logger.Error("UserMessageView.OnAttachmentPressed 打开大图失败", ex);
        }
        e.Handled = true;
    }
}
