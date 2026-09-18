/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： ImageViewerWindow
*版本号： V1.0.0.0
*唯一标识：图片大图查看窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/18
*描述：点击消息区图片附件缩略图后弹出的大图查看窗口。
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;

namespace LubanAgentCodex.Views;

/// <summary>
/// 图片大图查看窗口：按窗口尺寸等比缩放显示，Esc / 点击关闭。
/// </summary>
public sealed class ImageViewerWindow : Window
{
    /// <summary>构造。</summary>
    /// <param name="imagePath">本地图片路径。</param>
    /// <param name="title">窗口标题。</param>
    public ImageViewerWindow(string imagePath, string title)
    {
        Title = title;
        Width = 900;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Avalonia.Media.Brushes.Black;
        Content = new Image
        {
            Source = new Bitmap(imagePath),
            Stretch = Avalonia.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Avalonia.Thickness(8)
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        PointerPressed += (s, e) => Close();
    }
}