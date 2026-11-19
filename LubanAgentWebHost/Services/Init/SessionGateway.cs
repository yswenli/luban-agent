/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： SessionGateway
*描述：云效 S2 项目初始化会话通路收口实现（设计 §4.3）——
*      in-process 解析 IAgentHost / IToolConfirmationBroker / ISessionManager，
*      SendAsync 走 Chat/SSE 通道（EnableTools=true）消费事件流到终态，
*      tool.confirm 帧按预批清单逐条放行/拒绝；SSE 帧解析抽 internal static 纯函数（可测）。
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.3
*
*****************************************************************************/

using System.Text.Json;

namespace LubanAgentWebHost.Services.Init;

/// <summary>
/// 会话通路收口实现（in-process，不走 HTTP 自环）。
/// </summary>
/// <remarks>
/// <para>
/// 取 DI 口径与 <c>WebAgentService</c> 壳同款：<c>WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId)</c>
/// 解析底层服务。任务在后台线程执行，不受 HTTP 超时约束。
/// </para>
/// <para>
/// R-S2-1 最小回路实测（2026-11-18，宿主 18081 真机）钉住的帧形态（与 <see cref="SessionFrameParser"/> 注释一致）：
/// SSE 壳为 <c>event: {name}\ndata: {json}\n\n</c>；<c>session</c> 首帧
/// <c>{sessionId, created, runId}</c>；<c>text.delta</c>/<c>think.delta</c> 为 <c>{delta, llmRound}</c>；
/// <c>tool.confirm</c> 为 <c>{confirmationId, toolName, arguments:{command,…}, timeoutSeconds}</c>
/// （注意 <c>tool.started.name</c> 是 <c>RunShell</c>，confirm 的 <c>toolName</c> 是 <c>RunShellAsync</c>）；
/// 拒绝后回 <c>tool.denied {toolName, arguments}</c> + <c>tool.failed {callId, error:"工具调用被拒绝或已取消"}</c>；
/// 终态为 <c>done {sessionId}</c> 或 <c>error {message}</c>。
/// 确认回传：POST /agent/confirm（<c>AgentConfirmRequest{SessionId,ConfirmationId,Result}</c>）→
/// <c>{resolved:true}</c>，in-process 等价物即 <see cref="ConfirmAsync"/>（IToolConfirmationBroker.TryResolve）。
/// </para>
/// </remarks>
public sealed class SessionGateway : ISessionGateway
{
    /// <summary>初始化会话默认标题。</summary>
    private const string InitSessionTitle = "云效项目初始化";

    /// <summary>各会话的最终回执（SendAsync 消费到终态后写入；进程重启后由消息兜底重建）。</summary>
    private readonly ConcurrentDictionary<string, SessionReceipt> _receipts = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<string> CreateInitSessionAsync(string workspaceId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var session = await scope.GetRequiredService<ISessionManager>()
            .CreateSessionAsync(InitSessionTitle, workspaceId.Trim())
            .ConfigureAwait(false);
        return session.SessionId;
    }

    /// <inheritdoc />
    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
            return scope.GetService(typeof(IAgentHost)) is IAgentHost;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AgentWebRuntime.Log($"会话通路探测失败：{ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 内部走 Chat 同款的「装配工具的流式轮次」：<c>IAgentHost.RunStreamingAsync</c>
    /// （<c>EnableTools=true</c>），逐事件经 <c>AgentEventProtocol.ToFrame</c> 转成 SSE 帧后消费到终态。
    /// 非流式 Send 不装配工具，git 执行必须走本通道（设计 §4.3 已核实）。
    /// </remarks>
    public async Task SendAsync(
        string sessionId,
        string instruction,
        IReadOnlyList<string>? approvedCommands,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (scope.GetService(typeof(IAgentHost)) is not IAgentHost host)
        {
            throw new InvalidOperationException("LuBan Agent 未启用：工作区会话通路不可用");
        }

        var frames = new List<AgentSseFrame> { AgentSseFrame.ForSession(sessionId.Trim(), false) };
        var request = new AgentRunRequest
        {
            Input = instruction,
            SessionId = sessionId.Trim(),
            EnableTools = true,
            PermissionMode = ToolPermissionMode.Default
        };

        try
        {
            await foreach (var evt in host.RunStreamingAsync(request, ct).ConfigureAwait(false))
            {
                // 与 Chat 通道同款转换：框架事件 → 线上 SSE 帧（AgentEventFrame → AgentSseFrame）
                if (AgentEventProtocol.ToFrame(evt) is not { } eventFrame)
                {
                    continue;
                }

                var frame = AgentSseFrame.Create(eventFrame.Name, eventFrame.Payload);
                frames.Add(frame);

                if (string.Equals(frame.Name, AgentSseEventNames.ToolConfirm, StringComparison.Ordinal)
                    && SessionFrameParser.TryReadToolConfirm(frame, out var call))
                {
                    var decision = SessionFrameParser.DecideConfirmResult(approvedCommands, call);
                    await ConfirmAsync(sessionId, call.ConfirmationId, decision, ct).ConfigureAwait(false);
                }
            }

            frames.Add(AgentSseFrame.ForDone(sessionId));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            frames.Add(AgentSseFrame.ForError("本轮已取消"));
            _receipts[sessionId] = SessionFrameParser.Fold(frames);
            throw;
        }
        catch (Exception ex)
        {
            // 不向调用方抛出：失败细节进回执（脱敏），由调用方按回执失败该库
            frames.Add(AgentSseFrame.ForError(WebApi.SafeErrorMessage(ex)));
        }

        _receipts[sessionId] = SessionFrameParser.Fold(frames);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 与界面 <c>/agent/confirm</c> 同一机制：in-process 解析 <c>IToolConfirmationBroker</c> 并
    /// <c>TryResolve</c>（保留人工确认通道语义，不用 PermissionMode=bypass 绕过确认）。
    /// </remarks>
    public async Task ConfirmAsync(string sessionId, string confirmationId, string result, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationId);

        if (!SessionFrameParser.TryParseConfirmResult(result, out var parsed))
        {
            throw new ArgumentException("result 必须是 allow / deny / allowAll / allowSession", nameof(result));
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (scope.GetService(typeof(IToolConfirmationBroker)) is not IToolConfirmationBroker broker)
        {
            throw new InvalidOperationException("工具确认未启用");
        }

        broker.TryResolve(sessionId.Trim(), confirmationId.Trim(), parsed);
    }

    /// <inheritdoc />
    public async Task<SessionReceipt> WaitReceiptAsync(string sessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_receipts.TryGetValue(sessionId.Trim(), out var receipt))
        {
            return receipt;
        }

        // 兜底：SSE 帧缺失（进程重启 / 断流）时回读会话消息，取最后一条助手消息当回执摘要
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var messages = await scope.GetRequiredService<ISessionManager>()
            .GetLatestMessagesAsync(sessionId.Trim(), 20)
            .ConfigureAwait(false);

        var lastAssistant = messages?
            .LastOrDefault(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase));

        return lastAssistant is null
            ? new SessionReceipt(false, null, "尚无回执（事件流未收尾且会话内无助手消息）", sessionId.Trim())
            : new SessionReceipt(true, lastAssistant.Content, null, sessionId.Trim());
    }
}

/// <summary>
/// SSE 帧解析与预批策略纯函数（internal static，供单测以 mock 帧序列直测）。
/// </summary>
/// <remarks>
/// 帧名/字段名全部钉自 R-S2-1 最小回路实测（2026-11-18），见 <see cref="SessionGateway"/> 类注释；
/// 单测只打这些纯函数，不打真实宿主。
/// </remarks>
internal static class SessionFrameParser
{
    /// <summary>确认结果线上值：放行单次调用。</summary>
    internal const string ConfirmAllow = "allow";

    /// <summary>确认结果线上值：拒绝。</summary>
    internal const string ConfirmDeny = "deny";

    /// <summary>
    /// 解析 <c>tool.confirm</c> 帧：<c>{confirmationId, toolName, arguments:{command,…}, timeoutSeconds}</c>。
    /// </summary>
    /// <param name="frame">SSE 帧。</param>
    /// <param name="call">解析结果。</param>
    /// <returns>是否为可解析的 tool.confirm 帧。</returns>
    internal static bool TryReadToolConfirm(AgentSseFrame frame, out ToolConfirmCall call)
    {
        call = null!;
        if (frame is null
            || !string.Equals(frame.Name, AgentSseEventNames.ToolConfirm, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(frame.Data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var confirmationId = ReadString(root, "confirmationId");
            if (string.IsNullOrWhiteSpace(confirmationId))
            {
                return false;
            }

            string? command = null;
            if (root.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
            {
                command = ReadString(arguments, "command");
            }

            call = new ToolConfirmCall(confirmationId!, ReadString(root, "toolName") ?? "", command);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 预批策略：命令文本命中预批清单 → <c>allow</c>，否则 <c>deny</c>。
    /// </summary>
    /// <remarks>
    /// 命令精确匹配（去首尾空白、序数比较）——不做子串包含，防「预批命令是恶意命令的前缀」被放行。
    /// 命中用 <c>allow</c> 而<b>不是</b> <c>allowAll</c>：框架 allowAll = 本轮同类工具免确认，
    /// 会把后续偏离清单的调用一并放过（执行期 confirm 绕过预批清单是 Critical 红线），故逐帧逐条判定。
    /// </remarks>
    /// <param name="approvedCommands">预批命令清单（GitStep.Command）；null/空 = 一律 deny。</param>
    /// <param name="call">tool.confirm 帧解析结果。</param>
    /// <returns>线上确认值（allow / deny）。</returns>
    internal static string DecideConfirmResult(IReadOnlyList<string>? approvedCommands, ToolConfirmCall call)
    {
        if (call is null
            || string.IsNullOrWhiteSpace(call.Command)
            || approvedCommands is null
            || approvedCommands.Count == 0)
        {
            return ConfirmDeny;
        }

        var command = call.Command.Trim();
        foreach (var approved in approvedCommands)
        {
            if (!string.IsNullOrWhiteSpace(approved)
                && string.Equals(approved.Trim(), command, StringComparison.Ordinal))
            {
                return ConfirmAllow;
            }
        }

        return ConfirmDeny;
    }

    /// <summary>
    /// 解析确认结果线上值（allow / deny / allowAll / allowSession）。
    /// </summary>
    /// <param name="text">线上值。</param>
    /// <param name="result">解析出的确认结果。</param>
    /// <returns>是否合法。</returns>
    internal static bool TryParseConfirmResult(string? text, out ConfirmResult result)
    {
        result = ConfirmResult.Deny;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "allow":
                result = ConfirmResult.Allow;
                return true;
            case "deny":
                result = ConfirmResult.Deny;
                return true;
            case "allowall":
                result = ConfirmResult.AllowAll;
                return true;
            case "allowsession":
                result = ConfirmResult.AllowSession;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 把一轮 SSE 帧序列折叠成最终回执。
    /// </summary>
    /// <remarks>
    /// 判定口径：有 <c>done</c> 且无 <c>error</c> 帧、无 <c>tool.failed</c>、无 <c>tool.denied</c> 才算
    /// <see cref="SessionReceipt.Ok"/>（预批外调用被拒 → 失败该库语义）；正文 = <c>text.delta</c> 拼接。
    /// </remarks>
    /// <param name="frames">帧序列（时序）。</param>
    /// <returns>回执。</returns>
    internal static SessionReceipt Fold(IReadOnlyList<AgentSseFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        string? sessionId = null;
        string? runId = null;
        string? error = null;
        var denied = false;
        var done = false;
        var summary = new StringBuilder();
        var toolErrors = new List<string>();

        foreach (var frame in frames)
        {
            switch (frame.Name)
            {
                case AgentSseEventNames.Session:
                    sessionId ??= ReadPayloadString(frame, "sessionId");
                    runId ??= ReadPayloadString(frame, "runId");
                    break;

                case AgentSseEventNames.Done:
                    done = true;
                    sessionId ??= ReadPayloadString(frame, "sessionId");
                    break;

                case AgentSseEventNames.TextDelta:
                    summary.Append(ReadPayloadString(frame, "delta"));
                    break;

                case AgentSseEventNames.Error:
                    error ??= ReadPayloadString(frame, "message");
                    break;

                case AgentSseEventNames.ToolFailed:
                    if (ReadPayloadString(frame, "error") is { Length: > 0 } toolError)
                    {
                        toolErrors.Add(toolError);
                    }

                    break;

                case AgentSseEventNames.ToolDenied:
                    denied = true;
                    break;
            }
        }

        if (!done && error is null)
        {
            error = "事件流未正常收尾（缺少 done 终态帧）";
        }

        if (denied)
        {
            toolErrors.Add("存在被预批策略拒绝的工具调用");
        }

        var ok = done && error is null && toolErrors.Count == 0;
        var mergedError = error is not null && toolErrors.Count > 0
            ? error + "；" + string.Join("；", toolErrors)
            : error ?? (toolErrors.Count > 0 ? string.Join("；", toolErrors) : null);

        var text = summary.ToString().Trim();
        return new SessionReceipt(ok, text.Length > 0 ? text : null, mergedError, sessionId, runId, denied);
    }

    /// <summary>读帧载荷里的字符串字段（缺失/非法返回 null）。</summary>
    private static string? ReadPayloadString(AgentSseFrame frame, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(frame.Data);
            return ReadString(document.RootElement, property);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>读 JSON 对象的字符串字段（缺失/非字符串返回 null）。</summary>
    private static string? ReadString(JsonElement root, string property)
        => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}
