/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： AgentRunHandle
*描述：一轮对话的运行时句柄：分配 seq、维护尾帧环形缓冲、累积续流快照、广播给订阅者
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>轮次阶段。</summary>
public enum AgentRunStage
{
    /// <summary>运行中。</summary>
    Running,

    /// <summary>正常跑完。</summary>
    Done,

    /// <summary>被取消。</summary>
    Cancelled,

    /// <summary>异常终止。</summary>
    Failed
}

/// <summary>订阅结果：订阅者句柄 + 接入瞬间应补发的帧（快照在前）。</summary>
/// <param name="Subscription">订阅者句柄；读者用 <see cref="AgentRunSubscription.Reader"/>。</param>
/// <param name="Pending">接入瞬间的补发帧；为空表示无缺口。</param>
public sealed record AgentRunAttachResult(
    AgentRunSubscription Subscription,
    IReadOnlyList<AgentSseFrame> Pending);

/// <summary>
/// 一次订阅。由 <see cref="AgentRunHandle.Subscribe"/> 创建，释放即注销（幂等）。
/// </summary>
public sealed class AgentRunSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private int _disposed;

    internal AgentRunSubscription(ChannelReader<AgentSseFrame> reader, Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    /// <summary>帧读取端。</summary>
    public ChannelReader<AgentSseFrame> Reader { get; }

    /// <summary>注销订阅；重复调用不抛。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _unsubscribe();
    }
}

/// <summary>
/// 一轮对话的运行时句柄。
/// </summary>
/// <remarks>
/// <para>
/// 轮次自身的生命周期由 <see cref="AgentRunHub"/> 管理；本类型只负责三件事：
/// 给每帧分配 <c>seq</c>、把帧放进尾帧环形缓冲、把帧广播给当前订阅者。
/// </para>
/// <para>
/// 所有状态都在 <see cref="_gate"/> 内更新；通道的完成与订阅者摘除放在锁外，
/// 避免慢订阅者的 <c>TryComplete</c> 把发布线程卡住。
/// </para>
/// <para>
/// 订阅通道使用 <see cref="BoundedChannelFullMode.Wait"/>：发布侧只做 <c>TryWrite</c>，
/// 写不进就说明该订阅者已经跟不上，直接摘除，绝不阻塞发布。
/// </para>
/// </remarks>
public sealed class AgentRunHandle
{
    private readonly object _gate = new();
    private readonly IAgentHost _host;
    private readonly DiContainer _services;
    private readonly AgentRunHubOptions _options;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _runCts;

    /// <summary>尾帧缓冲上限；构造时由 <see cref="AgentRunHubOptions.BacklogCapacity"/> 钳到非负，避免负数导致空队列出队。</summary>
    private readonly int _backlogCapacity;

    private readonly Queue<(int Seq, AgentSseFrame Frame)> _backlog = new();
    private readonly List<(AgentRunSubscription Subscription, ChannelWriter<AgentSseFrame> Writer)> _subscribers = [];

    private readonly List<AgentRunSegmentSnapshot> _segments = [];
    private readonly List<AgentRunPlanSnapshot> _plans = [];
    private readonly List<string> _progress = [];
    private readonly List<AgentRunNoticeSnapshot> _notices = [];

    private int _seq;
    private int _attachmentsAdded;
    private int _attachmentsSkipped;
    private string _text = "";
    private string _thinking = "";
    private AgentRunConfirmationSnapshot? _pendingConfirmation;
    private AgentRunQuestionSnapshot? _pendingQuestion;

    /// <summary>发起提问的工具调用标识；该调用完成/失败即代表提问已结束，待回答提问随之作废。</summary>
    private string? _pendingQuestionCallId;

    /// <summary>提问工具的注册名（工具组以该名暴露给模型，见 InteractionToolPlugin）。</summary>
    private const string AskUserQuestionToolName = "AskUserQuestion";

    /// <summary>当前思考区间起点（Unix 毫秒）；0 表示没有进行中的思考区间。</summary>
    private long _thinkingStartedAtMs;

    /// <summary>当前思考区间最后一帧的时刻（Unix 毫秒）。</summary>
    private long _thinkingLastAtMs;

    /// <summary>工具调用起点（Unix 毫秒），按调用标识记录。</summary>
    private readonly Dictionary<string, long> _toolStartedAtMs = new(StringComparer.Ordinal);

    /// <summary>当前阶段；锁内写、锁外读（<see cref="IsCompleted"/> / <see cref="Stage"/>），用 volatile 保证可见性。</summary>
    private volatile AgentRunStage _stage = AgentRunStage.Running;

    internal AgentRunHandle(
        string runId,
        string sessionId,
        string userId,
        IAgentHost host,
        DiContainer services,
        CancellationTokenSource runCts,
        AgentRunHubOptions options,
        Action<string> log)
    {
        RunId = runId;
        SessionId = sessionId;
        UserId = userId;
        _host = host;
        _services = services;
        _runCts = runCts;
        _options = options;
        _log = log;
        _backlogCapacity = Math.Max(0, options.BacklogCapacity);
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>本轮标识；随 <c>session</c> 帧下发，取消时回传。</summary>
    public string RunId { get; }

    /// <summary>所属会话。</summary>
    public string SessionId { get; }

    /// <summary>所属用户。</summary>
    public string UserId { get; }

    /// <summary>轮次开始时间。</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>本轮取消令牌；对话体用它。宿主关闭时会一并取消。</summary>
    public CancellationToken Token => _runCts.Token;

    /// <summary>是否已进入终态。</summary>
    public bool IsCompleted => _stage != AgentRunStage.Running;

    /// <summary>当前阶段。</summary>
    public AgentRunStage Stage => _stage;

    /// <summary>
    /// 本轮后台收尾任务：完成即表示作用域已释放、登记已移除。
    /// </summary>
    /// <remarks>
    /// 构造即存在、由 <see cref="MarkCompleted"/> 置位，而不是等 hub 赋值：
    /// 登记（<c>TryAdd</c>）与赋值之间若被抢占方或取消方读到默认的「已完成」，
    /// 会把还在跑的轮次当成已收尾。
    /// </remarks>
    internal Task Completion => _completion.Task;

    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>本轮作用域内解析到的宿主实例；对话体用它产出事件流。</summary>
    public IAgentHost Host => _host;

    /// <summary>标记后台收尾已完成（作用域已释放、登记已移除）；重复调用无副作用。</summary>
    internal void MarkCompleted() => _completion.TrySetResult();

    /// <summary>轮次取消；重复调用与已释放均不抛。</summary>
    internal void Cancel()
    {
        try
        {
            _runCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已被收尾释放，忽略
        }
    }

    /// <summary>
    /// 发布一帧：分配 seq → 更新快照 → 入缓冲 → 广播。
    /// </summary>
    /// <param name="frame">要发布的帧；<c>null</c> 表示心跳，不进轮次。</param>
    /// <remarks>
    /// 心跳由每个订阅者的写出泵自己发 <c>: ping</c>，不占 seq、不进快照与缓冲，
    /// 否则重连拿到的序号里会混进无人消费的占位帧。
    /// </remarks>
    public void Publish(AgentSseFrame? frame)
    {
        if (frame is null)
        {
            return;
        }

        List<(AgentRunSubscription Subscription, ChannelWriter<AgentSseFrame> Writer)>? slow = null;
        lock (_gate)
        {
            if (_stage != AgentRunStage.Running)
            {
                return;
            }

            var seq = ++_seq;
            // 序号同时写进帧本身：订阅者泵要按帧输出 `id:` 行，重连时前端才能拿它当 fromSeq
            var stamped = frame with { Seq = seq };
            Accumulate(stamped, seq);

            _backlog.Enqueue((seq, stamped));
            while (_backlog.Count > _backlogCapacity)
            {
                _backlog.Dequeue();
            }

            foreach (var pair in _subscribers)
            {
                if (!pair.Writer.TryWrite(stamped))
                {
                    (slow ??= []).Add(pair);
                }
            }

            if (slow is not null)
            {
                foreach (var pair in slow)
                {
                    _subscribers.Remove(pair);
                }
            }
        }

        if (slow is null)
        {
            return;
        }

        foreach (var pair in slow)
        {
            pair.Writer.TryComplete();
            pair.Subscription.Dispose();
            _log($"订阅者过慢已摘除：run={RunId}");
        }
    }

    /// <summary>
    /// 订阅本轮的后续帧。
    /// </summary>
    /// <param name="fromSeq">已收到的最大 seq；<c>null</c> 表示页面刷新、内存为空，需下发快照。</param>
    /// <param name="snapshotOnGap">
    /// 缺口已滑出缓冲时是否退回快照路径。首连接（<c>/agent/chat</c>）传 <c>false</c>：
    /// 它本来就从头收，多发一个 <c>run.snapshot</c> 只会让前端多出一条不认识的帧。
    /// </param>
    /// <returns>订阅结果；轮次已结束返回 <c>null</c>。</returns>
    public AgentRunAttachResult? Subscribe(int? fromSeq, bool snapshotOnGap = true)
    {
        List<AgentSseFrame> pending;
        AgentRunSubscription subscription;

        lock (_gate)
        {
            if (_stage != AgentRunStage.Running)
            {
                return null;
            }

            var channel = Channel.CreateBounded<AgentSseFrame>(new BoundedChannelOptions(_options.SubscriberCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            // 以下标为身份注销：写入端实例唯一，闭包不必自引用订阅者句柄
            var writer = channel.Writer;
            subscription = new AgentRunSubscription(channel.Reader, () => Unsubscribe(writer));

            pending = BuildPending(fromSeq, snapshotOnGap);
            _subscribers.Add((subscription, writer));
        }

        return new AgentRunAttachResult(subscription, pending);
    }

    /// <summary>注销订阅；幂等。</summary>
    /// <param name="writer">订阅者对应的写入端。</param>
    private void Unsubscribe(ChannelWriter<AgentSseFrame> writer)
    {
        lock (_gate)
        {
            _subscribers.RemoveAll(x => ReferenceEquals(x.Writer, writer));
        }

        writer.TryComplete();
    }

    /// <summary>
    /// 收尾：置终态、完成全部订阅者通道。
    /// </summary>
    /// <param name="stage">终态（不应传 <see cref="AgentRunStage.Running"/>）。</param>
    /// <remarks>必须在释放作用域之前调用，让订阅者先拿到通道完成信号。</remarks>
    internal void Finish(AgentRunStage stage)
    {
        List<ChannelWriter<AgentSseFrame>> writers;
        lock (_gate)
        {
            CloseThinkingSegment();
            _stage = stage;
            writers = _subscribers.Select(x => x.Writer).ToList();
            _subscribers.Clear();
        }

        foreach (var writer in writers)
        {
            writer.TryComplete();
        }
    }

    /// <summary>释放本轮作用域（含宿主实例）与取消源。</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _services.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log($"轮次作用域释放失败：run={RunId} {ex.Message}");
        }

        // 收尾后不再有人等这个令牌；Cancel 对已释放的取消源按空操作处理
        _runCts.Dispose();
    }

    private List<AgentSseFrame> BuildPending(int? fromSeq, bool snapshotOnGap)
    {
        if (fromSeq is null)
        {
            return BuildSnapshotPending();
        }

        var seq = fromSeq.Value;
        var windowStart = _backlog.Count == 0 ? int.MaxValue : _backlog.Peek().Seq;
        // 收到 seq 之后无缺口：缓冲起点不晚于 seq + 1
        if (_backlog.Count == 0 || seq + 1 >= windowStart)
        {
            return _backlog.Where(x => x.Seq > seq).Select(x => x.Frame).ToList();
        }

        if (snapshotOnGap)
        {
            return BuildSnapshotPending();
        }

        return _backlog.Where(x => x.Seq > seq).Select(x => x.Frame).ToList();
    }

    private List<AgentSseFrame> BuildSnapshotPending()
    {
        var snapshot = new AgentRunSnapshot
        {
            RunId = RunId,
            SessionId = SessionId,
            StartedAt = StartedAt,
            Seq = _seq,
            Text = _text,
            Thinking = _thinking,
            Tools = [.. _segments.Where(x => x.Tool != null).Select(x => x.Tool!)],
            Segments = [.. _segments],
            Plans = [.. _plans],
            Progress = [.. _progress],
            AttachmentsAdded = _attachmentsAdded,
            AttachmentsSkipped = _attachmentsSkipped,
            Notices = [.. _notices],
            PendingConfirmation = _pendingConfirmation,
            // 已过期的提问不再下发：子代理提问的工具收尾帧不回到父轮次，
            // 只靠工具收尾清理会留下残留，刷新后仍会弹回
            PendingQuestion = _pendingQuestion is { } question && question.ExpiresAt > DateTimeOffset.UtcNow
                ? question
                : null
        };

        // 快照里的 Seq 就是缓冲末尾的序号，正文/工具/提示也都是同一把锁内取的最新值，
        // 因此不需要再补发缓冲里的帧；帧自身也带同一序号，前端可直接续传
        return [AgentSseFrame.Create(AgentSseEventNames.RunSnapshot, snapshot) with { Seq = _seq }];
    }

    /// <summary>按帧名累积快照。解析失败只记日志，不让坏帧掀翻轮次。</summary>
    private void Accumulate(AgentSseFrame frame, int seq)
    {
        try
        {
            switch (frame.Name)
            {
                case AgentSseEventNames.TextDelta:
                {
                    // 正文出现即思考区间结束：与段落「连续同类型」口径一致
                    CloseThinkingSegment();
                    var delta = ReadString(frame.Data, "delta") ?? "";
                    _text += delta;
                    AppendTextSegment(AgentRunSegmentSnapshot.TextKind, delta);
                    break;
                }

                case AgentSseEventNames.ThinkDelta:
                {
                    var at = ReadLong(frame.Data, "at") ?? NowMs();
                    if (_thinkingStartedAtMs == 0)
                    {
                        _thinkingStartedAtMs = at;
                    }

                    _thinkingLastAtMs = at;
                    var delta = ReadString(frame.Data, "delta") ?? "";
                    _thinking += delta;
                    // 新开的思考段带上区间起点，重连后才能接着算完整耗时
                    if (AppendTextSegment(AgentRunSegmentSnapshot.ThinkingKind, delta))
                    {
                        _segments[^1] = _segments[^1] with { StartedAtMs = at };
                    }
                    break;
                }

                case AgentSseEventNames.ToolStarted:
                {
                    CloseThinkingSegment();
                    var callId = ReadString(frame.Data, "callId") ?? "";
                    var at = ReadLong(frame.Data, "at") ?? NowMs();
                    // 同一调用重试：移除旧段落，保持画面里只有最新一次
                    _segments.RemoveAll(x => x.Tool?.CallId == callId);
                    _toolStartedAtMs[callId] = at;
                    _segments.Add(new AgentRunSegmentSnapshot
                    {
                        Kind = AgentRunSegmentSnapshot.ToolKind,
                        Tool = new AgentRunToolSnapshot
                        {
                            CallId = callId,
                            Name = ReadString(frame.Data, "name") ?? "",
                            Arguments = ReadRaw(frame.Data, "arguments"),
                            Status = "running",
                            StartedAtMs = at
                        }
                    });
                    _pendingConfirmation = null;
                    break;
                }

                case AgentSseEventNames.ToolCompleted:
                {
                    var callId = ReadString(frame.Data, "callId") ?? "";
                    var resultSummary = ReadString(frame.Data, "resultSummary");
                    var durationMs = TakeToolDurationMs(callId, ReadLong(frame.Data, "at"));
                    UpdateToolSegment(callId, tool => tool with
                    {
                        Status = "done",
                        ResultSummary = resultSummary,
                        Error = null,
                        DurationMs = durationMs
                    });
                    // 提问工具跑完（用户已作答/已取消）即作废待回答提问，避免刷新后弹回旧问题
                    ClearQuestionOnToolEnd(callId);
                    break;
                }

                case AgentSseEventNames.ToolFailed:
                {
                    var callId = ReadString(frame.Data, "callId") ?? "";
                    var error = ReadString(frame.Data, "error");
                    var durationMs = TakeToolDurationMs(callId, ReadLong(frame.Data, "at"));
                    UpdateToolSegment(callId, tool => tool with
                    {
                        Status = "failed",
                        ResultSummary = null,
                        Error = error,
                        DurationMs = durationMs
                    });
                    ClearQuestionOnToolEnd(callId);
                    break;
                }

                case AgentSseEventNames.PlanItem:
                    _plans.Add(new AgentRunPlanSnapshot
                    {
                        ToolName = ReadString(frame.Data, "toolName") ?? "",
                        Arguments = ReadRaw(frame.Data, "arguments")
                    });
                    break;

                case AgentSseEventNames.OrchestrationProgress:
                {
                    var message = ReadString(frame.Data, "message");
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        _progress.Add(message);
                    }

                    break;
                }

                case AgentSseEventNames.ToolConfirm:
                {
                    var timeout = ReadInt(frame.Data, "timeoutSeconds") ?? 0;
                    _pendingConfirmation = new AgentRunConfirmationSnapshot
                    {
                        ConfirmationId = ReadString(frame.Data, "confirmationId") ?? "",
                        ToolName = ReadString(frame.Data, "toolName") ?? "",
                        Arguments = ReadRaw(frame.Data, "arguments"),
                        TimeoutSeconds = timeout,
                        Origin = ReadOrigin(frame.Data),
                        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(timeout)
                    };
                    break;
                }

                case AgentSseEventNames.ToolQuestion:
                {
                    var timeout = ReadInt(frame.Data, "timeoutSeconds") ?? 0;
                    _pendingQuestion = new AgentRunQuestionSnapshot
                    {
                        QuestionId = ReadString(frame.Data, "questionId") ?? "",
                        QuestionsJson = ReadRaw(frame.Data, "questions"),
                        TimeoutSeconds = timeout,
                        Origin = ReadOrigin(frame.Data),
                        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(timeout)
                    };
                    // 记下发起提问的调用：只在该调用收尾时作废，避免并行编排里别的工具完成误清
                    _pendingQuestionCallId = _segments
                        .LastOrDefault(x => x.Tool is { Status: "running" }
                            && string.Equals(x.Tool.Name, AskUserQuestionToolName, StringComparison.Ordinal))
                        ?.Tool?.CallId;
                    break;
                }

                case AgentSseEventNames.ToolDenied:
                    _pendingConfirmation = null;
                    _pendingQuestion = null;
                    _pendingQuestionCallId = null;
                    _notices.Add(new AgentRunNoticeSnapshot { Event = frame.Name, Data = frame.Data });
                    break;

                case AgentSseEventNames.AttachmentAdded:
                    _attachmentsAdded++;
                    _notices.Add(new AgentRunNoticeSnapshot { Event = frame.Name, Data = frame.Data });
                    break;

                case AgentSseEventNames.AttachmentSkipped:
                    _attachmentsSkipped++;
                    _notices.Add(new AgentRunNoticeSnapshot { Event = frame.Name, Data = frame.Data });
                    break;

                case AgentSseEventNames.Error:
                    _notices.Add(new AgentRunNoticeSnapshot { Event = frame.Name, Data = frame.Data });
                    break;

                case AgentSseEventNames.ContextCompacted:
                    // 压缩提示是独立提示条目：重连/刷新后由快照回放，不能只留在实时流里
                    _notices.Add(new AgentRunNoticeSnapshot { Event = frame.Name, Data = frame.Data });
                    break;

                default:
                    // done / session 等不参与快照：前端重放时由历史接口负责
                    break;
            }
        }
        catch (Exception ex)
        {
            _log($"快照累积失败：run={RunId} seq={seq} frame={frame.Name} {ex.Message}");
        }
    }

    /// <summary>追加文本段落：与末段同类型时并入，否则新开一段，保证时间线顺序。</summary>
    /// <returns>是否新开了一段（false 表示并入末段）。</returns>
    private bool AppendTextSegment(string kind, string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return false;
        }

        if (_segments.Count > 0 && _segments[^1].Kind == kind && _segments[^1].Tool is null)
        {
            var last = _segments[^1];
            _segments[^1] = last with { Text = last.Text + delta };
            return false;
        }

        _segments.Add(new AgentRunSegmentSnapshot { Kind = kind, Text = delta });
        return true;
    }

    /// <summary>落定进行中的思考区间：把耗时写到时间线末尾的思考段上。</summary>
    private void CloseThinkingSegment()
    {
        if (_thinkingStartedAtMs == 0)
        {
            return;
        }

        var durationMs = (int)Math.Max(0, _thinkingLastAtMs - _thinkingStartedAtMs);
        _thinkingStartedAtMs = 0;
        _thinkingLastAtMs = 0;
        if (durationMs <= 0)
        {
            return;
        }

        if (_segments.Count > 0
            && _segments[^1].Tool is null
            && _segments[^1].Kind == AgentRunSegmentSnapshot.ThinkingKind)
        {
            _segments[^1] = _segments[^1] with { DurationMs = durationMs };
        }
    }

    /// <summary>取工具耗时：用开始记录与结束时刻做差；没有开始记录时返回 null。</summary>
    /// <param name="callId">调用标识。</param>
    /// <param name="endedAtMs">结束时刻（Unix 毫秒）；缺失时取当前时刻。</param>
    private int? TakeToolDurationMs(string callId, long? endedAtMs)
    {
        if (string.IsNullOrEmpty(callId) || !_toolStartedAtMs.Remove(callId, out var startedAtMs))
        {
            return null;
        }

        return (int)Math.Max(0, (endedAtMs ?? NowMs()) - startedAtMs);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>就地更新工具段落的状态；调用已不在时间线上时忽略。</summary>
    private void UpdateToolSegment(string callId, Func<AgentRunToolSnapshot, AgentRunToolSnapshot> mutate)
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            if (_segments[i].Tool is { } tool && tool.CallId == callId)
            {
                _segments[i] = _segments[i] with { Tool = mutate(tool) };
                return;
            }
        }
    }

    /// <summary>
    /// 作废当前待回答提问。用户作答/取消成功后由宿主直接调用：
    /// 子代理提问不在父轮次的工具卡里，无法靠工具收尾帧匹配 callId 来清理。
    /// </summary>
    /// <param name="questionId">提问标识；为空时不比对，清掉当前项。</param>
    /// <returns>确有待回答提问被清掉返回 true。</returns>
    public bool ClearPendingQuestion(string? questionId = null)
    {
        lock (_gate)
        {
            if (_pendingQuestion is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(questionId)
                && !string.Equals(_pendingQuestion.QuestionId, questionId, StringComparison.Ordinal))
            {
                return false;
            }

            _pendingQuestion = null;
            _pendingQuestionCallId = null;
            return true;
        }
    }

    /// <summary>提问工具调用收尾：仅当该调用就是发起提问的那次时才作废待回答提问。</summary>
    private void ClearQuestionOnToolEnd(string callId)
    {
        if (_pendingQuestion is null || _pendingQuestionCallId is null)
            return;
        if (!string.Equals(_pendingQuestionCallId, callId, StringComparison.Ordinal))
            return;

        _pendingQuestion = null;
        _pendingQuestionCallId = null;
    }

    private static string? ReadString(string data, string name)
    {
        using var doc = JsonDocument.Parse(data);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>读取确认来源对象（<c>{ nodeId, role, description }</c>）；缺失或类型不符返回 <c>null</c>。</summary>
    private static ToolConfirmationOrigin? ReadOrigin(string data)
    {
        using var doc = JsonDocument.Parse(data);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("origin", out var origin)
            || origin.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ToolConfirmationOrigin(
            ReadString(origin, "nodeId"),
            ReadString(origin, "role"),
            ReadString(origin, "description"));
    }

    private static int? ReadInt(string data, string name)
    {
        using var doc = JsonDocument.Parse(data);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static long? ReadLong(string data, string name)
    {
        using var doc = JsonDocument.Parse(data);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            ? number
            : null;
    }

    /// <summary>取属性和原始 JSON 文本；对象与数组保留原样，字符串取其值。</summary>
    private static string ReadRaw(string data, string name)
    {
        using var doc = JsonDocument.Parse(data);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty(name, out var value))
        {
            return "";
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : value.GetRawText();
    }
}