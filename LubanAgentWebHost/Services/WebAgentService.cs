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
            .ResolveAsync(scope, WebApi.Runtime.UserId, input.SessionId)
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
                .ResolveAsync(probe, userId, input.SessionId)
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
            run = await WebApi.Runtime.Runs.StartAsync(userId, resolution.SessionId, async (handle, ct) =>
            {
                var sideFrames = new ConcurrentQueue<AgentSseFrame>();

                // 编排进度：子代理在工具调用内部执行，进度不进对话流；
                // 订阅本轮作用域内的出口，逐条转成 orchestration.progress 帧
                var progressSink = handle.Host.Services.GetService<HostOrchestrationProgressSink>();
                void OnProgress(OrchestrationProgressEvent progress) => handle.Publish(
                    AgentSseFrame.Create(AgentSseEventNames.OrchestrationProgress, OrchestrationProgressPayload.From(progress)));
                if (progressSink is not null)
                {
                    progressSink.Progress += OnProgress;
                }

                var request = new AgentRunRequest
                {
                    Input = prompt,
                    SessionId = resolution.SessionId,
                    SystemPrompt = string.IsNullOrWhiteSpace(input.System) ? null : input.System,
                    Attachments = NormalizeAttachments(input.Attachments),
                    EnableTools = input.EnableTools,
                    ReasoningEffort = ParseEffort(input.ReasoningEffort),
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
        AgentRunAttachResult? attach = null;
        try
        {
            SsePump.Begin(response);
            attach = run.Subscribe(0, snapshotOnGap: false);
            if (attach is null)
            {
                // 轮次在「起轮次」与「订阅」之间就已收尾，帧随通道关闭一起消失：
                // 给出明确信号，否则界面会静默停在半截
                SsePump.TryWrite(response, AgentSseFrame.ForError("本轮已结束，请重新加载该会话查看结果"));
                SsePump.TryWrite(response, AgentSseFrame.ForDone(resolution.SessionId));
                return new AgentEventStreamResult();
            }

            foreach (var pending in attach.Pending)
            {
                if (!SsePump.TryWrite(response, pending))
                {
                    return new AgentEventStreamResult();
                }
            }

            await SsePump.RunAsync(
                response,
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
        var attach = run.Subscribe(input.FromSeq);
        if (attach is null)
        {
            return NoContent();
        }

        var response = WebApi.Response;
        try
        {
            SsePump.Begin(response);
            foreach (var pending in attach.Pending)
            {
                if (!SsePump.TryWrite(response, pending))
                {
                    return new AgentEventStreamResult();
                }
            }

            await SsePump.RunAsync(
                response,
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