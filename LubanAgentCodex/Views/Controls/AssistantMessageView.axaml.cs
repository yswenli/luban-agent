/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： AssistantMessageView
*版本号： V1.0.0.0
*唯一标识：AI 助手消息视图
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：AI 助手消息显示控件，使用 AvaloniaEdit 渲染内容
*
*****************************************************************************/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using LubanAgentCodex.ViewModels.Messages;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// AI 助手消息视图
/// </summary>
public partial class AssistantMessageView : UserControl
{
    private TextEditor? _editor;
    private AssistantMessageItem? _viewModel;

    public AssistantMessageView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _editor = this.FindControl<TextEditor>("Editor");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is AssistantMessageItem vm)
        {
            _viewModel = vm;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateEditorContent();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistantMessageItem.Content))
        {
            UpdateEditorContent();
        }
    }

    private void UpdateEditorContent()
    {
        if (_editor != null && _viewModel != null)
        {
            var content = _viewModel.Content ?? "";
            if (_editor.Text != content)
            {
                _editor.Text = content;
                // 滚动到底部
                _editor.ScrollToEnd();
            }
        }
    }
}
