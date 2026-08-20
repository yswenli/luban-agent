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
*描述：AI 助手消息显示控件，支持 Markdown 渲染
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCodex.Utils;
using LubanAgentCodex.ViewModels.Messages;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// AI 助手消息视图
/// </summary>
public partial class AssistantMessageView : UserControl
{
    private TextBlock? _contentText;
    private AssistantMessageItem? _viewModel;
    private string? _lastContent;

    public AssistantMessageView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _contentText = this.FindControl<TextBlock>("ContentText");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is AssistantMessageItem vm)
        {
            _viewModel = vm;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateContent();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistantMessageItem.Content))
        {
            UpdateContent();
        }
    }

    private void UpdateContent()
    {
        if (_contentText == null || _viewModel == null) return;

        var content = _viewModel.Content ?? "";
        
        // 只在内容变化时更新
        if (_lastContent != content)
        {
            _lastContent = content;
            
            // 使用 Markdown 渲染器解析内容
            if (_contentText.Inlines != null)
            {
                MarkdownRenderer.Parse(content, _contentText.Inlines);
            }
        }
    }
}
