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
using System.ComponentModel;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 编排子代理节点卡片
/// </summary>
public partial class OrchestrationNodeCard : UserControl
{
    private Border? _rootBorder;
    private Border? _accentBar;
    private OrchestrationNodeItem? _boundItem;
    private PropertyChangedEventHandler? _itemHandler;

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

        UnbindItem();

        if (DataContext is OrchestrationNodeItem item)
            BindItem(item);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // 卡片在虚拟化场景会被回收复用：重新挂到可视树时按当前 DataContext 重订阅
        if (_boundItem is null && DataContext is OrchestrationNodeItem item)
            BindItem(item);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // item 由消息流长期持有，退订避免已移除的卡片被 item 事件持续强引用
        UnbindItem();
    }

    private void BindItem(OrchestrationNodeItem item)
    {
        _boundItem = item;
        UpdateAccent();
        _itemHandler = (s, args) =>
        {
            if (args.PropertyName is nameof(OrchestrationNodeItem.IsFailed)
                or nameof(OrchestrationNodeItem.IsComplete)
                or nameof(OrchestrationNodeItem.IsSkipped))
            {
                UpdateAccent();
            }
        };
        item.PropertyChanged += _itemHandler;
    }

    private void UnbindItem()
    {
        if (_boundItem != null && _itemHandler != null)
            _boundItem.PropertyChanged -= _itemHandler;
        _itemHandler = null;
        _boundItem = null;
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
