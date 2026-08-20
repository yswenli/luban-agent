/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： ConfirmCard
*版本号： V1.0.0.0
*唯一标识：工具确认卡片
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工具确认卡片，允许/拒绝工具执行
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCore.Models;
using System.Text.Json;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 工具确认卡片
/// </summary>
public partial class ConfirmCard : UserControl
{
    private TextBlock? _toolNameText;
    private TextBox? _argumentsText;
    private Button? _allowButton;
    private Button? _allowAllButton;
    private Button? _denyButton;
    private ToolConfirmItem? _boundItem;

    public ConfirmCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _toolNameText = this.FindControl<TextBlock>("ToolNameText");
        _argumentsText = this.FindControl<TextBox>("ArgumentsText");
        _allowButton = this.FindControl<Button>("AllowButton");
        _allowAllButton = this.FindControl<Button>("AllowAllButton");
        _denyButton = this.FindControl<Button>("DenyButton");

        _allowButton!.Click += (s, e) => Respond(ConfirmResult.Allow);
        _allowAllButton!.Click += (s, e) => Respond(ConfirmResult.AllowAll);
        _denyButton!.Click += (s, e) => Respond(ConfirmResult.Deny);
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ToolConfirmItem item)
        {
            BindItem(item);
        }
    }

    private void BindItem(ToolConfirmItem item)
    {
        _boundItem = item;

        if (_toolNameText != null)
            _toolNameText.Text = $"工具: {item.ToolName}";

        if (_argumentsText != null && item.Arguments.Count > 0)
        {
            var json = JsonSerializer.Serialize(item.Arguments, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            _argumentsText.Text = json;
        }
    }

    private void Respond(ConfirmResult result)
    {
        _boundItem?.OnRespond(result);

        // 禁用按钮，防止重复点击
        if (_allowButton != null) _allowButton.IsEnabled = false;
        if (_allowAllButton != null) _allowAllButton.IsEnabled = false;
        if (_denyButton != null) _denyButton.IsEnabled = false;
    }
}
