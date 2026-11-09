/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： AgentController
*描述：对话接口：非流式 / SSE 流式 / 确认 / 取消 / 重建
*
*****************************************************************************/

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Hosting;
using LuBan.AIAgent.Protocol;

using LubanAgentCore.Protocol.Dtos;
using LubanAgentCore.Protocol.Events;

using LubanAgentWebHost.Hosting;

using Microsoft.Extensions.AI;

using SAEA.MVC;

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 对话接口。
/// </summary>
public sealed class AgentController : Controller
{
    private const string AgentDisabled =
        "LuBan Agent 未启用：请检查 appsettings 的 LuBanAgent 节与数据库配置";

    private const string CancelledMessage = "本轮对话已取消";

    /// <summary>非流式对话（不装配工具，返回正文 + 思考 + 附件摘要）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentChatRequest"/> JSON。</param>
    /// <returns>对话结果。</returns>
    [HttpPost]
    public Task<ActionResult> Send(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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
    [HttpPost]
    public Task<ActionResult> Chat(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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

        var sideFrames = new ConcurrentQueue<AgentSseFrame>();

        // 编排进度：子代理在工具调用内部执行，进度不进对话流；
        // 订阅本作用域内的出口，逐条转成 orchestration.progress 帧
        var progressSink = scope.GetService<HostOrchestrationProgressSink>();
        void OnProgress(OrchestrationProgressEvent progress) => sideFrames.Enqueue(
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

        var (runCts, runId) = WebApi.Runtime.BeginRun(WebApi.Runtime.UserId, resolution.SessionId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token, WebApi.Runtime.ShutdownToken);
        var response = WebApi.Response;

        try
        {
            // 写响应头也可能失败（客户端已断）：放进 try 才能保证登记被收尾
            SsePump.Begin(response);

            await foreach (var frame in StreamFramesAsync(host, request, resolution, sideFrames, runId, linked.Token)
                .ConfigureAwait(false))
            {
                var writable = frame is null
                    ? SsePump.TryWriteHeartbeat(response)
                    : SsePump.TryWrite(response, frame);

                if (!writable)
                {
                    break;
                }
            }
        }
        finally
        {
            if (progressSink is not null)
            {
                progressSink.Progress -= OnProgress;
            }

            SsePump.Complete(response);

            // 写出失败（客户端断连）或调用方提前停止枚举时，回合仍可能挂在 LLM/工具调用上。
            // 这里取消链接令牌让底层尽快收尾；正常结束时取消是空操作。
            CancelQuietly(linked);
            WebApi.Runtime.EndRun(WebApi.Runtime.UserId, resolution.SessionId, runCts);
        }

        return new AgentEventStreamResult();
    });

    /// <summary>回传工具确认结果。</summary>
    /// <param name="payload">包装后的 <see cref="AgentConfirmRequest"/> JSON。</param>
    /// <returns>是否写入成功。</returns>
    [HttpPost]
    public Task<ActionResult> Confirm(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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
    [HttpPost]
    public Task<ActionResult> Cancel(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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
    [HttpPost]
    public Task<ActionResult> Reset(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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

    /// <summary>
    /// 取消链接令牌并吞掉「已释放」异常。
    /// </summary>
    /// <param name="cts">待取消的取消源。</param>
    private static void CancelQuietly(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已被其他路径收尾释放，忽略
        }
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