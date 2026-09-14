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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LubanAgentCore.Services;
using LubanAgentCore.Utils;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCodex.Views;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.ViewModels;

/// <summary>
/// 主窗口 ViewModel
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly AgentHostService _agentHost;
    private CancellationTokenSource? _cts;
    private int _activeRunSeq; // 运行序号：每次发起对话自增，用于丢弃取消/新对话后迟到的旧事件
    private readonly StringBuilder _pendingText = new();
    private readonly StringBuilder _pendingThinking = new();
    private FlushThrottle? _throttle;
    private AssistantMessageItem? _currentAssistant;
    private ThinkingMessageItem? _currentThinking;

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
    /// 是否正在切换会话（用于显示中央加载提示）
    /// </summary>
    [ObservableProperty]
    private bool _isSwitchingSession;

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

        var input = InputText.Trim();

        // 处理 / 命令
        if (input.StartsWith('/'))
        {
            InputText = "";
            await ExecuteCommandAsync(input);
            return;
        }

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
        InputText = "";

        // 添加用户消息
        Messages.Add(new UserMessageItem { Content = input });

        // 注意：不预创建 AssistantMessageItem，正文项在首个 TextDeltaEvent 到达时懒创建，
        // 确保它排在思考内容/工具卡片之后（修复显示顺序错乱）

        _cts = new CancellationTokenSource();
        var runSeq = Interlocked.Increment(ref _activeRunSeq); // 本次对话运行序号；取消/新对话将使其失效

        // 初始化节流器：回调投递到 UI 线程执行，与事件处理天然串行
        _throttle ??= new FlushThrottle(
            () => Dispatcher.UIThread.Post(FlushPending),
            TimeSpan.FromMilliseconds(50));

        try
        {
            // 关键：消费循环放后台线程，避免确认回调阻塞 UI 线程导致死锁；
            // 事件本身全部投递到 UI 线程按序处理（Dispatcher 队列 FIFO 保序）
            await Task.Run(() => ConsumeStreamAsync(input, _cts.Token, runSeq));
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
            // 收尾同样投递到 UI 线程，排在所有已投递事件之后执行
            Dispatcher.UIThread.Post(() =>
            {
                FlushPending();
                CompleteCurrentItems();
                IsRunning = false;
            });
            var cts = _cts;
            _cts = null;
            cts?.Dispose();
        }
    }

    /// <summary>
    /// 取消命令
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (_cts == null)
            return;
        // 失效当前运行序号：丢弃取消后迟到的旧事件（避免下次输入仍执行上次对话）
        Interlocked.Increment(ref _activeRunSeq);
        _cts.Cancel();
    }

    /// <summary>
    /// 重置 Agent，下次发送消息时按最新配置（如切换后的模型）重建
    /// </summary>
    public void ResetAgent() => _agentHost.Reset();

    /// <summary>
    /// 执行 / 命令
    /// </summary>
    private async Task ExecuteCommandAsync(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmd = parts[0].ToLowerInvariant();
        var args = ExpandSubCommandAliases(parts.Skip(1).ToArray());

        switch (cmd)
        {
            case "/help":
                ShowHelp();
                break;

            case "/clear":
                ClearMessages();
                Messages.Add(new SystemMessageItem { Content = "会话已清空" });
                break;

            case "/mode":
                if (args.Length > 0)
                {
                    var mode = args[0].ToLowerInvariant() switch
                    {
                        "default" => ToolPermissionMode.Default,
                        "plan" => ToolPermissionMode.Plan,
                        "accept-edits" or "acceptedits" => ToolPermissionMode.AcceptEdits,
                        "bypass" or "bypasspermissions" => ToolPermissionMode.BypassPermissions,
                        _ => (ToolPermissionMode?)null
                    };

                    if (mode.HasValue)
                    {
                        PermissionMode = mode.Value;
                        Messages.Add(new SystemMessageItem
                        {
                            Content = $"权限模式已切换: {GetPermissionModeDisplay(PermissionMode)}"
                        });
                    }
                    else
                    {
                        Messages.Add(new SystemMessageItem
                        {
                            Content = $"无效模式: {args[0]}. 可用: default, plan, accept-edits, bypass",
                            IsError = true
                        });
                    }
                }
                else
                {
                    Messages.Add(new SystemMessageItem
                    {
                        Content = $"当前权限模式: {GetPermissionModeDisplay(PermissionMode)}\n可用模式: default / plan / accept-edits / bypass"
                    });
                }
                break;

            case "/model":
            case "/m":
                await ExecuteModelCommandAsync(args);
                break;

            case "/session":
            case "/se":
                await ExecuteSessionCommandAsync(args);
                break;

            case "/stats":
            case "/st":
                ShowStats();
                break;

            case "/provider":
            case "/p":
                ShowProviderManager(args);
                break;

            case "/skill":
            case "/sk":
                ShowSkillManager(args);
                break;

            case "/rule":
            case "/r":
                ShowRuleManager(args);
                break;

            case "/mcp":
            case "/mp":
                ShowMcpManager(args);
                break;

            case "/work":
            case "/w":
                ShowWorkManager(args);
                break;

            case "/rag":
            case "/rg":
                ShowRagManager(args);
                break;

            default:
                Messages.Add(new SystemMessageItem
                {
                    Content = $"未知命令: {cmd}，输入 /help 查看可用命令",
                    IsError = true
                });
                break;
        }
    }

    /// <summary>
    /// 子命令简写展开
    /// </summary>
    private static string[] ExpandSubCommandAliases(string[] parts)
    {
        var result = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            result[i] = parts[i] switch
            {
                "-l" => "-list",
                "-a" => "-add",
                "-u" => "-update",
                "-d" => "-delete",
                "-s" => "-switch",
                "-n" => "-new",
                "-c" => "-clear",
                "-t" => "-tools",
                _ => parts[i]
            };
        }
        return result;
    }

    /// <summary>
    /// 打开统一设置中心并定位到「供应商」页签（替代旧的 ProviderManageWindow）
    /// </summary>
    private void ShowProviderManager(string[] args)
    {
        OpenSettings(SettingsTabKind.Provider);
    }

    /// <summary>
    /// 打开工作区设置中心并定位到「技能」页签（替代旧的 SkillManageWindow）
    /// </summary>
    private void ShowSkillManager(string[] args)
    {
        OpenSettings(SettingsTabKind.Skill);
    }

    /// <summary>
    /// 打开工作区设置中心并定位到「规则」页签（替代旧的 RuleManageWindow）
    /// </summary>
    private void ShowRuleManager(string[] args)
    {
        OpenSettings(SettingsTabKind.Rule);
    }

    /// <summary>
    /// 打开工作区设置中心并定位到「MCP」页签（替代旧的 MCPManageWindow）
    /// </summary>
    private void ShowMcpManager(string[] args)
    {
        OpenSettings(SettingsTabKind.Mcp);
    }

    /// <summary>
    /// 打开工作区设置中心并预选当前工作区（非 RAG）与指定页签。
    /// 命令面板路径无 Window owner，故用非模态 Show（侧边栏「⚙ 设置」按钮走 MainWindow 的模态 ShowDialog）。
    /// </summary>
    private void OpenSettings(SettingsTabKind kind)
    {
        var workspaceManager = Services.GetService<IWorkspaceManager>();
        var current = workspaceManager?.CurrentWorkspace;
        var preselect = (current != null && current.Type != "Rag") ? current : null;

        var window = new SettingsWindow(Services, preselect);
        window.PreselectTab(kind);
        window.Show();
    }

    /// <summary>
    /// 显示工作区管理窗口
    /// </summary>
    private void ShowWorkManager(string[] args)
    {
        var window = new WorkManageWindow(Services);
        window.Show();
    }

    /// <summary>
    /// 显示 RAG 知识库管理窗口
    /// </summary>
    private void ShowRagManager(string[] args)
    {
        var window = new RagManageWindow(Services);
        window.Show();
    }

    private void ShowHelp()
    {
        var helpText = @"可用命令:
  /help               显示此帮助
  /clear              清空会话历史
  /mode [name]        查看或切换权限模式
  /model, /m          管理模型
  /provider, /p       管理 AI Provider
  /skill, /sk         管理技能
  /rule, /r           管理规则
  /mcp, /mp           管理 MCP 服务
  /session, /se       管理对话会话
  /stats, /st         显示统计信息
  /work, /w           管理工作区
  /rag, /rg           管理 RAG 知识库

快捷键:
  Enter               发送消息
  Ctrl+Enter          换行
  Shift+Tab           切换权限模式
  Esc                 取消当前任务";

        Messages.Add(new SystemMessageItem { Content = helpText });
    }

    private string GetPermissionModeDisplay(ToolPermissionMode mode) => mode switch
    {
        ToolPermissionMode.Default => "默认 (Default)",
        ToolPermissionMode.Plan => "计划 (Plan)",
        ToolPermissionMode.AcceptEdits => "接受编辑 (AcceptEdits)",
        ToolPermissionMode.BypassPermissions => "跳过权限 (Bypass)",
        _ => mode.ToString()
    };

    private async Task ExecuteModelCommandAsync(string[] args)
    {
        var configManager = Services.GetService<ConfigManager>();
        if (configManager == null)
        {
            Messages.Add(new SystemMessageItem { Content = "配置管理器未初始化", IsError = true });
            return;
        }

        if (args.Length == 0 || args[0] == "-list" || args[0] == "list")
        {
            // 列出所有模型
            var sb = new StringBuilder();
            sb.AppendLine("可用模型:");

            foreach (var provider in configManager.Providers)
            {
                var models = configManager.GetAllModels(provider.Name);
                foreach (var model in models)
                {
                    var fullName = $"{provider.Name}:{model}";
                    var selected = fullName == configManager.SelectedModel ? " (当前)" : "";
                    sb.AppendLine($"  {fullName}{selected}");
                }
            }

            if (!string.IsNullOrEmpty(configManager.SelectedModel))
            {
                sb.AppendLine($"\n当前选择: {configManager.SelectedModel}");
            }

            Messages.Add(new SystemMessageItem { Content = sb.ToString() });
        }
        else if (args[0] == "-switch" || args[0] == "switch")
        {
            if (args.Length < 2)
            {
                Messages.Add(new SystemMessageItem
                {
                    Content = "用法: /model -switch <provider:model>",
                    IsError = true
                });
                return;
            }

            var modelId = string.Join(' ', args.Skip(1));
            try
            {
                configManager.SetSelectedModel(modelId);
                Messages.Add(new SystemMessageItem { Content = $"已切换模型: {modelId}" });
            }
            catch (Exception ex)
            {
                Messages.Add(new SystemMessageItem
                {
                    Content = $"切换模型失败: {ex.Message}",
                    IsError = true
                });
            }
        }
        else
        {
            Messages.Add(new SystemMessageItem
            {
                Content = "用法: /model [-list] [-switch <provider:model>]"
            });
        }
    }

    private async Task ExecuteSessionCommandAsync(string[] args)
    {
        var sessionManager = Services.GetService<ISessionManager>();
        if (sessionManager == null)
        {
            Messages.Add(new SystemMessageItem { Content = "会话管理器未初始化", IsError = true });
            return;
        }

        if (args.Length == 0 || args[0] == "-list" || args[0] == "list")
        {
            // 列出所有会话
            var sessions = await sessionManager.GetUserSessionsAsync("");
            var sb = new StringBuilder();
            sb.AppendLine("会话列表:");

            int index = 1;
            foreach (var session in sessions)
            {
                var current = session.SessionId == sessionManager.CurrentSession?.SessionId ? " (当前)" : "";
                sb.AppendLine($"  {index}. {session.Title}{current}");
                index++;
            }

            if (!sessions.Any())
            {
                sb.AppendLine("  (无会话)");
            }

            Messages.Add(new SystemMessageItem { Content = sb.ToString() });
        }
        else if (args[0] == "-new" || args[0] == "new")
        {
            var title = args.Length > 1 ? string.Join(' ', args.Skip(1)) : "新会话";
            var session = await sessionManager.CreateSessionAsync(null, title);
            await sessionManager.SetCurrentSessionAsync(session.SessionId);
            Messages.Add(new SystemMessageItem { Content = $"已创建并切换到会话: {title}" });
        }
        else if (args[0] == "-clear" || args[0] == "clear")
        {
            sessionManager.ClearCurrentSession();
            ClearMessages();
            Messages.Add(new SystemMessageItem { Content = "会话已清空" });
        }
        else
        {
            Messages.Add(new SystemMessageItem
            {
                Content = "用法: /session [-list] [-new <标题>] [-clear]"
            });
        }
    }

    private void ShowStats()
    {
        var sessionManager = Services.GetService<ISessionManager>();
        var sb = new StringBuilder();
        sb.AppendLine("统计信息:");
        sb.AppendLine($"  消息数量: {Messages.Count}");
        sb.AppendLine($"  权限模式: {GetPermissionModeDisplay(PermissionMode)}");

        if (sessionManager?.CurrentSession != null)
        {
            sb.AppendLine($"  当前会话: {sessionManager.CurrentSession.SessionId}");
        }

        Messages.Add(new SystemMessageItem { Content = sb.ToString() });
    }

    /// <summary>
    /// 后台消费流式事件：只负责转发，全部事件按到达顺序投递到 UI 线程
    /// </summary>
    private async Task ConsumeStreamAsync(string input, CancellationToken ct, int runSeq)
    {
        var plannedCount = 0;

        await foreach (var evt in _agentHost.RunStreamingAsync(
            input,
            ConfirmCallback,
            PermissionMode,
            onPlannedAction: (tool, args) =>
            {
                if (runSeq != _activeRunSeq) return; // 运行序号失效（取消/新对话）则丢弃
                Interlocked.Increment(ref plannedCount);
                var content = $"📋 计划项（Plan 模式，未执行）: {tool} {ToolArgsFormatter.Summarize(args)}";
                Dispatcher.UIThread.Post(() => Messages.Add(new SystemMessageItem { Content = content }));
            },
            ct: ct))
        {
            var e = evt;
            Dispatcher.UIThread.Post(() => HandleStreamEvent(e, runSeq));
        }

        if (runSeq == _activeRunSeq)
            ReportPlannedActions(Volatile.Read(ref plannedCount));
    }

    /// <summary>
    /// Plan 模式回合结束汇总：本轮计划项均未执行，提示切换模式后重新发起。
    /// </summary>
    /// <param name="plannedCount">本轮收集到的计划项数量。</param>
    private void ReportPlannedActions(int plannedCount)
    {
        if (PermissionMode != ToolPermissionMode.Plan || plannedCount <= 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => Messages.Add(new SystemMessageItem
        {
            Content = $"📋 Plan 模式：本轮记录 {plannedCount} 个计划项，均未执行。"
                + "切换到 default / accept-edits 后重新发送即可执行。"
        }));
    }

    /// <summary>
    /// UI 线程上串行处理流式事件，保证消息流显示顺序：
    /// 思考 → 工具调用 → 工具结果 → ... → 最终正文
    /// </summary>
    private void HandleStreamEvent(StreamEvent evt, int runSeq)
    {
        // 运行序号防护：取消或新对话发起后，迟到的旧事件一律丢弃，
        // 避免"按 Esc 取消后，下次输入仍继续执行上次对话"。
        if (runSeq != _activeRunSeq)
            return;

        switch (evt)
        {
            case TextDeltaEvent t:
                // 正文项懒创建：首个文本到达时才插入，确保排在思考/工具之后
                if (_currentAssistant == null || _currentAssistant.IsComplete)
                {
                    FlushPending();
                    _currentAssistant = new AssistantMessageItem();
                    Messages.Add(_currentAssistant);
                }
                _pendingText.Append(t.Delta);
                _throttle?.Schedule();
                break;

            case ThinkingDeltaEvent t:
                // 思考内容作为独立消息项显示。
                // 若已输出过正文、当前又产生新的思考，则收尾旧思考块并另起一块，
                // 使"思考→输出→再思考"按发生顺序交错显示，而非堆积在顶部单块。
                if (_currentThinking == null || _currentThinking.IsComplete || _currentAssistant != null)
                {
                    FlushPending();
                    if (_currentThinking != null)
                        _currentThinking.IsComplete = true;
                    _currentThinking = new ThinkingMessageItem();
                    Messages.Add(_currentThinking);
                }
                _pendingThinking.Append(t.Delta);
                _throttle?.Schedule();
                break;

            case ToolCallStartedEvent tc:
                FlushPending();
                CompleteCurrentItems();
                Messages.Add(new ToolCallItem
                {
                    ToolName = tc.Name,
                    CallId = tc.CallId,
                    Arguments = tc.Arguments,
                    State = ToolCallState.Running
                });
                break;

            case ToolCallCompletedEvent tcc:
                UpdateToolCallState(tcc.CallId, ToolCallState.Done, null);
                break;

            case ToolCallFailedEvent tcf:
                UpdateToolCallState(tcf.CallId, ToolCallState.Failed, tcf.Error);
                break;

            case ErrorEvent e:
                Messages.Add(new SystemMessageItem { Content = e.Message, IsError = true });
                break;
        }
    }

    /// <summary>
    /// 结束当前思考/正文消息项（仅 UI 线程调用）
    /// </summary>
    private void CompleteCurrentItems()
    {
        if (_currentThinking != null)
        {
            _currentThinking.IsComplete = true;
            _currentThinking = null;
        }
        if (_currentAssistant != null)
        {
            _currentAssistant.IsComplete = true;
            _currentAssistant = null;
        }
    }

    /// <summary>
    /// 更新工具调用卡片状态（仅 UI 线程调用）
    /// </summary>
    private void UpdateToolCallState(string callId, ToolCallState state, string? error)
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i] is ToolCallItem tool && tool.CallId == callId)
            {
                // 必须先写错误信息再改状态：ToolCallCard 只订阅 State，
                // State 的 setter 会同步触发渲染，若此时 ErrorMessage 尚未赋值，
                // 卡片会永远显示兜底文案「工具执行失败」而丢掉真实原因。
                tool.ErrorMessage = error;
                tool.State = state;
                break;
            }
        }
    }

    /// <summary>
    /// 刷新待显示的增量（仅 UI 线程调用，直接同步追加，顺序确定）
    /// </summary>
    private void FlushPending()
    {
        if (_pendingText.Length > 0)
        {
            var text = _pendingText.ToString();
            _pendingText.Clear();
            _currentAssistant?.AppendDelta(text);
        }

        if (_pendingThinking.Length > 0)
        {
            var thinking = _pendingThinking.ToString();
            _pendingThinking.Clear();
            _currentThinking?.AppendDelta(thinking);
        }
    }

    /// <summary>
    /// 加载会话历史
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public async Task LoadSessionHistoryAsync(string sessionId)
    {
        if (IsRunning) return;

        IsSwitchingSession = true;
        try
        {
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
                        Thinking = msg.Thinking ?? "",
                        IsComplete = true
                    });
                }
            }
        }
        finally
        {
            IsSwitchingSession = false;
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
