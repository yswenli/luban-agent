/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： NodeDetailWindow
*版本号： V1.0.0.0
*唯一标识：子代理节点详情窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/16
*描述：展示单个编排子代理节点的思考/工具调用/结果完整时间线
*
*****************************************************************************/
using Avalonia.Markup.Xaml;
using LubanAgentCodex.ViewModels.Messages;
using System.Collections.Specialized;

namespace LubanAgentCodex.Views;

/// <summary>
/// 子代理节点详情窗口
/// </summary>
public partial class NodeDetailWindow : Window
{
    private ScrollViewer? _activityScroller;
    private bool _autoScroll = true;
    private bool _scrollScheduled;
    private OrchestrationNodeItem? _item;

    /// <summary>
    /// 无参构造（供 XAML 加载器/设计器使用）
    /// </summary>
    public NodeDetailWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 构造并加载指定节点
    /// </summary>
    /// <param name="item">编排节点消息项。</param>
    public NodeDetailWindow(OrchestrationNodeItem item) : this()
    {
        LoadNode(item);
    }

    /// <summary>
    /// 绑定节点数据；节点活动为流式追加，绑定后随窗口生命周期实时刷新
    /// </summary>
    /// <param name="item">编排节点消息项。</param>
    private void LoadNode(OrchestrationNodeItem item)
    {
        if (_item != null)
            _item.Activities.CollectionChanged -= OnActivitiesChanged;

        _item = item;
        DataContext = item;
        item.Activities.CollectionChanged += OnActivitiesChanged;

        // 绑定新节点后回到最新活动并恢复"贴底"意图。
        _autoScroll = true;
        ScheduleScrollToEnd();
    }

    /// <summary>
    /// 窗口关闭时退订节点活动事件：item 由消息流长期持有，若不退订会把已关闭的窗口一直挂在 item 上。
    /// </summary>
    protected override void OnClosed(System.EventArgs e)
    {
        if (_item != null)
        {
            _item.Activities.CollectionChanged -= OnActivitiesChanged;
            _item = null;
        }
        base.OnClosed(e);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _activityScroller = this.FindControl<ScrollViewer>("ActivityScroller");

        if (_activityScroller != null)
            _activityScroller.ScrollChanged += OnScrollChanged;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnActivitiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_autoScroll)
            ScheduleScrollToEnd();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_activityScroller == null) return;

        // 内容/尺寸变化（流式追加活动文本、项裁剪、窗口缩放）不是用户滚动：
        // 保持原"贴底"意图并继续追底，避免按位置重算把贴底误判成上滚而永久关闭自动滚动。
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (_autoScroll)
                ScheduleScrollToEnd();
            return;
        }

        // 纯位移变化才是用户滚动：按实际位置更新意图；向下滚且原本贴底时继续追底。
        var offset = _activityScroller.Offset.Y;
        var extent = _activityScroller.Extent.Height;
        var viewport = _activityScroller.Viewport.Height;
        var atBottom = (extent - offset - viewport) < 50;

        _autoScroll = atBottom || (e.OffsetDelta.Y > 0 && _autoScroll);
    }

    /// <summary>
    /// 布局完成后把视图滚到底：虚拟化面板先完成测量 extent 才稳定，故在 Loaded 优先级投递。
    /// 同一帧内的多次请求合并为一次；执行时复检 <see cref="_autoScroll"/>，
    /// 避免投递后用户已上滚仍被拉回底部。
    /// </summary>
    private void ScheduleScrollToEnd()
    {
        if (_activityScroller == null || _scrollScheduled) return;

        _scrollScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollScheduled = false;
            if (_autoScroll)
                _activityScroller?.ScrollToEnd();
        }, DispatcherPriority.Loaded);
    }
}