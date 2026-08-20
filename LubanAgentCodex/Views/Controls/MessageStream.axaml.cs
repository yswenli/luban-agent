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
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using LubanAgentCodex.ViewModels.Messages;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 消息流控件
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
    /// 设置消息集合
    /// </summary>
    public void SetMessages(ObservableCollection<MessageItemBase> messages)
    {
        if (_messages != null)
        {
            _messages.CollectionChanged -= OnMessagesChanged;
        }

        _messages = messages;
        _messages.CollectionChanged += OnMessagesChanged;

        // 初始加载
        UpdateItemsControl();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_messagesItems == null) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                // 新增项：只添加新控件
                if (e.NewItems != null)
                {
                    foreach (var item in e.NewItems)
                    {
                        if (item is MessageItemBase msg)
                        {
                            var control = CreateControlForMessage(msg);
                            if (control != null)
                            {
                                _messagesItems.Items.Add(control);
                            }
                        }
                    }
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                // 移除项：根据索引移除
                if (e.OldStartingIndex >= 0 && e.OldStartingIndex < _messagesItems.Items.Count)
                {
                    _messagesItems.Items.RemoveAt(e.OldStartingIndex);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                // 清空：重新加载
                UpdateItemsControl();
                break;
        }

        if (_scroller != null && _autoScroll)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _scroller.ScrollToEnd();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void UpdateItemsControl()
    {
        if (_messagesItems == null || _messages == null) return;

        _messagesItems.Items.Clear();

        foreach (var msg in _messages)
        {
            var control = CreateControlForMessage(msg);
            if (control != null)
            {
                _messagesItems.Items.Add(control);
            }
        }
    }

    private Control? CreateControlForMessage(MessageItemBase message)
    {
        return message switch
        {
            UserMessageItem => new UserMessageView { DataContext = message },
            AssistantMessageItem => new AssistantMessageView { DataContext = message },
            ToolCallItem tool => new ToolCallCard { DataContext = tool },
            ToolConfirmItem confirm => new ConfirmCard { DataContext = confirm },
            SystemMessageItem => new SystemMessageView { DataContext = message },
            _ => null
        };
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
