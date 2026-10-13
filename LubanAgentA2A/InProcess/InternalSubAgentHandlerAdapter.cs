/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.InProcess
*文件名： InternalSubAgentHandlerAdapter
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：进程内 A2A 句柄适配器，同时实现框架 IA2AHandler 与 SDK IAgentHandler，
*      将一条 A2A 消息落到本地 LuBanAgent 执行并回填事件
*
*****************************************************************************/
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using LuBan.AIAgent;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.A2A.Models;
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;

using LubanAgentA2A.Mapping;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sdk = A2A;

namespace LubanAgentA2A.InProcess;

/// <summary>
/// 进程内 A2A 句柄适配器。无状态、可被多个 Agent 名复用。
/// <para>框架 <see cref="IA2AHandler"/>：新建事件队列，后台执行核心逻辑，前台消费并映射为框架事件流；
/// 核心异常兜底收尾队列，避免消费者挂起。</para>
/// <para>SDK <see cref="Sdk.IAgentHandler"/>：直接执行核心逻辑，由 SDK 完成队列收尾与任务持久化。</para>
/// 核心逻辑统一走 <see cref="RunCoreAsync"/>，用 <see cref="Sdk.TaskUpdater"/> 发出生命周期事件。
/// </summary>
public sealed class InternalSubAgentHandlerAdapter : IA2AHandler, Sdk.IAgentHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InternalSubAgentHandlerAdapter>? _logger;

    /// <summary>
    /// 创建句柄适配器实例。
    /// </summary>
    /// <param name="scopeFactory">服务作用域工厂（每次执行创建独立作用域以解析 Scoped 的代理工厂）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public InternalSubAgentHandlerAdapter(
        IServiceScopeFactory scopeFactory,
        ILogger<InternalSubAgentHandlerAdapter>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// 框架侧入口：处理入站消息并流式产出框架事件。
    /// </summary>
    /// <param name="message">入站消息。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>框架 A2A 流式事件。</returns>
    public async IAsyncEnumerable<A2AStreamEvent> ExecuteAsync(
        A2AMessage message, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var taskId = string.IsNullOrEmpty(message.TaskId) ? Guid.NewGuid().ToString("N") : message.TaskId!;
        var contextId = string.IsNullOrEmpty(message.ContextId) ? taskId : message.ContextId!;
        var queue = new Sdk.AgentEventQueue();

        // 后台执行核心逻辑；异常兜底收尾队列，保证下面的消费循环一定能结束。
        _ = RunGuardedAsync(message, taskId, contextId, queue, ct);

        await using var enumerator = queue.GetAsyncEnumerator(ct);
        while (await enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            yield return enumerator.Current.ToFramework();
        }
    }

    /// <summary>
    /// 取消指定任务。进程内取消为尽力而为：传输层持有任务句柄，句柄侧无独立取消语义。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已完成的任务。</returns>
    public Task CancelAsync(string taskId, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// SDK 侧入口：直接执行核心逻辑，由 SDK 负责队列收尾与任务生命周期。
    /// </summary>
    /// <param name="context">SDK 预解析请求上下文。</param>
    /// <param name="eventQueue">SDK 事件队列。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行任务。</returns>
    public Task ExecuteAsync(Sdk.RequestContext context, Sdk.AgentEventQueue eventQueue, CancellationToken cancellationToken)
        => RunCoreAsync(context.Message.ToFramework(), context.TaskId, context.ContextId, eventQueue, cancellationToken);

    private async Task RunGuardedAsync(
        A2AMessage message, string taskId, string contextId, Sdk.AgentEventQueue queue, CancellationToken ct)
    {
        try
        {
            await RunCoreAsync(message, taskId, contextId, queue, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "A2A 进程内处理失败，taskId={TaskId}", taskId);
            queue.Complete(ex);
        }
    }

    /// <summary>
    /// 核心执行逻辑：解析规格 → 创建代理 → 流式执行 → 回填状态/活动/产出物 → 收尾。
    /// 仅负责 T2A 内部映射；对外（入站无规格）按「单代理轮次」处理。
    /// </summary>
    private async Task RunCoreAsync(
        A2AMessage message, string taskId, string contextId, Sdk.AgentEventQueue queue, CancellationToken ct)
    {
        var updater = new Sdk.TaskUpdater(queue, taskId, contextId);
        await updater.SubmitAsync(cancellationToken: ct).ConfigureAwait(false);

        var spec = TryBuildSpec(message);

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        // 严格身份回填：root 派生 scope 不带身份，从 A2A 消息元数据取用户并写入；
        // 缺失时回退本机默认用户（本地单用户）。
        var userId = ResolveUserId(message);
        services.GetRequiredService<LubanAgentCore.Context.UserContextState>().UserId = userId;

        LuBanAgent agent;
        string prompt;
        try
        {
            if (spec is not null)
            {
                // 仅创建子代理；执行由本适配器自行流式驱动，避免 SubAgentFactory 的默认确认作用域覆盖外层 A3 作用域。
                agent = await services.GetRequiredService<SubAgentFactory>().CreateAsync(spec, ct).ConfigureAwait(false);
                prompt = spec.Prompt;
            }
            else
            {
                prompt = ExtractText(message);
                agent = await services.GetRequiredService<LuBanAgentFactory>().CreateAsync(cancellationToken: ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await updater.FailAsync(BuildMessage(ex.Message), cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        await updater.StartWorkAsync(cancellationToken: ct).ConfigureAwait(false);
        await EmitActivityAsync(queue, taskId, contextId, spec?.NodeId, InProcessProtocol.ActivityStarted, null, agent.Id, null, ct).ConfigureAwait(false);

        var output = new StringBuilder();
        Exception? streamError = null;

        await using (var enumerator = agent.RunStreamingAsync(prompt, ct).GetAsyncEnumerator(ct))
        {
            while (true)
            {
                AgentResponseUpdate? update = null;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                    update = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    streamError = ex;
                }

                if (streamError is not null)
                {
                    break;
                }

                if (update!.Contents is null)
                {
                    continue;
                }

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                            await EmitActivityAsync(queue, taskId, contextId, spec?.NodeId, InProcessProtocol.ActivityThinking, reasoning.Text, null, null, ct).ConfigureAwait(false);
                            break;

                        case FunctionCallContent functionCall:
                            await EmitActivityAsync(queue, taskId, contextId, spec?.NodeId, InProcessProtocol.ActivityToolCall, functionCall.Name, null, SummarizeArguments(functionCall.Arguments), ct).ConfigureAwait(false);
                            break;

                        case FunctionResultContent functionResult:
                            var resultText = functionResult.Exception is not null
                                ? $"❌ {functionResult.Exception.Message}"
                                : SummarizeResult(functionResult.Result);
                            await EmitActivityAsync(queue, taskId, contextId, spec?.NodeId, InProcessProtocol.ActivityToolResult, resultText, null, null, ct).ConfigureAwait(false);
                            break;

                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            output.Append(text.Text);
                            await EmitActivityAsync(queue, taskId, contextId, spec?.NodeId, InProcessProtocol.ActivityText, text.Text, null, null, ct).ConfigureAwait(false);
                            break;
                    }
                }
            }
        }

        if (streamError is not null)
        {
            await updater.FailAsync(BuildMessage(streamError.Message), cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        var artifactId = string.IsNullOrEmpty(spec?.NodeId) ? taskId : spec!.NodeId;
        await updater.AddArtifactAsync(
            parts: new List<Sdk.Part> { new Sdk.Part { Text = output.ToString() } },
            artifactId: artifactId,
            name: artifactId,
            description: null,
            lastChunk: true,
            append: false,
            cancellationToken: ct).ConfigureAwait(false);

        await updater.CompleteAsync(cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 以 Working 状态更新承载一条活动事件；文本放于状态消息，事件语义放于事件级 metadata。
    /// </summary>
    private static async ValueTask EmitActivityAsync(
        Sdk.AgentEventQueue queue, string taskId, string contextId,
        string? nodeId, string activityKind, string? text, string? sessionId, string? detail, CancellationToken ct)
    {
        var metadata = new Dictionary<string, JsonElement>(5)
        {
            [InProcessProtocol.EventType] = JsonSerializer.SerializeToElement(InProcessProtocol.EventTypeActivity),
            [InProcessProtocol.ActivityKind] = JsonSerializer.SerializeToElement(activityKind),
        };

        if (!string.IsNullOrEmpty(nodeId))
        {
            metadata[InProcessProtocol.NodeId] = JsonSerializer.SerializeToElement(nodeId);
        }
        if (!string.IsNullOrEmpty(sessionId))
        {
            metadata[InProcessProtocol.SessionId] = JsonSerializer.SerializeToElement(sessionId);
        }
        if (!string.IsNullOrEmpty(detail))
        {
            metadata[InProcessProtocol.Detail] = JsonSerializer.SerializeToElement(detail);
        }

        var status = new Sdk.TaskStatus
        {
            State = Sdk.TaskState.Working,
            Timestamp = DateTimeOffset.UtcNow,
        };
        if (!string.IsNullOrEmpty(text))
        {
            status.Message = new Sdk.Message
            {
                Role = Sdk.Role.Agent,
                Parts = new List<Sdk.Part> { new Sdk.Part { Text = text } },
            };
        }

        var update = new Sdk.TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = status,
            Metadata = metadata,
        };

        await queue.EnqueueStatusUpdateAsync(update, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从消息 metadata 重建子代理规格；无规格标记时返回 null（按单代理轮次处理）。
    /// </summary>
    private static SubAgentSpec? TryBuildSpec(A2AMessage message)
    {
        if (message.Metadata is null
            || !message.Metadata.TryGetValue(InProcessProtocol.SpecMarker, out var marker)
            || !string.Equals(marker, InProcessProtocol.SpecSubAgent, StringComparison.Ordinal))
        {
            return null;
        }

        return new SubAgentSpec
        {
            NodeId = message.Metadata.GetValueOrDefault(InProcessProtocol.NodeId) ?? string.Empty,
            Prompt = ExtractText(message),
            Role = message.Metadata.GetValueOrDefault(InProcessProtocol.Role),
            ToolGroups = ParseToolGroups(message.Metadata.GetValueOrDefault(InProcessProtocol.ToolGroups)),
            ModelName = message.Metadata.GetValueOrDefault(InProcessProtocol.ModelName),
            ParentSessionId = message.Metadata.GetValueOrDefault(InProcessProtocol.ParentSessionId) ?? string.Empty,
            WorkspaceRoot = message.Metadata.GetValueOrDefault(InProcessProtocol.WorkspaceRoot),
            SharedContext = message.Metadata.GetValueOrDefault(InProcessProtocol.SharedContext),
        };
    }

    private static List<string>? ParseToolGroups(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractText(A2AMessage message)
        => message.Parts?.FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text ?? string.Empty;

    /// <summary>
    /// 从 A2A 消息解析用户标识：优先 <c>Metadata["userId"]</c>，缺失回退本机默认用户。
    /// </summary>
    private static string ResolveUserId(A2AMessage message)
    {
        if (message.Metadata is { Count: > 0 } meta
            && meta.TryGetValue("userId", out var uid)
            && !string.IsNullOrWhiteSpace(uid))
        {
            return uid.Trim();
        }
        return LubanAgentCore.Hosting.LocalUserDefaults.DefaultUserId;
    }

    private static Sdk.Message BuildMessage(string text)
        => new() { Role = Sdk.Role.Agent, Parts = new List<Sdk.Part> { new Sdk.Part { Text = text } } };

    private static string? SummarizeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var kv in arguments)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(kv.Key).Append('=').Append(FormatArgumentValue(kv.Value));
            if (sb.Length > 200)
            {
                sb.Length = 200;
                sb.Append('…');
                break;
            }
        }
        return sb.ToString();
    }

    private static string FormatArgumentValue(object? value) => value switch
    {
        null => "null",
        string s => s.Replace("\r", " ").Replace("\n", " "),
        _ => value.ToString() ?? "null"
    };

    private static string? SummarizeResult(object? result)
    {
        if (result is null) return null;

        var text = result switch
        {
            string s => s,
            _ => result.ToString()
        };
        if (string.IsNullOrWhiteSpace(text)) return null;

        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 200 ? text[..200] + "…" : text;
    }
}