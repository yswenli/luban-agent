/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels
*文件名： SessionViewModel
*版本号： V1.0.0.0
*唯一标识：会话 ViewModel
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：单个会话的运行态与消息流：每会话一份，支撑后台并行与会话间切换
*
*****************************************************************************/
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCodex.Views;
using LubanAgentCore.Services;
using LubanAgentCore.Utils;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Errors;
using LuBan.AIAgent.Orchestration;

namespace LubanAgentCodex.ViewModels;

/// <summary>
/// 单个会话的 ViewModel：持有该会话的消息流与整轮运行态（取消令牌、运行序号、
/// 节流缓冲、思考/正文当前项、运行中编排节点）。
/// 会话之间互不共享可变状态，故切换显示不影响后台会话继续运行。
/// </summary>
public partial class SessionViewModel : ObservableObject
{
    private readonly AgentHostService _agentHost;
    private readonly HostOrchestrationProgressSink? _orchestrationSink;
    private readonly Func<ToolPermissionMode> _permissionModeProvider;
    private readonly Func<string, bool> _isCurrentProvider;
    private readonly Action<SessionViewModel, bool>? _pendingConfirmationReporter;

    private CancellationTokenSource? _cts;
    private int _activeRunSeq; // 运行序号：每次发起对话自增，用于丢弃取消/新对话后迟到的旧事件
    private volatile bool _deniedByUser;
    private readonly StringBuilder _pendingText = new();
    private readonly StringBuilder _pendingThinking = new();
    private FlushThrottle? _throttle;
    private AssistantMessageItem? _currentAssistant;
    private ThinkingMessageItem? _currentThinking;
    private int _currentThinkingRound; // 当前思考块所属的 LLM 轮次号（0=未标注）
    private readonly List<OrchestrationNodeItem> _runningNodes = new();

    /// <summary>
    /// 创建会话 ViewModel。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="agentHost">Agent 宿主服务（按 sessionId 路由）。</param>
    /// <param name="orchestrationSink">编排进度出口（可空）。</param>
    /// <param name="permissionModeProvider">权限模式提供者（读取宿主当前模式）。</param>
    /// <param name="isCurrentProvider">判断该会话是否为当前选中会话（用于工具确认按会话归属）。</param>
    /// <param name="pendingConfirmationReporter">待确认状态上报（参数：会话、是否待确认），用于侧栏徽标。</param>
    public SessionViewModel(
        string sessionId,
        AgentHostService agentHost,
        HostOrchestrationProgressSink? orchestrationSink,
        Func<ToolPermissionMode> permissionModeProvider,
        Func<string, bool> isCurrentProvider,
        Action<SessionViewModel, bool>? pendingConfirmationReporter = null)
    {
        SessionId = sessionId;
        _agentHost = agentHost;
        _orchestrationSink = orchestrationSink;
        _permissionModeProvider = permissionModeProvider;
        _isCurrentProvider = isCurrentProvider;
        _pendingConfirmationReporter = pendingConfirmationReporter;
    }

    /// <summary>
    /// 会话标识
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// 消息集合（本会话独立）
    /// </summary>
    public ObservableCollection<MessageItemBase> Messages { get; } = new();

    /// <summary>
    /// 本会话是否正在运行
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// 发送一条用户消息并消费流式回复（不含附件解析，附件由宿主解析后传入）。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <param name="attachmentPaths">附件原始路径（送入 Agent）。</param>
    /// <param name="attachments">附件展示信息（渲染到用户消息）。</param>
    public async Task SendAsync(
        string input,
        IReadOnlyList<string> attachmentPaths,
        IReadOnlyList<AttachmentDisplayInfo> attachments)
    {
        Messages.Add(new UserMessageItem { Content = input, Attachments = attachments });

        // 新一轮开始：清除上一轮的拒绝终止标志
        _deniedByUser = false;
        _cts = new CancellationTokenSource();
        var runSeq = Interlocked.Increment(ref _activeRunSeq); // 本次对话运行序号；取消/新对话将使其失效

        // 新回合开始：清空上一回合遗留的运行中节点（节点卡片保留在消息流中作为历史）
        _runningNodes.Clear();
        _throttle ??= new FlushThrottle(
            () => Dispatcher.UIThread.Post(FlushPending),
            TimeSpan.FromMilliseconds(50));

        IsRunning = true;

        try
        {
            // 关键：消费循环放后台线程，避免确认回调阻塞 UI 线程导致死锁；
            // 事件本身全部投递到 UI 线程按序处理（Dispatcher 队列 FIFO 保序）
            await Task.Run(() => ConsumeStreamAsync(input, attachmentPaths, _cts.Token, runSeq));
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new SystemMessageItem { Content = _deniedByUser ? "已拒绝，本轮终止" : "已取消" });
        }
        catch (AgentApiException apiEx)
        {
            // 框架已完成错误分类：只渲染友好文案，原始异常进日志
            Messages.Add(new SystemMessageItem
            {
                Content = apiEx.Error.FriendlyMessage,
                IsError = true
            });
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
            FinalizeRun();
        }
    }

    /// <summary>
    /// 执行一轮提示词并返回累积正文（供 AGENTS.md 初始化等需要完整文本的场景）。
    /// 异常由调用方处理；本方法只负责运行态装配与收尾。
    /// </summary>
    /// <param name="prompt">提示词。</param>
    /// <returns>本轮累积正文文本。</returns>
    public async Task<string> RunPromptAsync(string prompt)
    {
        _deniedByUser = false;
        _cts = new CancellationTokenSource();
        var runSeq = Interlocked.Increment(ref _activeRunSeq);
        _runningNodes.Clear();
        _throttle ??= new FlushThrottle(
            () => Dispatcher.UIThread.Post(FlushPending),
            TimeSpan.FromMilliseconds(50));

        IsRunning = true;
        try
        {
            return await Task.Run(() => ConsumeStreamAsync(prompt, Array.Empty<string>(), _cts.Token, runSeq));
        }
        finally
        {
            FinalizeRun();
        }
    }

    /// <summary>
    /// 取消本会话当前运行。
    /// </summary>
    public void Cancel()
    {
        if (_cts == null)
            return;
        // 失效当前运行序号：丢弃取消后迟到的旧事件（避免下次输入仍执行上次对话）
        Interlocked.Increment(ref _activeRunSeq);
        _cts.Cancel();
    }

    /// <summary>
    /// 清空消息流与运行中节点。
    /// </summary>
    public void Clear()
    {
        Messages.Clear();
        _runningNodes.Clear();
    }

    /// <summary>
    /// 运行收尾：刷新缓冲、结束当前项、落定运行中节点、复位运行标志并释放令牌。
    /// </summary>
    private void FinalizeRun()
    {
        // 收尾同样投递到 UI 线程，排在所有已投递事件之后执行
        Dispatcher.UIThread.Post(() =>
        {
            FlushPending();
            CompleteCurrentItems();
            // 回合收尾：把仍标记为运行中的节点落定为结束态，避免卡片一直显示"运行中"
            foreach (var node in _runningNodes)
            {
                node.IsComplete = true;
            }
            _runningNodes.Clear();
            IsRunning = false;
        });
        var cts = _cts;
        _cts = null;
        cts?.Dispose();
    }

    /// <summary>
    /// 后台消费流式事件：只负责转发，全部事件按到达顺序投递到 UI 线程
    /// </summary>
    /// <returns>本回合累积的正文文本</returns>
    private async Task<string> ConsumeStreamAsync(
        string input, IReadOnlyList<string> attachmentPaths, CancellationToken ct, int runSeq)
    {
        var plannedCount = 0;
        var sb = new StringBuilder();

        // 编排进度出口：执行在工具内部、不经对话流，故本轮订阅、结束退订；
        // 迟到事件按运行序号丢弃（可能来自并行节点线程，需编组到 UI 线程）
        Action<OrchestrationProgressEvent>? onProgress = _orchestrationSink is null
            ? null
            : p => Dispatcher.UIThread.Post(() =>
            {
                if (runSeq != _activeRunSeq) return;
                HandleOrchestrationProgress(p);
            });
        if (_orchestrationSink is not null && onProgress is not null)
            _orchestrationSink.Progress += onProgress;

        try
        {
            await foreach (var evt in _agentHost.RunStreamingAsync(
                input,
                attachmentPaths,
                ConfirmCallback,
                _permissionModeProvider(),
                onPlannedAction: (tool, args) =>
                {
                    if (runSeq != _activeRunSeq) return; // 运行序号失效（取消/新对话）则丢弃
                    Interlocked.Increment(ref plannedCount);
                    var content = $"📋 计划项（Plan 模式，未执行）: {tool} {ToolArgsFormatter.Summarize(args)}";
                    Dispatcher.UIThread.Post(() => Messages.Add(new SystemMessageItem { Content = content }));
                },
                onUserDenied: (tool, args) =>
                {
                    // 用户明确拒绝：标记并立即终止本轮（框架保证 token 未取消，即非 Esc）
                    if (runSeq != _activeRunSeq) return;
                    _deniedByUser = true;
                    _cts?.Cancel();
                },
                sessionId: SessionId,
                ct: ct))
            {
                var e = evt;
                if (e is TextDeltaEvent delta)
                    sb.Append(delta.Delta);
                Dispatcher.UIThread.Post(() => HandleStreamEvent(e, runSeq));
            }

            if (runSeq == _activeRunSeq)
                ReportPlannedActions(Volatile.Read(ref plannedCount));

            return sb.ToString();
        }
        finally
        {
            if (_orchestrationSink is not null && onProgress is not null)
                _orchestrationSink.Progress -= onProgress;
        }
    }

    /// <summary>
    /// Plan 模式回合结束汇总：本轮计划项均未执行，提示切换模式后重新发起。
    /// </summary>
    /// <param name="plannedCount">本轮收集到的计划项数量。</param>
    private void ReportPlannedActions(int plannedCount)
    {
        if (_permissionModeProvider() != ToolPermissionMode.Plan || plannedCount <= 0)
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
                // 一个思考块 = 一次 LLM 请求/回复：同一轮内正文夹在思考中间也并入同一块，
                // 仅当进入下一轮（工具调用后再思考，LlmRound 变化）或当前块已收尾时才另起一块，
                // 使"思考→工具→思考"跨轮按发生顺序交错，而单轮内不再被正文误切。
                if (_currentThinking == null || _currentThinking.IsComplete
                    || (t.LlmRound > 0 && t.LlmRound != _currentThinkingRound))
                {
                    FlushPending();
                    if (_currentThinking != null)
                        _currentThinking.IsComplete = true;
                    _currentThinking = new ThinkingMessageItem();
                    _currentThinkingRound = t.LlmRound;
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
                UpdateToolCallState(tcc.CallId, ToolCallState.Done, null, tcc.ResultSummary);
                break;

            case ToolCallFailedEvent tcf:
                UpdateToolCallState(tcf.CallId, ToolCallState.Failed, tcf.Error, null);
                break;

            case ErrorEvent e:
                Messages.Add(new SystemMessageItem { Content = e.Message, IsError = true });
                break;

            case OrchestrationProgressEvent p:
                HandleOrchestrationProgress(p);
                break;
        }
    }

    /// <summary>
    /// 处理编排进度事件：节点级事件渲染为可点击的节点卡片（点击查看子代理详情），
    /// 其余事件追加一行系统提示，让规划/反思等长耗时阶段有实时反馈。
    /// （渲染口径与 CLI 的 HandleOrchestrationProgress 对齐）
    /// </summary>
    /// <param name="progress">编排进度事件。</param>
    private void HandleOrchestrationProgress(OrchestrationProgressEvent progress)
    {
        // 节点开始：插入节点卡片，后续活动/完成事件按 NodeId 回填
        if (progress.EventType == ProgressEventType.NodeStarted)
        {
            var nodeBlock = new OrchestrationNodeItem
            {
                NodeId = progress.NodeId ?? "",
                Description = progress.Message ?? progress.NodeId ?? "子任务"
            };
            _runningNodes.Add(nodeBlock);
            Messages.Add(nodeBlock);
            return;
        }

        // 节点活动：把子代理想法/工具调用/结果追加到对应节点卡片
        if (progress.EventType == ProgressEventType.NodeActivity)
        {
            if (progress.Activity is null)
                return;
            FindNode(progress.NodeId)?.AppendActivity(progress.Activity);
            return;
        }

        // 节点结束：收尾节点卡片（命中则不再追加系统提示行）
        if (progress.EventType is ProgressEventType.NodeCompleted or ProgressEventType.NodeFailed or ProgressEventType.NodeSkipped)
        {
            var nodeBlock = FindNode(progress.NodeId);
            if (nodeBlock != null)
            {
                nodeBlock.ElapsedSeconds = progress.ElapsedSeconds.GetValueOrDefault();
                if (progress.EventType == ProgressEventType.NodeFailed)
                {
                    nodeBlock.IsFailed = true;
                    nodeBlock.Error = progress.Error;
                }
                else if (progress.EventType == ProgressEventType.NodeSkipped)
                {
                    nodeBlock.IsSkipped = true;
                }
                else
                {
                    nodeBlock.IsComplete = true;
                }
                _runningNodes.Remove(nodeBlock);
                return;
            }
        }

        var text = progress.EventType switch
        {
            ProgressEventType.PlanningStarted => $"⏳ {progress.Message ?? "正在规划任务…"}",
            ProgressEventType.PlanningCompleted => $"▸ {progress.Message ?? "任务图谱已生成"}",
            ProgressEventType.NodeStarted => $"▶ 开始执行: {progress.Message ?? progress.NodeId}",
            ProgressEventType.NodeCompleted => $"✓ {progress.Message ?? progress.NodeId} · {progress.ElapsedSeconds.GetValueOrDefault():F1}s",
            ProgressEventType.NodeFailed => $"✗ {progress.Message ?? progress.NodeId} · {progress.Error ?? "失败"}",
            ProgressEventType.NodeSkipped => $"− {progress.Message ?? progress.NodeId} · 已跳过（前驱失败）",
            ProgressEventType.ReflectionStarted => $"↻ {progress.Message ?? "反思重规划"}",
            _ => null
        };

        if (text is null)
            return;

        Messages.Add(new SystemMessageItem
        {
            Content = text,
            IsError = progress.EventType == ProgressEventType.NodeFailed
        });
    }

    /// <summary>
    /// 按节点标识查找运行中的节点卡片；未提供 NodeId 时退化为最近一个未结束的节点
    /// </summary>
    /// <param name="nodeId">节点标识。</param>
    /// <returns>节点卡片，未命中返回 null。</returns>
    private OrchestrationNodeItem? FindNode(string? nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
            return _runningNodes.FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));

        return _runningNodes.LastOrDefault(n => !n.IsComplete && !n.IsFailed);
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
    private void UpdateToolCallState(string callId, ToolCallState state, string? error, string? resultSummary)
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i] is ToolCallItem tool && tool.CallId == callId)
            {
                // 必须先写错误信息/结果摘要再改状态：ToolCallCard 只订阅 State，
                // State 的 setter 会同步触发渲染，若此时 ErrorMessage/ResultSummary 尚未赋值，
                // 卡片会永远显示兜底文案「工具执行失败」而丢掉真实原因/结果摘要。
                tool.ErrorMessage = error;
                tool.ResultSummary = resultSummary;
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

    private ConfirmResult ConfirmCallback(string toolName, IReadOnlyDictionary<string, object?> args)
    {
        var ct = _cts?.Token ?? CancellationToken.None;

        // 非当前选中会话：先在侧栏挂待确认徽标，并阻塞等待用户切到本会话再弹窗，
        // 避免后台会话的确认模态打断当前会话，或两个会话的确认模态互相堆叠。
        var deferred = !_isCurrentProvider(SessionId);
        if (deferred)
            _pendingConfirmationReporter?.Invoke(this, true);

        try
        {
            while (!_isCurrentProvider(SessionId))
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(50);
            }

            return ShowConfirmDialog(toolName, args, ct);
        }
        finally
        {
            if (deferred)
                _pendingConfirmationReporter?.Invoke(this, false);
        }
    }

    /// <summary>
    /// 在 UI 线程弹出工具确认模态并等待用户选择（阻塞调用线程）。
    /// </summary>
    /// <param name="toolName">待确认的工具名。</param>
    /// <param name="args">工具参数。</param>
    /// <param name="ct">取消令牌；取消时提前唤醒。</param>
    /// <returns>用户选择结果。</returns>
    private ConfirmResult ShowConfirmDialog(string toolName, IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        using var done = new ManualResetEventSlim(false);
        var result = ConfirmResult.Deny;

        using var ctr = ct.CanBeCanceled ? ct.Register(() => done.Set()) : default;

        // 工具确认改为模态对话框：在 UI 线程弹出并等待选择，agent 线程阻塞在 done 上
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                result = await Dialogs.ShowToolConfirmAsync(owner, toolName, args, ct);
                Messages.Add(new SystemMessageItem { Content = FormatConfirmRecord(toolName, result) });
            }
            catch (Exception ex)
            {
                Logger.Warn($"工具确认对话框异常，按拒绝处理: {ex.Message}");
                result = ConfirmResult.Deny;
            }
            finally
            {
                done.Set();
            }
        });

        // 无限期等待用户响应；Esc/终止时经 ct 注册提前唤醒（ct 可取消时抛 OCE，由 SendAsync 收尾）
        if (ct.CanBeCanceled)
            done.Wait(ct);
        else
            done.Wait();

        return result;
    }

    /// <summary>
    /// 确认结果在消息流留痕，如「已允许 执行命令」
    /// </summary>
    private static string FormatConfirmRecord(string toolName, ConfirmResult result)
    {
        var action = ToolDisplayNames.GetAction(toolName);
        return result switch
        {
            ConfirmResult.Allow => $"已允许 {action}",
            ConfirmResult.AllowAll => $"已允许 {action}（本轮全部允许）",
            ConfirmResult.AllowSession => $"已允许 {action}（本会话全部允许）",
            _ => $"已拒绝 {action}"
        };
    }
}