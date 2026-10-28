/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： MessageStream
*版本号： V1.0.0.0
*唯一标识：消息流控件
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：消息流显示控件，支持自动滚动和多种消息类型
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using LubanAgentCodex.ViewModels.Messages;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 消息流控件：基于虚拟化 <see cref="ItemsControl"/> 按消息类型套用 <c>DataTemplate</c>，
/// 只保留视口内可见项的视觉元素，避免长会话把整条消息流全部实例化到可视树。
/// </summary>
public partial class MessageStream : UserControl
{
    private ScrollViewer? _scroller;
    private ItemsControl? _messagesItems;
    private bool _autoScroll = true;
    private bool _scrollScheduled;
    private ObservableCollection<MessageItemBase>? _messages;

    public MessageStream()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _scroller = this.FindControl<ScrollViewer>("Scroller");
        _messagesItems = this.FindControl<ItemsControl>("MessagesItems");

        if (_scroller != null)
        {
            _scroller.ScrollChanged += OnScrollChanged;
        }
    }

    /// <summary>
    /// 设置消息集合。控件本身不再手工创建子控件，改由 <see cref="ItemsControl"/> 依据
    /// <c>DataTemplate</c> 与虚拟化面板生成，只负责订阅变化以驱动自动滚动。
    /// </summary>
    public void SetMessages(ObservableCollection<MessageItemBase> messages)
    {
        if (_messages != null)
        {
            _messages.CollectionChanged -= OnMessagesChanged;
        }

        _messages = messages;
        _messages.CollectionChanged += OnMessagesChanged;

        if (_messagesItems != null)
        {
            _messagesItems.ItemsSource = messages;
        }

        // 切换会话/重新绑定后回到最新位置，并恢复"贴底"意图，
        // 否则上一会话用户上滚留下的 _autoScroll=false 会让新会话停在历史处。
        _autoScroll = true;
        ScheduleScrollToEnd();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_autoScroll)
        {
            ScheduleScrollToEnd();
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroller == null) return;

        // 内容/尺寸变化（流式追加正文/思考、项裁剪、窗口缩放）不是用户滚动：
        // 保持原"贴底"意图并继续追底，避免按位置重算把贴底误判成上滚而永久关闭自动滚动。
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (_autoScroll)
                ScheduleScrollToEnd();
            return;
        }

        // 纯位移变化才是用户滚动：按实际位置更新意图；向下滚且原本贴底时继续追底。
        var offset = _scroller.Offset.Y;
        var extent = _scroller.Extent.Height;
        var viewport = _scroller.Viewport.Height;
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
        if (_scroller == null || _scrollScheduled) return;

        _scrollScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollScheduled = false;
            if (_autoScroll)
                _scroller?.ScrollToEnd();
        }, DispatcherPriority.Loaded);
    }
}
