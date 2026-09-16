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
        if (_activityScroller == null || !_autoScroll)
            return;

        Dispatcher.UIThread.Post(() => _activityScroller.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_activityScroller == null) return;

        var offset = _activityScroller.Offset.Y;
        var extent = _activityScroller.Extent.Height;
        var viewport = _activityScroller.Viewport.Height;

        _autoScroll = (extent - offset - viewport) < 50;
    }
}