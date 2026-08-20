/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： TitleBar
*版本号： V1.0.0.0
*唯一标识：顶部标题栏控件
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：极简标题栏，显示会话标题和创建时间
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 顶部标题栏控件
/// </summary>
public partial class TitleBar : UserControl
{
    private TextBlock? _titleText;
    private TextBlock? _timeText;

    public TitleBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _titleText = this.FindControl<TextBlock>("TitleText");
        _timeText = this.FindControl<TextBlock>("TimeText");
    }

    /// <summary>
    /// 设置会话标题
    /// </summary>
    public string SessionTitle
    {
        get => _titleText?.Text ?? "";
        set { if (_titleText != null) _titleText.Text = value; }
    }

    /// <summary>
    /// 设置会话时间
    /// </summary>
    public string SessionTime
    {
        get => _timeText?.Text ?? "";
        set { if (_timeText != null) _timeText.Text = value; }
    }
}
