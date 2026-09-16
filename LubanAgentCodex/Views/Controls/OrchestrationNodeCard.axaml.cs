/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： OrchestrationNodeCard
*版本号： V1.0.0.0
*唯一标识：编排节点卡片
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/16
*描述：编排子代理节点卡片，点击打开节点详情窗口
*
*****************************************************************************/
using Avalonia.Markup.Xaml;
using LubanAgentCodex.ViewModels.Messages;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 编排子代理节点卡片
/// </summary>
public partial class OrchestrationNodeCard : UserControl
{
    private Border? _rootBorder;

    public OrchestrationNodeCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _rootBorder = this.FindControl<Border>("RootBorder");

        if (_rootBorder != null)
        {
            _rootBorder.PointerPressed += OnPointerPressed;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not OrchestrationNodeItem item)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var window = new NodeDetailWindow(item);

        if (owner != null)
            _ = window.ShowDialog(owner);
        else
            window.Show();
    }
}
