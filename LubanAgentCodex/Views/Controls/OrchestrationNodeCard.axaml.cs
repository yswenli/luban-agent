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
*描述：编排子代理节点卡片：左侧色条 + 宽度限制 + 点击打开节点详情窗口。

*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LubanAgentCodex.ViewModels.Messages;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 编排子代理节点卡片
/// </summary>
public partial class OrchestrationNodeCard : UserControl
{
    private Border? _rootBorder;
    private Border? _accentBar;
    private OrchestrationNodeItem? _boundItem;

    public OrchestrationNodeCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _rootBorder = this.FindControl<Border>("RootBorder");
        _accentBar = this.FindControl<Border>("AccentBar");

        if (_rootBorder != null)
        {
            _rootBorder.PointerPressed += OnPointerPressed;
        }
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is OrchestrationNodeItem item)
        {
            _boundItem = item;
            UpdateAccent();
            item.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName is nameof(OrchestrationNodeItem.IsFailed)
                    or nameof(OrchestrationNodeItem.IsComplete)
                    or nameof(OrchestrationNodeItem.IsSkipped))
                {
                    UpdateAccent();
                }
            };
        }
    }

    /// <summary>
    /// 根据节点状态设置左侧色条颜色：
    /// 失败=红、跳过=灰、完成=绿、运行中=黄。
    /// </summary>
    private void UpdateAccent()
    {
        if (_accentBar is null || _boundItem is null) return;

        var key = _boundItem.IsFailed ? "ErrorBrush"
                : _boundItem.IsSkipped ? "TextTertiaryBrush"
                : _boundItem.IsComplete ? "SuccessBrush"
                : "WarningBrush";

        if (Application.Current?.FindResource(key) is IBrush brush)
            _accentBar.Background = brush;
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
