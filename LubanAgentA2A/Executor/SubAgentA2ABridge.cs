/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Executor
*文件名： SubAgentA2ABridge
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：子代理规格与 A2A 消息/事件之间的桥接工具，供进程内与远程执行器复用
*
*****************************************************************************/
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using LuBan.AIAgent.A2A.Models;
using LuBan.AIAgent.Orchestration.Models;

using LubanAgentA2A.InProcess;

namespace LubanAgentA2A.Executor;

/// <summary>
/// 子代理 A2A 桥接工具。负责 <see cref="SubAgentSpec"/> → <see cref="A2AMessage"/> 的编码，
/// 以及 <see cref="A2AStreamEvent"/> → <see cref="SubAgentExecutionEvent"/> 的解码，供两类执行器复用。
/// </summary>
internal static class SubAgentA2ABridge
{
    /// <summary>
    /// 把子代理规格编码为带规格标记的 A2A 消息。
    /// </summary>
    public static A2AMessage BuildMessage(SubAgentSpec spec)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InProcessProtocol.SpecMarker] = InProcessProtocol.SpecSubAgent,
            [InProcessProtocol.NodeId] = spec.NodeId,
        };

        if (!string.IsNullOrEmpty(spec.Role))
        {
            metadata[InProcessProtocol.Role] = spec.Role!;
        }
        if (spec.ToolGroups is { Count: > 0 })
        {
            metadata[InProcessProtocol.ToolGroups] = JsonSerializer.Serialize(spec.ToolGroups);
        }
        if (!string.IsNullOrEmpty(spec.ModelName))
        {
            metadata[InProcessProtocol.ModelName] = spec.ModelName!;
        }
        if (!string.IsNullOrEmpty(spec.WorkspaceRoot))
        {
            metadata[InProcessProtocol.WorkspaceRoot] = spec.WorkspaceRoot!;
        }
        if (!string.IsNullOrEmpty(spec.SharedContext))
        {
            metadata[InProcessProtocol.SharedContext] = spec.SharedContext!;
        }
        if (!string.IsNullOrEmpty(spec.ParentSessionId))
        {
            metadata[InProcessProtocol.ParentSessionId] = spec.ParentSessionId!;
        }

        return new A2AMessage
        {
            Role = A2ARole.User,
            Parts = new List<A2APart> { A2APart.FromText(spec.Prompt) },
            TaskId = BuildTaskId(spec),
            ContextId = string.IsNullOrEmpty(spec.ParentSessionId) ? null : spec.ParentSessionId,
            Metadata = metadata,
        };
    }

    /// <summary>
    /// 组装子任务标识 <c>"{GraphId}:{nodeId}"</c>（§4.3 节点映射）。
    /// <see cref="SubAgentSpec.ParentSessionId"/> 由调度器填充为图谱 Id（<c>graph.GraphId</c>）；
    /// 缺少任一要素时返回 null，交由处理器生成随机标识。
    /// </summary>
    /// <param name="spec">子代理规格。</param>
    /// <returns>子任务标识；无法组装时为 null。</returns>
    private static string? BuildTaskId(SubAgentSpec spec)
        => string.IsNullOrEmpty(spec.ParentSessionId) || string.IsNullOrEmpty(spec.NodeId)
            ? null
            : $"{spec.ParentSessionId}:{spec.NodeId}";

    /// <summary>
    /// 把 A2A 事件流解码为子代理执行事件流。
    /// </summary>
    /// <param name="source">A2A 事件源。</param>
    /// <param name="sessionId">初始会话标识（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    public static async IAsyncEnumerable<SubAgentExecutionEvent> DecodeAsync(
        IAsyncEnumerable<A2AStreamEvent> source, string? sessionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var output = new StringBuilder();
        string? artifactText = null;
        string? failure = null;

        await using var enumerator = source.GetAsyncEnumerator(ct);
        while (true)
        {
            A2AStreamEvent? evt = null;
            Exception? error = null;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }
                evt = enumerator.Current;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (error is not null)
            {
                failure = error.Message;
                break;
            }

            if (evt!.Kind == A2AStreamEventKind.Artifact)
            {
                if (evt.Artifact is not null)
                {
                    artifactText = ExtractText(evt.Artifact.Parts);
                }
                continue;
            }

            if (evt.Kind != A2AStreamEventKind.Status || evt.Status is null)
            {
                continue;
            }

            switch (evt.Status.State)
            {
                case A2ATaskState.Failed:
                    failure = ExtractText(evt.Status.Message) ?? "子代理执行失败";
                    continue;
                case A2ATaskState.Canceled:
                    failure = "子代理执行已取消";
                    continue;
                case A2ATaskState.Rejected:
                    failure = ExtractText(evt.Status.Message) ?? "子代理执行被拒绝";
                    continue;
            }

            var activity = GetMetadata(evt, InProcessProtocol.ActivityKind);
            switch (activity)
            {
                case InProcessProtocol.ActivityStarted:
                    sessionId = GetMetadata(evt, InProcessProtocol.SessionId) ?? sessionId;
                    yield return new SubAgentExecutionEvent(SubAgentEventKind.Started, sessionId, null, null);
                    break;

                case InProcessProtocol.ActivityThinking:
                    yield return new SubAgentExecutionEvent(SubAgentEventKind.Thinking, sessionId, ExtractText(evt.Status.Message), null);
                    break;

                case InProcessProtocol.ActivityToolCall:
                    yield return new SubAgentExecutionEvent(SubAgentEventKind.ToolCall, sessionId, ExtractText(evt.Status.Message), GetMetadata(evt, InProcessProtocol.Detail));
                    break;

                case InProcessProtocol.ActivityToolResult:
                    yield return new SubAgentExecutionEvent(SubAgentEventKind.ToolResult, sessionId, ExtractText(evt.Status.Message), null);
                    break;

                case InProcessProtocol.ActivityText:
                    var delta = ExtractText(evt.Status.Message);
                    if (!string.IsNullOrEmpty(delta))
                    {
                        output.Append(delta);
                    }
                    yield return new SubAgentExecutionEvent(SubAgentEventKind.Text, sessionId, delta, null);
                    break;
            }
        }

        if (failure is not null)
        {
            yield return new SubAgentExecutionEvent(SubAgentEventKind.Failed, sessionId, failure, null);
            yield break;
        }

        var finalOutput = !string.IsNullOrEmpty(artifactText) ? artifactText! : output.ToString();
        yield return new SubAgentExecutionEvent(SubAgentEventKind.Completed, sessionId, finalOutput, null);
    }

    private static string? GetMetadata(A2AStreamEvent evt, string key)
        => evt.Metadata is not null && evt.Metadata.TryGetValue(key, out var value) ? value : null;

    private static string? ExtractText(A2AMessage? message)
        => message?.Parts?.FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text;

    private static string? ExtractText(List<A2APart>? parts)
    {
        if (parts is null || parts.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part.Text))
            {
                sb.Append(part.Text);
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}