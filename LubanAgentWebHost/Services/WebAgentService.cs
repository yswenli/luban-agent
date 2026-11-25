/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebAgentService
*描述：对话业务：非流式 / SSE 流式 / 确认 / 取消 / 重建
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 对话业务。
/// </summary>
public static class WebAgentService
{
    private const string AgentDisabled =
        "LuBan Agent 未启用：请检查 appsettings 的 LuBanAgent 节与数据库配置";

    private const string CancelledMessage = "本轮对话已取消";

    /// <summary>提示词改写的输入上限（字符数），超长直接拒绝，避免用户误把长文档丢进来。</summary>
    private const int MaxOptimizeChars = 4000;

    /// <summary>提示词改写的整体超时：改写是即时交互，卡住就让用户重试而不是无限等待。</summary>
    private static readonly TimeSpan OptimizeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 改写的系统指令：只做表达重组，不执行内容。
    /// </summary>
    /// <remarks>
    /// 固定写在服务端而不是由调用方传入：改写结果的质量依赖这组约束（不执行、不臆造、信息不足先问），
    /// 放开给前端等于把产品行为交给终端决定。
    /// </remarks>
    private const string OptimizeInstruction = """
        你是需求改写助手。用户会发来一句待办的表达，你的唯一任务是把这句话改写成清楚、具体、可以直接交给执行方使用的需求表述。

        必须遵守：
        1. 不要执行、不要回答这句话描述的任务，也不要解释或点评你的改写。
        2. 保持用户原意，只补充让意图更明确所必需的内容；不要臆造用户没有提到的具体事实，例如具体数值、型号、版本号或时间。
        3. 输出一段紧凑的正文，先写要做什么，再点明范围或对象，最后写清期望的结果形态或验收要点。
        4. 如果缺少关键信息才能执行，在末尾用一句话列出需要补充的要点，而不是自行假设。
        5. 只输出改写后的正文：不要加引号、标题、序号、前缀或任何说明，语言与用户输入保持一致。
        """;

    /// <summary>非流式对话（不装配工具，返回正文 + 思考 + 附件摘要）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentChatRequest"/> JSON。</param>
    /// <returns>对话结果。</returns>
    public static Task<ActionResult> SendAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentChatRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        var prompt = input.Prompt?.Trim() ?? "";
        if (prompt.Length == 0)
        {
            return WebApi.FailJson(400, "prompt 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (scope.GetService(typeof(IAgentHost)) is not IAgentHost host)
        {
            return WebApi.FailJson(503, AgentDisabled);
        }

        var resolution = await WebSessionResolver
            .ResolveAsync(scope, WebApi.Runtime.UserId, input.SessionId,
                WebApi.Runtime.GetSessionCreationGate(WebApi.Runtime.UserId))
            .ConfigureAwait(false);
        if (!resolution.Ok)
        {
            return WebApi.FailJson(resolution.Code, resolution.Error ?? "会话解析失败");
        }

        var request = new AgentRunRequest
        {
            Input = prompt,
            SessionId = resolution.SessionId,
            SystemPrompt = string.IsNullOrWhiteSpace(input.System) ? null : input.System,
            Attachments = NormalizeAttachments(input.Attachments),
            EnableTools = false,
            ReasoningEffort = ParseEffort(input.ReasoningEffort) ?? ReasoningEffort.None,
            PermissionMode = ToolPermissionMode.Default
        };

        var content = new StringBuilder();
        var thinking = new StringBuilder();
        var added = new List<AttachmentAddedDto>();
        var skipped = new List<AttachmentSkippedDto>();
        string? error = null;

        try
        {
            await foreach (var evt in host
                .RunStreamingAsync(request, WebApi.Runtime.ShutdownToken)
                .ConfigureAwait(false))
            {
                switch (evt)
                {
                    case TextDeltaEvent text:
                        content.Append(text.Delta);
                        break;
                    case ThinkingDeltaEvent think:
                        thinking.Append(think.Delta);
                        break;
                    case AttachmentAddedEvent attachment:
                        added.Add(new AttachmentAddedDto(
                            attachment.Source ?? attachment.FileName,
                            attachment.FileName,
                            attachment.MediaType,
                            attachment.FileSize));
                        break;
                    case AttachmentSkippedEvent skip:
                        skipped.Add(new AttachmentSkippedDto(skip.Source, skip.Reason));
                        break;
                    case ErrorEvent failure:
                        error = failure.Message;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            error = CancelledMessage;
        }
        catch (Exception ex)
        {
            error = WebApi.SafeErrorMessage(ex);
        }

        // 有正文时按 200 + error 返回，避免调用方丢掉已生成内容
        if (error is not null && content.Length == 0 && thinking.Length == 0)
        {
            return WebApi.FailJson(502, error);
        }

        return WebApi.OkJson(new AgentChatResponse(
            resolution.SessionId,
            content.ToString(),
            thinking.ToString(),
            new AttachmentOverviewDto(added, skipped),
            error));
    });

    /// <summary>流式对话（SSE，装配工具与确认）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentChatRequest"/> JSON。</param>
    /// <returns>SSE 标记结果。</returns>
    /// <remarks>
    /// 轮次归属会话而不是这条连接：本次连接断开只摘订阅者，轮次继续在后台跑完，
    /// 客户端重开页面后可用 <see cref="AgentApiRoutes.Agent.Attach"/> 接上。
    /// </remarks>
    public static Task<ActionResult> ChatAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentChatRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        var prompt = input.Prompt?.Trim() ?? "";
        if (prompt.Length == 0)
        {
            return WebApi.FailJson(400, "prompt 不能为空");
        }

        // 以下校验必须全部在切换 Content-Type 之前完成：一旦开始写 SSE 就不能再改状态码
        var userId = WebApi.Runtime.UserId;
        SessionResolution resolution;
        await using (var probe = WebApi.Runtime.CreateRequestScope(userId))
        {
            if (probe.GetService(typeof(IAgentHost)) is not IAgentHost)
            {
                return WebApi.FailJson(503, AgentDisabled);
            }

            resolution = await WebSessionResolver
                .ResolveAsync(probe, userId, input.SessionId, WebApi.Runtime.GetSessionCreationGate(userId))
                .ConfigureAwait(false);
            if (!resolution.Ok)
            {
                return WebApi.FailJson(resolution.Code, resolution.Error ?? "会话解析失败");
            }
        }

        // 起轮次：作用域、宿主实例与取消源都由轮次中心持有，连接只是它的一个订阅者
        AgentRunHandle run;
        try
        {
            // 文件链接工具的基地址只能取自用户此刻的请求头：轮次在后台跑，起跑后请求上下文已释放，
            // 故在这里（仍在请求线程上）先解析出来，再由轮次体内挂到调用链上
            var baseAddress = WebRequestAddress.Resolve(
                WebApi.Request,
                WebApi.Runtime.Settings.Port,
                WebApi.Runtime.Settings.UseHttps);
            run = await WebApi.Runtime.Runs.StartAsync(userId, resolution.SessionId, async (handle, ct) =>
            {
                using var linkScope = WebFileLinkScope.Begin(baseAddress);
                var sideFrames = new ConcurrentQueue<AgentSseFrame>();

                // 自动重试提示：回调挂在按用户共享的配置实例上，出口用作用域绑定到本回合，
                // 并发会话各自只收到自己的重试提示
                if (handle.Host.Services.GetService<IOptions<LuBanAgentOptions>>() is { } agentOptions)
                {
                    agentOptions.Value.OnApiRetry = ApiRetryScope.Publish;
                }
                using var retryScope = ApiRetryScope.Begin(
                    notice => sideFrames.Enqueue(ApiRetryScope.CreateFrame(notice)));

                // 编排进度：子代理在工具调用内部执行，进度不进对话流；
                // 订阅本轮作用域内的出口，逐条转成 orchestration.progress 帧
                var progressSink = handle.Host.Services.GetService<HostOrchestrationProgressSink>();
                void OnProgress(OrchestrationProgressEvent progress) => handle.Publish(
                    AgentSseFrame.Create(AgentSseEventNames.OrchestrationProgress, OrchestrationProgressPayload.From(progress)));
                if (progressSink is not null)
                {
                    progressSink.Progress += OnProgress;
                }

                // 自动命名在首条消息进入回合后异步完成：完成时本轮流仍开着就补一帧，
                // 流已收尾时 Publish 自动丢弃（前端会在回合结束刷新会话列表时带出）
                var titleNotifier = handle.Host.Services.GetService<SessionTitleNotifier>();
                void OnTitleChanged(SessionTitleChanged change)
                {
                    if (!string.Equals(change.SessionId, resolution.SessionId, StringComparison.Ordinal)) return;

                    handle.Publish(AgentSseFrame.Create(
                        AgentSseEventNames.SessionTitle,
                        new { sessionId = change.SessionId, title = change.Title }));
                }
                if (titleNotifier is not null)
                {
                    titleNotifier.Changed += OnTitleChanged;
                }

                var request = new AgentRunRequest
                {
                    Input = prompt,
                    SessionId = resolution.SessionId,
                    SystemPrompt = string.IsNullOrWhiteSpace(input.System) ? null : input.System,
                    Attachments = NormalizeAttachments(input.Attachments),
                    EnableTools = input.EnableTools,
                    ReasoningEffort = ParseEffort(input.ReasoningEffort)
                        ?? ParseEffort(await handle.Host.Services.GetRequiredService<ISessionGenerationSettings>()
                            .ResolveReasoningEffortAsync(resolution.SessionId, ct)
                            .ConfigureAwait(false)),
                    PermissionMode = ParsePermissionMode(input.PermissionMode),
                    OnPlannedAction = (toolName, arguments) => sideFrames.Enqueue(
                        AgentSseFrame.Create(AgentSseEventNames.PlanItem, new { toolName, arguments })),
                    OnUserDenied = (toolName, arguments) => sideFrames.Enqueue(
                        AgentSseFrame.Create(AgentSseEventNames.ToolDenied, new { toolName, arguments }))
                };

                try
                {
                    await foreach (var frame in StreamFramesAsync(handle.Host, request, resolution, sideFrames, handle.RunId, ct)
                        .ConfigureAwait(false))
                    {
                        // null（心跳占位）由轮次中心丢弃：心跳改由每个订阅者的写出泵自己发
                        handle.Publish(frame);
                    }
                }
                finally
                {
                    if (progressSink is not null)
                    {
                        progressSink.Progress -= OnProgress;
                    }

                    if (titleNotifier is not null)
                    {
                        titleNotifier.Changed -= OnTitleChanged;
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (AgentRunConflictException)
        {
            return WebApi.FailJson(409, "上一轮尚未结束，请先停止后再发送");
        }
        catch (Exception ex)
        {
            return WebApi.FailJson(503, WebApi.SafeErrorMessage(ex));
        }

        // 当前连接即第一个订阅者：从零开始收，不需要快照帧
        var response = WebApi.Response;
        // 连接通道：断连后 SAEA 的写出是静默丢弃，必须靠它判定对端是否还在
        var connection = WebApi.Connection;
        AgentRunAttachResult? attach = null;
        try
        {
            // 长连接不再参与 SAEA 的会话空闲回收：心跳是单向下发，不会被算作会话活跃
            WebApi.MarkLongLived();
            // 半开连接在应用层无法判死，另挂一条绝对寿命兜底（到点关闭，前端按序号自动续接）
            using var lifetime = SsePump.GuardLifetime(connection);
            SsePump.Begin(response);
            attach = run.Subscribe(0, snapshotOnGap: false);
            if (attach is null)
            {
                // 轮次在「起轮次」与「订阅」之间就已收尾，帧随通道关闭一起消失：
                // 给出明确信号，否则界面会静默停在半截
                SsePump.TryWrite(response, connection, AgentSseFrame.ForError("本轮已结束，请重新加载该会话查看结果"));
                SsePump.TryWrite(response, connection, AgentSseFrame.ForDone(resolution.SessionId));
                return new AgentEventStreamResult();
            }

            foreach (var pending in attach.Pending)
            {
                if (!SsePump.TryWrite(response, connection, pending))
                {
                    return new AgentEventStreamResult();
                }
            }

            await SsePump.RunAsync(
                response,
                connection,
                attach.Subscription.Reader,
                TimeSpan.FromSeconds(Math.Max(1, WebApi.Runtime.Settings.HeartbeatSeconds)),
                WebApi.Runtime.ShutdownToken).ConfigureAwait(false);
        }
        finally
        {
            // 只摘订阅者：断连不再取消轮次
            attach?.Subscription.Dispose();
            SsePump.Complete(response);
        }

        return new AgentEventStreamResult();
    });

    /// <summary>探活：该会话是否有正在进行的回合。</summary>
    /// <param name="payload">包装后的 <see cref="AgentActiveRequest"/> JSON。</param>
    /// <returns><c>{ active, runId, startedAt }</c>。</returns>
    public static Task<ActionResult> ActiveAsync(string payload) => WebApi.RunGuardedAsync(() =>
    {
        if (!WebApi.TryReadPayload<AgentActiveRequest>(payload, out var input) || input is null
            || string.IsNullOrWhiteSpace(input.SessionId))
        {
            return Task.FromResult(WebApi.FailJson(400, "sessionId 不能为空"));
        }

        var run = WebApi.Runtime.Runs.Find(WebApi.Runtime.UserId, input.SessionId.Trim());
        return Task.FromResult(WebApi.OkJson(new { active = run is not null, runId = run?.RunId, startedAt = run?.StartedAt }));
    });

    /// <summary>接上该会话正在进行的回合（SSE）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentAttachRequest"/> JSON。</param>
    /// <returns>SSE 标记结果；无活跃回合返回 204 空体。</returns>
    /// <remarks>
    /// 首帧可能是 <c>run.snapshot</c>（前端内存为空时）或直接补发缺失帧，
    /// 随后与首连接一样按订阅者泵逐帧写出。
    /// </remarks>
    public static Task<ActionResult> AttachAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentAttachRequest>(payload, out var input) || input is null
            || string.IsNullOrWhiteSpace(input.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        var run = WebApi.Runtime.Runs.Find(WebApi.Runtime.UserId, input.SessionId.Trim());
        if (run is null)
        {
            return NoContent();
        }

        if (!string.IsNullOrWhiteSpace(input.RunId)
            && !string.Equals(input.RunId.Trim(), run.RunId, StringComparison.Ordinal))
        {
            return NoContent();
        }

        // 先订阅：一旦写出响应头就改不了状态码，而「轮次已收尾」只可能在这里判出来
        // fromSeq 优先取请求体；旧客户端/浏览器自带重试只带 Last-Event-ID 头，也认
        var fromSeq = input.FromSeq ?? ReadLastEventId();
        var attach = run.Subscribe(fromSeq);
        if (attach is null)
        {
            return NoContent();
        }

        var response = WebApi.Response;
        // 连接通道：断连后 SAEA 的写出是静默丢弃，必须靠它判定对端是否还在
        var connection = WebApi.Connection;
        try
        {
            // 长连接不再参与 SAEA 的会话空闲回收：心跳是单向下发，不会被算作会话活跃
            WebApi.MarkLongLived();
            // 半开连接在应用层无法判死，另挂一条绝对寿命兜底（到点关闭，前端按序号自动续接）
            using var lifetime = SsePump.GuardLifetime(connection);
            SsePump.Begin(response);
            foreach (var pending in attach.Pending)
            {
                if (!SsePump.TryWrite(response, connection, pending))
                {
                    return new AgentEventStreamResult();
                }
            }

            await SsePump.RunAsync(
                response,
                connection,
                attach.Subscription.Reader,
                TimeSpan.FromSeconds(Math.Max(1, WebApi.Runtime.Settings.HeartbeatSeconds)),
                WebApi.Runtime.ShutdownToken).ConfigureAwait(false);
        }
        finally
        {
            attach.Subscription.Dispose();
            SsePump.Complete(response);
        }

        return new AgentEventStreamResult();
    });

    /// <summary>空体响应（204）。</summary>
    /// <returns>空体结果。</returns>
    private static ActionResult NoContent() => new EmptyResult { Status = HttpStatusCode.NoContent };

    /// <summary>读取 SSE 标准的 <c>Last-Event-ID</c> 头；缺失或非数字返回 <c>null</c>。</summary>
    /// <returns>已收到的最大帧序号。</returns>
    private static int? ReadLastEventId()
        => WebApi.Request.Headers.TryGetValue("Last-Event-ID", out var raw)
           && int.TryParse(raw?.Trim(), out var seq)
            ? seq
            : null;

    /// <summary>回传工具确认结果。</summary>
    /// <param name="payload">包装后的 <see cref="AgentConfirmRequest"/> JSON。</param>
    /// <returns>是否写入成功。</returns>
    public static Task<ActionResult> ConfirmAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentConfirmRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.SessionId) || string.IsNullOrWhiteSpace(input.ConfirmationId))
        {
            return WebApi.FailJson(400, "sessionId / confirmationId 不能为空");
        }

        if (!TryParseConfirmResult(input.Result, out var result))
        {
            return WebApi.FailJson(400, "result 必须是 allow / deny / allowAll / allowSession");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (scope.GetService(typeof(IToolConfirmationBroker)) is not IToolConfirmationBroker broker)
        {
            return WebApi.FailJson(503, "工具确认未启用");
        }

        var resolved = broker.TryResolve(input.SessionId.Trim(), input.ConfirmationId.Trim(), result);
        return resolved
            ? WebApi.OkJson(new { resolved = true })
            : WebApi.FailJson(404, "确认请求不存在、已过期或会话不匹配");
    });

    /// <summary>回传用户对提问的回答（或取消）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentAnswerRequest"/> JSON。</param>
    /// <returns>是否写入成功。</returns>
    public static Task<ActionResult> AnswerAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentAnswerRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.SessionId) || string.IsNullOrWhiteSpace(input.QuestionId))
        {
            return WebApi.FailJson(400, "sessionId / questionId 不能为空");
        }

        if (!input.Cancelled && input.Answers.Count == 0)
        {
            return WebApi.FailJson(400, "answers 不能为空；取消作答请传 cancelled=true");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (scope.GetService(typeof(IUserQuestionBroker)) is not IUserQuestionBroker broker)
        {
            return WebApi.FailJson(503, "用户提问未启用");
        }

        var sessionId = input.SessionId.Trim();
        var questionId = input.QuestionId.Trim();

        var resolved = input.Cancelled
            ? broker.TryCancel(sessionId, questionId)
            : broker.TryResolve(
                sessionId,
                questionId,
                input.Answers
                    .Select(a => new UserQuestionAnswer(
                        a.Question?.Trim() ?? "",
                        (a.Labels ?? Array.Empty<string>())
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => l.Trim())
                            .ToList(),
                        string.IsNullOrWhiteSpace(a.FreeText) ? null : a.FreeText.Trim()))
                    .ToList());

        if (resolved)
        {
            // 提问已收尾：立即从轮次快照里摘掉。子代理提问没有父级工具卡，
            // 只靠 tool.completed 永远清不到，同一轮内刷新仍会弹回
            WebApi.Runtime.Runs.Find(WebApi.Runtime.UserId, sessionId)?.ClearPendingQuestion(questionId);
        }

        return resolved
            ? WebApi.OkJson(new { resolved = true })
            : WebApi.FailJson(404, "提问不存在、已过期或答案与会话/问题不匹配");
    });

    /// <summary>取消指定会话正在进行的回合。</summary>
    /// <param name="payload">包装后的 <see cref="AgentCancelRequest"/> JSON。</param>
    /// <returns>是否确有回合被取消。</returns>
    public static Task<ActionResult> CancelAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentCancelRequest>(payload, out var input) || input is null
            || string.IsNullOrWhiteSpace(input.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        var cancelled = WebApi.Runtime.CancelRun(
            WebApi.Runtime.UserId,
            input.SessionId.Trim(),
            string.IsNullOrWhiteSpace(input.RunId) ? null : input.RunId.Trim());
        return WebApi.OkJson(new { cancelled });
    });

    /// <summary>丢弃该会话服务端缓存的 Agent（配置变更后按最新配置重建）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentResetRequest"/> JSON；<c>payload</c> 可为空。</param>
    /// <returns>固定成功。</returns>
    public static Task<ActionResult> ResetAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        var input = WebApi.TryReadPayload<AgentResetRequest>(payload, out var parsed) ? parsed : null;

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (scope.GetService(typeof(IAgentHost)) is not IAgentHost host)
        {
            return WebApi.FailJson(503, AgentDisabled);
        }

        host.Reset(string.IsNullOrWhiteSpace(input?.SessionId) ? null : input!.SessionId.Trim());
        return WebApi.OkJson(new { reset = true });
    });

    /// <summary>把宿主事件与传输层补写事件合成 SSE 帧序列；<c>null</c> 表示心跳。</summary>
    private static async IAsyncEnumerable<AgentSseFrame?> StreamFramesAsync(
        IAgentHost host,
        AgentRunRequest request,
        SessionResolution resolution,
        ConcurrentQueue<AgentSseFrame> sideFrames,
        string runId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return AgentSseFrame.ForSession(resolution.SessionId, resolution.Created, runId);

        var heartbeat = TimeSpan.FromSeconds(Math.Max(1, WebApi.Runtime.Settings.HeartbeatSeconds));
        using var timer = new PeriodicTimer(heartbeat);
        var tick = timer.WaitForNextTickAsync(ct).AsTask();

        var enumerator = host.RunStreamingAsync(request, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    yield return AgentSseFrame.ForError(CancelledMessage);
                    break;
                }

                var moveNext = enumerator.MoveNextAsync().AsTask();
                while (true)
                {
                    var winner = await Task.WhenAny(moveNext, tick).ConfigureAwait(false);
                    if (ReferenceEquals(winner, moveNext))
                    {
                        break;
                    }

                    // 取消后 WaitForNextTickAsync 立即返回已取消任务：必须跳出，否则会不断写心跳
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    // 先把旁路帧（计划项/拒绝/编排进度）送出，再发心跳，避免长思考阶段被压住
                    while (sideFrames.TryDequeue(out var pending))
                    {
                        yield return pending;
                    }

                    yield return null;
                    tick = timer.WaitForNextTickAsync(ct).AsTask();
                }

                if (ct.IsCancellationRequested)
                {
                    // 底层流未响应取消时不再等它：直接收尾，finally 负责释放枚举器
                    yield return AgentSseFrame.ForError(CancelledMessage);
                    break;
                }

                var hasNext = false;
                string? failure = null;
                var cancelled = false;
                try
                {
                    hasNext = await moveNext.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    failure = WebApi.SafeErrorMessage(ex);
                }

                while (sideFrames.TryDequeue(out var extra))
                {
                    yield return extra;
                }

                if (cancelled)
                {
                    yield return AgentSseFrame.ForError(CancelledMessage);
                    break;
                }

                if (failure is not null)
                {
                    yield return AgentSseFrame.ForError(failure);
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                // 压缩事件是产品侧类型，框架 AgentEventProtocol.ToFrame 不认识（返回 null），
                // 必须在这里显式转帧，否则帧被静默丢弃、三端都看不到压缩标记。
                if (enumerator.Current is ContextCompactedEvent compacted)
                {
                    yield return AgentSseFrame.Create(AgentSseEventNames.ContextCompacted, new
                    {
                        phase = compacted.Phase,
                        trigger = compacted.Trigger,
                        archivedCount = compacted.ArchivedCount,
                        keptCount = compacted.KeptCount,
                        estimatedTokens = compacted.EstimatedTokens,
                        budget = compacted.Budget,
                        message = compacted.Describe()
                    });
                    continue;
                }

                if (AgentEventProtocol.ToFrame(enumerator.Current) is { } frame)
                {
                    yield return AgentSseFrame.Create(frame.Name, frame.Payload);
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        while (sideFrames.TryDequeue(out var tail))
        {
            yield return tail;
        }

        yield return AgentSseFrame.ForDone(resolution.SessionId);
    }

    /// <summary>
    /// 把输入框内容改写成需求表述：用会话当前选中的模型做一次非流式、无工具的改写。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="AgentOptimizeRequest"/> JSON。</param>
    /// <returns>改写结果。</returns>
    /// <remarks>
    /// 不读会话历史与工作区，也不装配工具：改写只需要当前这段文本，
    /// 带上上下文反而会把无关信息写进结果。
    /// </remarks>
    public static Task<ActionResult> OptimizeAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentOptimizeRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        var prompt = input.Prompt?.Trim() ?? "";
        if (prompt.Length == 0)
        {
            return WebApi.FailJson(400, "待改写的内容不能为空");
        }

        if (prompt.Length > MaxOptimizeChars)
        {
            return WebApi.FailJson(400, $"待改写的内容不能超过 {MaxOptimizeChars} 字");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (scope.GetService(typeof(IAgentModelSelection)) is not IAgentModelSelection selection
            || scope.GetService(typeof(IProviderRouter)) is not IProviderRouter router)
        {
            return WebApi.FailJson(503, AgentDisabled);
        }

        var model = await selection
            .ResolveQualifiedModelNameAsync(input.SessionId, WebApi.Runtime.ShutdownToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(model))
        {
            return WebApi.FailJson(503, "尚未选择模型：请先在设置里选定模型再使用改写");
        }

        IChatClient client;
        try
        {
            client = router.CreateChatClient(model);
        }
        catch (InvalidOperationException)
        {
            // 供应商被删/改名后路由会抛 InvalidOperationException，属于配置问题而非服务故障
            return WebApi.FailJson(503, "当前模型不可用：请检查供应商配置后重试");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(WebApi.Runtime.ShutdownToken);
        timeout.CancelAfter(OptimizeTimeout);

        ChatResponse? response;
        try
        {
            response = await client
                .GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, OptimizeInstruction),
                        new ChatMessage(ChatRole.User, prompt),
                    ],
                    new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } },
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!WebApi.Runtime.ShutdownToken.IsCancellationRequested)
        {
            return WebApi.FailJson(504, "改写超时，请稍后重试");
        }
        catch (Exception ex)
        {
            AgentWebRuntime.Log($"提示词改写失败：{ex}");
            return WebApi.FailJson(502, "改写失败，请稍后重试");
        }

        var text = response?.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            return WebApi.FailJson(502, "模型没有返回可用的改写结果");
        }

        return WebApi.OkJson(new AgentOptimizeResponse(text));
    });

    /// <summary>
    /// 把一段对话内容概括成不超过 15 字的会话主题。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="AgentAutoSessionNameRequest"/> JSON。</param>
    /// <returns>主题（标题）。</returns>
    /// <remarks>
    /// 与 <see cref="OptimizeAsync"/> 同口径：一次性非流式调用，不读会话历史、不装配工具、关闭思考。
    /// 自动命名本身在回合入口后台触发，本接口供远程模式与调试复用。
    /// </remarks>
    public static Task<ActionResult> AutoSessionNameAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentAutoSessionNameRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.Text))
        {
            return WebApi.FailJson(400, "待概括的内容不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (scope.GetService(typeof(ISessionAutoNamer)) is not ISessionAutoNamer namer)
        {
            return WebApi.FailJson(503, AgentDisabled);
        }

        var title = await namer
            .AutoSessionNameAsync(input.SessionId, input.Text, WebApi.Runtime.ShutdownToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(title)
            ? WebApi.FailJson(502, "主题生成失败，请稍后重试")
            : WebApi.OkJson(new AgentAutoSessionNameResponse(title));
    });

    /// <summary>
    /// 会话当前生效的生成参数（思考档位 / 上下文预算档位）。
    /// </summary>
    /// <param name="sessionId">会话标识；缺省返回全局默认与全局模型的档位。</param>
    /// <returns>生效值、可选档位与模型窗口。</returns>
    public static Task<ActionResult> SessionSettingsAsync(string? sessionId) => WebApi.RunGuardedAsync(async () =>
    {
        var id = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (id is not null && await WebSessionService.CheckOwnerAsync(scope, id).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var modelKey = await ResolveModelKeyAsync(scope, id).ConfigureAwait(false);
        var profile = await LoadProfileAsync(scope, modelKey).ConfigureAwait(false);
        var settings = scope.GetRequiredService<ISessionGenerationSettings>();

        var state = await settings.ResolveAsync(id, profile, WebApi.Runtime.ShutdownToken).ConfigureAwait(false);

        return WebApi.OkJson(new SessionGenerationSettingsDto(
            state.ReasoningEffort,
            state.Budget?.Threshold,
            WebSystemService.BuildReasoningEfforts(profile?.Reasoning),
            ContextTiers.FromWindow(profile?.ContextWindow),
            profile?.ContextWindow,
            state.Budget?.Selected));
    });

    /// <summary>
    /// 写入生成参数：给定会话写会话覆盖，缺省会话写全局默认。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="SessionGenerationSettingsRequest"/> JSON。</param>
    /// <returns>保存结果。</returns>
    /// <remarks>两个字段按整体替换处理：缺省即清除该覆盖，前端每次提交都应带上当前全部选择。</remarks>
    public static Task<ActionResult> SaveSessionSettingsAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SessionGenerationSettingsRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        ReasoningEffort? effort = null;
        if (!string.IsNullOrWhiteSpace(input.ReasoningEffort))
        {
            if (!Enum.TryParse<ReasoningEffort>(input.ReasoningEffort.Trim(), true, out var parsed)
                || parsed is not (ReasoningEffort.None or ReasoningEffort.Low or ReasoningEffort.Medium
                    or ReasoningEffort.High or ReasoningEffort.ExtraHigh))
            {
                return WebApi.FailJson(400, "不支持的思考档位");
            }

            effort = parsed;
        }

        if (input.ContextTier is { } tier && tier <= 0)
        {
            return WebApi.FailJson(400, "上下文档位必须为正整数");
        }

        var id = string.IsNullOrWhiteSpace(input.SessionId) ? null : input.SessionId.Trim();

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);

        if (id is null)
        {
            scope.GetRequiredService<ConfigManager>().SetDefaults(new GenerationDefaults
            {
                ReasoningEffort = effort?.ToString(),
                ContextTier = input.ContextTier
            });
            return WebApi.OkJson(new { saved = true });
        }

        if (await WebSessionService.CheckOwnerAsync(scope, id).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var updated = await scope.GetRequiredService<SessionRepository>()
            .UpdateGenerationSettingsAsync(id, WebApi.Runtime.UserId, effort?.ToString(), input.ContextTier)
            .ConfigureAwait(false);
        if (!updated)
        {
            return WebApi.FailJson(404, WebSessionResolver.SessionNotFound);
        }

        return WebApi.OkJson(new { saved = true });
    });

    /// <summary>解析会话实际使用的模型键（<c>provider:model</c>）；未选模型时为空串。</summary>
    private static async ValueTask<string> ResolveModelKeyAsync(DiContainer scope, string? sessionId)
        => (await scope.GetRequiredService<IAgentModelSelection>()
            .ResolveQualifiedModelNameAsync(sessionId, WebApi.Runtime.ShutdownToken)
            .ConfigureAwait(false) ?? string.Empty).Trim();

    /// <summary>按 <c>provider:model</c> 取模型能力档案；键不完整时返回 <c>null</c>。</summary>
    private static async ValueTask<ModelProfile?> LoadProfileAsync(DiContainer scope, string modelKey)
    {
        var separator = modelKey.IndexOf(':');
        if (separator <= 0 || separator >= modelKey.Length - 1) return null;

        return await scope.GetRequiredService<IProviderModelManagementService>()
            .GetModelProfileAsync(modelKey[..separator], modelKey[(separator + 1)..])
            .ConfigureAwait(false);
    }

    private static List<string> NormalizeAttachments(IReadOnlyList<string>? sources)
        => (sources ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToList();

    private static ReasoningEffort? ParseEffort(string? text)
        => !string.IsNullOrWhiteSpace(text) && Enum.TryParse<ReasoningEffort>(text.Trim(), true, out var effort)
            ? effort
            : null;

    /// <summary>
    /// 解析权限模式（default / plan / accept-edits / bypass）；无法识别或为空时回落到 Default。
    /// </summary>
    private static ToolPermissionMode ParsePermissionMode(string? text)
        => !string.IsNullOrWhiteSpace(text) && PermissionModeRules.Parse(text) is { } mode
            ? mode
            : ToolPermissionMode.Default;

    private static bool TryParseConfirmResult(string? text, out ConfirmResult result)
    {
        result = ConfirmResult.Deny;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "allow" => Set(ConfirmResult.Allow, out result),
            "deny" => Set(ConfirmResult.Deny, out result),
            "allowall" => Set(ConfirmResult.AllowAll, out result),
            "allowsession" => Set(ConfirmResult.AllowSession, out result),
            _ => false
        };

        static bool Set(ConfirmResult value, out ConfirmResult target)
        {
            target = value;
            return true;
        }
    }
}