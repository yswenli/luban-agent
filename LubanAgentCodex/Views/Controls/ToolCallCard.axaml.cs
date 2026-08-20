/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： ToolCallCard
*版本号： V1.0.0.0
*唯一标识：工具调用卡片
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工具调用显示卡片，支持展开参数查看
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LubanAgentCodex.ViewModels.Messages;
using System.Text.Json;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 工具调用卡片
/// </summary>
public partial class ToolCallCard : UserControl
{
    private TextBlock? _statusIcon;
    private TextBlock? _toolNameText;
    private TextBlock? _stateText;
    private TextBox? _argumentsText;
    private ToolCallItem? _boundItem;

    public ToolCallCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _statusIcon = this.FindControl<TextBlock>("StatusIcon");
        _toolNameText = this.FindControl<TextBlock>("ToolNameText");
        _stateText = this.FindControl<TextBlock>("StateText");
        _argumentsText = this.FindControl<TextBox>("ArgumentsText");
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ToolCallItem item)
        {
            BindItem(item);
        }
    }

    private void BindItem(ToolCallItem item)
    {
        _boundItem = item;

        if (_toolNameText != null)
            _toolNameText.Text = item.ToolName;

        if (_argumentsText != null && item.Arguments.Count > 0)
        {
            var json = JsonSerializer.Serialize(item.Arguments, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            _argumentsText.Text = json;
        }

        UpdateState();

        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ToolCallItem.State))
                UpdateState();
        };
    }

    private void UpdateState()
    {
        if (_boundItem == null || _statusIcon == null || _stateText == null) return;

        switch (_boundItem.State)
        {
            case ToolCallState.Running:
                _statusIcon.Text = "⏳";
                _stateText.Text = "运行中...";
                _stateText.Foreground = Brushes.Yellow;
                break;
            case ToolCallState.Done:
                _statusIcon.Text = "✓";
                _stateText.Text = "已完成";
                _stateText.Foreground = Brushes.LimeGreen;
                break;
            case ToolCallState.Failed:
                _statusIcon.Text = "✗";
                _stateText.Text = _boundItem.ErrorMessage ?? "失败";
                _stateText.Foreground = Brushes.Red;
                break;
        }
    }
}
