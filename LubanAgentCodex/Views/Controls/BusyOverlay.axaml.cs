/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： BusyOverlay
*版本号： V1.0.0.0
*唯一标识：忙碌遮罩
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/21
*描述：全窗忙碌遮罩（不确定进度条 + 状态文本），供主窗口与 LLM Wiki 管理窗口共用
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 全窗忙碌遮罩：长耗时任务（索引/重建/lint）期间显示，避免用户误以为程序卡死。
/// 文案通过 <see cref="Text"/> 设置（可绑定），显示/隐藏由宿主控制 <c>IsVisible</c> 或调用
/// <see cref="Show"/> / <see cref="UpdateStatus"/> / <see cref="Hide"/>。
/// </summary>
public partial class BusyOverlay : UserControl
{
    /// <summary>状态文本属性</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<BusyOverlay, string?>(nameof(Text), "正在处理…");

    private TextBlock? _statusText;

    /// <summary>
    /// 创建忙碌遮罩
    /// </summary>
    public BusyOverlay()
    {
        InitializeComponent();
    }

    /// <summary>状态文本（显示在进度条下方）</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText");
        if (_statusText != null) _statusText.Text = Text ?? string.Empty;
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && _statusText != null)
        {
            _statusText.Text = change.GetNewValue<string?>() ?? string.Empty;
        }
    }

    /// <summary>显示遮罩并设置状态文本。</summary>
    public void Show(string text)
    {
        Text = text;
        IsVisible = true;
    }

    /// <summary>就地更新状态文本（保持在显示状态）。</summary>
    public void UpdateStatus(string text) => Text = text;

    /// <summary>隐藏遮罩。</summary>
    public void Hide() => IsVisible = false;
}