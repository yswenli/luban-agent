/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： RenameDialog
*版本号： V1.0.0.0
*唯一标识：重命名对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：通用重命名对话框，用于重命名工作区等
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LubanAgentCodex.Views;

/// <summary>
/// 重命名对话框
/// </summary>
public partial class RenameDialog : Window
{
    private TextBox? _nameTextBox;
    private TextBlock? _titleTextBlock;
    private string? _dialogTitle;
    private string? _watermark;

    /// <summary>
    /// 输入的新名称
    /// </summary>
    public string? Result { get; private set; }

    /// <summary>自定义窗口标题与内容标题，null 时默认"重命名"</summary>
    public string? DialogTitle
    {
        get => _dialogTitle;
        set { _dialogTitle = value; ApplyTitle(); }
    }

    /// <summary>输入框占位提示，null 时保持 axaml 默认</summary>
    public string? Watermark
    {
        get => _watermark;
        set { _watermark = value; ApplyWatermark(); }
    }

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）
    /// </summary>
    public RenameDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 带参构造函数
    /// </summary>
    /// <param name="currentName">当前名称（预填）</param>
    public RenameDialog(string currentName) : this()
    {
        if (_nameTextBox != null)
        {
            _nameTextBox.Text = currentName;
            _nameTextBox.SelectAll();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _nameTextBox = this.FindControl<TextBox>("NameTextBox");
        _titleTextBlock = this.FindControl<TextBlock>("TitleTextBlock");

        var okButton = this.FindControl<Button>("OkButton");
        var cancelButton = this.FindControl<Button>("CancelButton");

        if (okButton != null)
            okButton.Click += OnOk;
        if (cancelButton != null)
            cancelButton.Click += OnCancel;
    }

    private void ApplyTitle()
    {
        var title = _dialogTitle ?? "重命名";
        this.Title = title;
        if (_titleTextBlock != null) _titleTextBlock.Text = title;
    }

    private void ApplyWatermark()
    {
        if (_watermark != null && _nameTextBox != null)
            _nameTextBox.PlaceholderText = _watermark;
    }

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = _nameTextBox?.Text?.Trim();
        Close(Result);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}
