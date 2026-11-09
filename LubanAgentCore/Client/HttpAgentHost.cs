/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： HttpAgentHost
*描述：IAgentHost 的 HTTP/SSE 实现（把本地宿主调用转成一次 HTTP 回合）
*
*****************************************************************************/

using System.Runtime.CompilerServices;

namespace LubanAgentCore.Client;

/// <summary>
/// <see cref="IAgentHost"/> 的远程实现：一轮对话 = 一次 <c>/agent/chat</c> SSE 请求。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Services"/> 返回客户端本地容器：视图大量通过 <c>IAgentHost.Services</c> 解析服务，
/// 这些服务（配置、技能、MCP、插件等）仍是本地实例，只有会话/工作区/对话走服务端。
/// </para>
/// <para>
/// 工具确认：服务端经 <c>tool.confirm</c> 帧下发待确认项，本类调用
/// <see cref="AgentRunRequest.ConfirmHandlerAsync"/> / <see cref="AgentRunRequest.ConfirmHandler"/>
/// 取得用户选择后回传 <c>/agent/confirm</c>；两个回调都没设置时按「拒绝」回传，
/// 与框架「未设置确认回调的工具调用一律拒绝」的语义一致。
/// </para>
/// <para>
/// 取消：服务端无法探测连接断开，故流被取消或提前结束（未收到 <c>done</c>）时，
/// 本类在收尾处显式调用 <c>/agent/cancel</c>。
/// </para>
/// </remarks>
public sealed class HttpAgentHost(
    DiContainer services,
    AgentServerClient client,
    HttpSessionManager sessions) : IAgentHost
{
    private string? _initializedSessionId;

    /// <inheritdoc />
    public bool IsInitialized
        => _initializedSessionId is { Length: > 0 } id && string.Equals(id, CurrentSessionId, StringComparison.Ordinal);

    /// <inheritdoc />
    public DiContainer Services => services;

    /// <inheritdoc />
    public string? CurrentSessionId => sessions.CurrentSession?.SessionId;

    /// <inheritdoc />
    /// <remarks>
    /// 服务端按需构建会话 Agent，客户端只需把目标会话设为「当前会话」并校验其存在。
    /// 人设/工具覆盖随每轮 <c>/agent/chat</c> 入参下发，故此处不承载 <paramref name="overrides"/>。
    /// </remarks>
    public async Task InitializeAsync(
        string sessionId, CancellationToken ct = default, AgentBuildOverrides? overrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await client.PostAsync(
            AgentApiRoutes.Session.Switch,
            new SessionIdRequest { SessionId = sessionId },
            ct).ConfigureAwait(false);

        sessions.SetCurrentFromRemote(sessionId);
        _initializedSessionId = sessionId;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 服务端可能在回合运行中（延迟重载），故失败只记日志、不抛出——该接口本身无返回值，
    /// 抛异常会打断调用方的 UI 流程。
    /// </remarks>
    public void Reset(string? sessionId = null)
    {
        _ = ResetAsync(sessionId ?? CurrentSessionId);

        async Task ResetAsync(string? target)
        {
            try
            {
                await client.PostAsync(
                    AgentApiRoutes.Agent.Reset,
                    new AgentResetRequest { SessionId = target },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn($"重置远端 Agent 失败（sessionId={target}）：{ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new AgentChatRequest
        {
            Prompt = request.Input,
            System = request.SystemPrompt,
            // 未解析来源与已解析路径一并下发：服务端用同一套解析器再归一
            Attachments = request.Attachments.Concat(request.AttachmentPaths).ToList(),
            SessionId = request.SessionId ?? CurrentSessionId,
            EnableTools = request.EnableTools,
            ReasoningEffort = request.ReasoningEffort?.ToString(),
            PermissionMode = request.PermissionMode.ToString()
        };

        var sessionId = payload.SessionId;
        string? runId = null;
        var completed = false;
        try
        {
            await foreach (var frame in client
                .StreamAsync(AgentApiRoutes.Agent.Chat, payload, ct)
                .ConfigureAwait(false))
            {
                switch (frame.Name)
                {
                    case AgentSseEventNames.Session:
                        var session = frame.GetPayload<SessionFramePayload>();
                        if (session is not null)
                        {
                            runId = session.RunId;
                        }

                        if (!string.IsNullOrWhiteSpace(session?.SessionId))
                        {
                            sessionId = session.SessionId;
                            sessions.SetCurrentFromRemote(session.SessionId);
                        }

                        continue;

                    case AgentSseEventNames.Done:
                        completed = true;
                        continue;

                    case AgentSseEventNames.PlanItem:
                        if (request.OnPlannedAction is not null
                            && AgentEventTranslator.TryReadToolInvocation(frame, out var plannedTool, out var plannedArgs))
                        {
                            request.OnPlannedAction(plannedTool, plannedArgs);
                        }

                        continue;

                    case AgentSseEventNames.ToolDenied:
                        if (request.OnUserDenied is not null
                            && AgentEventTranslator.TryReadToolInvocation(frame, out var deniedTool, out var deniedArgs))
                        {
                            request.OnUserDenied(deniedTool, deniedArgs);
                        }

                        continue;

                    case AgentSseEventNames.OrchestrationProgress:
                        // 编排在服务端执行，这里把进度还原成本地出口事件，界面消费方无需感知远程与否
                        if (frame.GetPayload<OrchestrationProgressPayload>() is { } progress)
                        {
                            services.GetService<HostOrchestrationProgressSink>()?.Raise(progress.ToEvent());
                        }

                        continue;

                    case AgentSseEventNames.ToolConfirm:
                        if (AgentEventTranslator.TryTranslate(frame, out var confirmation)
                            && confirmation is ToolConfirmationRequestedEvent pending)
                        {
                            if (await ResolveConfirmationAsync(pending, sessionId, request, ct).ConfigureAwait(false))
                            {
                                continue;
                            }
                        }

                        break;
                }

                if (AgentEventTranslator.TryTranslate(frame, out var evt) && evt is not null)
                {
                    yield return evt;
                }
            }
        }
        finally
        {
            // 未收到 done：说明连接中断、调用方取消或提前停止枚举，服务端不会自行结束回合。
            // 带上本轮 runId：若新回合已登记，服务端会拒绝这次迟到取消，避免误伤。
            if (!completed)
            {
                await CancelAsync(sessionId, runId).ConfigureAwait(false);
            }
        }
    }

    /// <summary>取得用户选择并回传确认结果。</summary>
    /// <returns>是否已回传（false 表示帧无法解析，按普通事件透传）。</returns>
    private async Task<bool> ResolveConfirmationAsync(
        ToolConfirmationRequestedEvent pending,
        string? sessionId,
        AgentRunRequest request,
        CancellationToken ct)
    {
        var result = ConfirmResult.Deny;
        try
        {
            if (request.ConfirmHandlerAsync is not null)
            {
                result = await request.ConfirmHandlerAsync(pending.ToolName, pending.Arguments, ct).ConfigureAwait(false);
            }
            else if (request.ConfirmHandler is not null)
            {
                // 同步回调在 UI 线程上阻塞等待用户选择，放到独立线程避免卡住流读取
                result = await Task.Run(() => request.ConfirmHandler(pending.ToolName, pending.Arguments), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"工具确认回调异常，按拒绝处理：{ex.Message}");
            result = ConfirmResult.Deny;
        }

        if (ct.IsCancellationRequested)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            // 首帧之前不该出现确认帧；没有会话标识时服务端会按 400 拒绝，直接跳过并记日志
            Logger.Warn($"缺少会话标识，跳过工具确认回传（confirmationId={pending.ConfirmationId}）");
            return true;
        }

        try
        {
            await client.PostAsync(
                AgentApiRoutes.Agent.Confirm,
                new AgentConfirmRequest
                {
                    SessionId = sessionId,
                    ConfirmationId = pending.ConfirmationId,
                    Result = ToWire(result)
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warn($"回传工具确认失败（confirmationId={pending.ConfirmationId}）：{ex.Message}");
        }

        return true;
    }

    private async Task CancelAsync(string? sessionId, string? runId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.PostAsync(
                AgentApiRoutes.Agent.Cancel,
                new AgentCancelRequest { SessionId = sessionId, RunId = runId },
                timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"取消远端回合失败（sessionId={sessionId}）：{ex.Message}");
        }
    }

    private static string ToWire(ConfirmResult result) => result switch
    {
        ConfirmResult.Allow => "allow",
        ConfirmResult.AllowAll => "allowAll",
        ConfirmResult.AllowSession => "allowSession",
        _ => "deny"
    };

    /// <summary><c>session</c> 帧载荷。</summary>
    /// <param name="SessionId">实际使用的会话标识。</param>
    /// <param name="Created">是否为本轮新建。</param>
    /// <param name="RunId">本轮标识；取消时回传，避免迟到取消命中新回合。</param>
    private sealed record SessionFramePayload(string SessionId, bool Created, string? RunId);
}