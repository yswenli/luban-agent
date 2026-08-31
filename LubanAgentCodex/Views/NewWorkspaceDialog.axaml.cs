/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： NewWorkspaceDialog
*版本号： V1.0.0.0
*唯一标识：新建工作区对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/31
*描述：新建工作区对话框，输入名称并选择目录
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace LubanAgentCodex.Views;

/// <summary>
/// 新建工作区对话框
/// </summary>
public partial class NewWorkspaceDialog : Window
{
    private TextBox? _nameBox;
    private TextBox? _pathBox;
    private TextBlock? _errorText;

    /// <summary>用户输入的工作区名称（可为空，创建时取目录名）</summary>
    public string? WorkspaceName { get; private set; }

    /// <summary>选中的工作区目录绝对路径</summary>
    public string? WorkspacePath { get; private set; }

    public NewWorkspaceDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _nameBox = this.FindControl<TextBox>("NameTextBox");
        _pathBox = this.FindControl<TextBox>("PathTextBox");
        _errorText = this.FindControl<TextBlock>("ErrorText");

        if (this.FindControl<Button>("BrowseButton") is { } browse)
            browse.Click += OnBrowse;
        if (this.FindControl<Button>("OkButton") is { } ok)
            ok.Click += OnOk;
        if (this.FindControl<Button>("CancelButton") is { } cancel)
            cancel.Click += (s, e) => Close(false);

        if (_nameBox != null)
            _nameBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) OnOk(s, e);
            };
        if (_pathBox != null)
            _pathBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) OnOk(s, e);
            };
    }

    private async void OnBrowse(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择工作区目录",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && _pathBox != null)
        {
            _pathBox.Text = folders[0].Path.LocalPath;
            HideError();
        }
    }

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = _pathBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowError("请选择工作区目录");
            return;
        }

        var name = _nameBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            // 名称缺省时取目录名（与 WorkspaceManager.CreateWorkspaceAsync 行为一致）
            name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        }

        WorkspaceName = name;
        WorkspacePath = path;
        Close(true);
    }

    private void ShowError(string msg)
    {
        if (_errorText != null)
        {
            _errorText.Text = msg;
            _errorText.IsVisible = true;
        }
    }

    private void HideError()
    {
        if (_errorText != null)
            _errorText.IsVisible = false;
    }
}
