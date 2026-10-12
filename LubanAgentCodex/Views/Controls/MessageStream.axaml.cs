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
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scroller != null && _autoScroll)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _scroller.ScrollToEnd();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroller == null) return;

        var offset = _scroller.Offset.Y;
        var extent = _scroller.Extent.Height;
        var viewport = _scroller.Viewport.Height;

        _autoScroll = (extent - offset - viewport) < 50;
    }
}
