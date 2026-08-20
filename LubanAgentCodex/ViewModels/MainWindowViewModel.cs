/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels
*文件名： MainWindowViewModel
*版本号： V1.0.0.0
*唯一标识：主窗口 ViewModel
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：主窗口 ViewModel，管理消息流、会话和 Agent 交互
*
*****************************************************************************/
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LubanAgentCodex.Services;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCore.Models;
using LubanAgentCore.Utils;
using LuBan.AIAgent.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Text;

namespace LubanAgentCodex.ViewModels;

/// <summary>
/// 主窗口 ViewModel
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly AgentHostService _agentHost;
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _pendingText = new();
    private readonly StringBuilder _pendingThinking = new();
    private FlushThrottle? _throttle;
    private AssistantMessageItem? _currentAssistant;

    /// <summary>
    /// 服务提供者
    /// </summary>
    public IServiceProvider Services => _agentHost.Services;

    /// <summary>
    /// 输入文本
    /// </summary>
    [ObservableProperty]
    private string _inputText = "";

    /// <summary>
    /// 是否正在运行
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// 权限模式
    /// </summary>
    [ObservableProperty]
    private ToolPermissionMode _permissionMode = ToolPermissionMode.Default;

    /// <summary>
    /// 消息集合
    /// </summary>
    public ObservableCollection<MessageItemBase> Messages { get; } = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="services">服务提供者</param>
    public MainWindowViewModel(IServiceProvider services)
    {
        _agentHost = new AgentHostService(services);
    }

    /// <summary>
    /// 发送消息命令
    /// </summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(InputText))
            return;

        if (!_agentHost.IsInitialized)
        {
            try
            {
                await _agentHost.InitializeAsync();
            }
            catch (Exception ex)
            {
                Messages.Add(new SystemMessageItem
                {
                    Content = $"初始化失败: {ex.Message}",
                    IsError = true
                });
                return;
            }
        }

        IsRunning = true;
        var input = InputText;
        InputText = "";

        // 添加用户消息
        Messages.Add(new UserMessageItem { Content = input });

        // 创建 AI 消息项
        _currentAssistant = new AssistantMessageItem();
        Messages.Add(_currentAssistant);

        _cts = new CancellationTokenSource();

        // 初始化节流器
        _throttle ??= new FlushThrottle(FlushPending, TimeSpan.FromMilliseconds(50));

        try
        {
            // 关键：消费循环放后台线程，避免确认回调阻塞 UI 线程导致死锁
            await Task.Run(() => ConsumeStreamAsync(input, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new SystemMessageItem { Content = "已取消" });
        }
        catch (Exception ex)
        {
            Messages.Add(new SystemMessageItem
            {
                Content = $"错误: {ex.Message}",
                IsError = true
            });
        }
        finally
        {
            FlushPending();
            _currentAssistant.IsComplete = true;
            _currentAssistant = null;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 取消命令
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private async Task ConsumeStreamAsync(string input, CancellationToken ct)
    {
        await foreach (var evt in _agentHost.RunStreamingAsync(
            input, ConfirmCallback, PermissionMode, ct))
        {
            switch (evt)
            {
                case TextDeltaEvent t:
                    _pendingText.Append(t.Delta);
                    _throttle?.Schedule();
                    break;

                case ThinkingDeltaEvent t:
                    _pendingThinking.Append(t.Delta);
                    _throttle?.Schedule();
                    break;

                case ToolCallStartedEvent tc:
                    FlushPending();
                    var toolItem = new ToolCallItem
                    {
                        ToolName = tc.Name,
                        CallId = tc.CallId,
                        Arguments = tc.Arguments,
                        State = ToolCallState.Running
                    };
                    Dispatcher.UIThread.Post(() => Messages.Add(toolItem));
                    break;

                case ToolCallCompletedEvent tcc:
                    Dispatcher.UIThread.Post(() =>
                    {
                        for (int i = Messages.Count - 1; i >= 0; i--)
                        {
                            if (Messages[i] is ToolCallItem tool && tool.CallId == tcc.CallId)
                            {
                                tool.State = ToolCallState.Done;
                                break;
                            }
                        }
                    });
                    break;

                case ToolCallFailedEvent tcf:
                    Dispatcher.UIThread.Post(() =>
                    {
                        for (int i = Messages.Count - 1; i >= 0; i--)
                        {
                            if (Messages[i] is ToolCallItem tool && tool.CallId == tcf.CallId)
                            {
                                tool.State = ToolCallState.Failed;
                                tool.ErrorMessage = tcf.Error;
                                break;
                            }
                        }
                    });
                    break;

                case ErrorEvent e:
                    Dispatcher.UIThread.Post(() => Messages.Add(
                        new SystemMessageItem { Content = e.Message, IsError = true }));
                    break;
            }
        }
    }

    private void FlushPending()
    {
        if (_currentAssistant == null) return;

        var text = _pendingText.ToString();
        var thinking = _pendingThinking.ToString();

        if (text.Length > 0 || thinking.Length > 0)
        {
            _pendingText.Clear();
            _pendingThinking.Clear();

            Dispatcher.UIThread.Post(() =>
            {
                if (text.Length > 0)
                    _currentAssistant.AppendDelta(text);
                if (thinking.Length > 0)
                    _currentAssistant.AppendThinking(thinking);
            });
        }
    }

    /// <summary>
    /// 加载会话历史
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public async Task LoadSessionHistoryAsync(string sessionId)
    {
        if (IsRunning) return;

        var sessionManager = _agentHost.Services.GetRequiredService<LuBan.AIAgent.Sessions.ISessionManager>();
        await sessionManager.SetCurrentSessionAsync(sessionId);

        Messages.Clear();

        var messages = await sessionManager.GetLatestMessagesAsync(sessionId, 50);
        foreach (var msg in messages.OrderBy(m => m.CreatedAt))
        {
            if (msg.Role == "user")
            {
                Messages.Add(new UserMessageItem { Content = msg.Content });
            }
            else if (msg.Role == "assistant")
            {
                Messages.Add(new AssistantMessageItem
                {
                    Content = msg.Content,
                    IsComplete = true
                });
            }
        }
    }

    /// <summary>
    /// 清空消息流
    /// </summary>
    public void ClearMessages()
    {
        Messages.Clear();
    }

    private ConfirmResult ConfirmCallback(string toolName, IReadOnlyDictionary<string, object?> args)
    {
        using var done = new ManualResetEventSlim(false);
        var result = ConfirmResult.Deny;

        var ct = _cts?.Token ?? CancellationToken.None;
        using var ctr = ct.CanBeCanceled ? ct.Register(() => done.Set()) : default;

        Dispatcher.UIThread.Post(() =>
        {
            var confirmItem = new ToolConfirmItem
            {
                ToolName = toolName,
                Arguments = args,
                OnRespond = cr =>
                {
                    result = cr;
                    done.Set();
                }
            };
            Messages.Add(confirmItem);
        });

        done.Wait(TimeSpan.FromMinutes(2), ct);

        return result;
    }
}
