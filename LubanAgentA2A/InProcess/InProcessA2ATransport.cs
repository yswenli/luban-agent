/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.InProcess
*文件名： InProcessA2ATransport
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：进程内 A2A 传输，按 Agent 名路由到注册表句柄，直连不经过 HTTP
*
*****************************************************************************/
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.A2A.Models;

namespace LubanAgentA2A.InProcess;

/// <summary>
/// 进程内 A2A 传输。按 Agent 名从 <see cref="IA2AAgentRegistry"/> 解析句柄并直连调用，
/// 不走网络；同时维护一份内存任务快照，供查询/取消/订阅使用。
/// </summary>
public sealed class InProcessA2ATransport : IA2ATransport
{
    private readonly IA2AAgentRegistry _registry;
    private readonly ConcurrentDictionary<string, A2ATask> _tasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _taskAgents = new(StringComparer.Ordinal);

    /// <summary>
    /// 创建进程内传输实例。
    /// </summary>
    /// <param name="registry">A2A Agent 注册表。</param>
    public InProcessA2ATransport(IA2AAgentRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public async Task<A2ATask> SendAsync(string agentName, A2AMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var taskId = string.IsNullOrEmpty(message.TaskId) ? Guid.NewGuid().ToString("N") : message.TaskId!;
        var contextId = string.IsNullOrEmpty(message.ContextId) ? taskId : message.ContextId!;
        var normalized = CloneWithIds(message, taskId, contextId);

        var task = new A2ATask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new A2ATaskStatus { State = A2ATaskState.Submitted, Timestamp = DateTimeOffset.UtcNow },
        };

        await using var enumerator = SendStreamingAsync(agentName, normalized, ct).GetAsyncEnumerator(ct);
        while (await enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            Apply(task, enumerator.Current);
        }

        _tasks[taskId] = task;
        _taskAgents[taskId] = agentName;
        return task;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<A2AStreamEvent> SendStreamingAsync(
        string agentName, A2AMessage message, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_registry.TryResolve(agentName, out _, out var handler))
        {
            throw new InvalidOperationException($"未注册的 A2A Agent：{agentName}");
        }

        await using var enumerator = handler.ExecuteAsync(message, ct).GetAsyncEnumerator(ct);
        while (true)
        {
            A2AStreamEvent? current = null;
            Exception? error = null;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }
                current = enumerator.Current;
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
                ExceptionDispatchInfo.Capture(error).Throw();
            }

            yield return current!;
        }
    }

    /// <inheritdoc />
    public Task<A2ATask?> GetTaskAsync(string agentName, string taskId, CancellationToken ct = default)
    {
        if (_taskAgents.TryGetValue(taskId, out var owner)
            && string.Equals(owner, agentName, StringComparison.Ordinal)
            && _tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult<A2ATask?>(task);
        }

        return Task.FromResult<A2ATask?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<A2ATask>> ListTasksAsync(string agentName, CancellationToken ct = default)
    {
        IReadOnlyList<A2ATask> list = _taskAgents
            .Where(kv => string.Equals(kv.Value, agentName, StringComparison.Ordinal))
            .Select(kv => _tasks.TryGetValue(kv.Key, out var task) ? task : null)
            .Where(task => task is not null)
            .Cast<A2ATask>()
            .ToList();

        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public async Task<A2ATask?> CancelAsync(string agentName, string taskId, CancellationToken ct = default)
    {
        if (!_registry.TryResolve(agentName, out _, out var handler))
        {
            return null;
        }

        await handler.CancelAsync(taskId, ct).ConfigureAwait(false);

        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Status = new A2ATaskStatus { State = A2ATaskState.Canceled, Timestamp = DateTimeOffset.UtcNow };
            return task;
        }

        return null;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<A2AStreamEvent> SubscribeAsync(
        string agentName, string taskId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 进程内无事件回放缓冲，仅回吐当前任务快照（若有）。
        await Task.CompletedTask.ConfigureAwait(false);

        if (_taskAgents.TryGetValue(taskId, out var owner)
            && string.Equals(owner, agentName, StringComparison.Ordinal)
            && _tasks.TryGetValue(taskId, out var task))
        {
            yield return new A2AStreamEvent
            {
                Kind = A2AStreamEventKind.Status,
                TaskId = task.Id,
                ContextId = task.ContextId,
                Status = task.Status,
            };
        }
    }

    private static A2AMessage CloneWithIds(A2AMessage message, string taskId, string contextId)
        => new()
        {
            Role = message.Role,
            Parts = message.Parts,
            MessageId = message.MessageId,
            ContextId = contextId,
            TaskId = taskId,
            ReferenceTaskIds = message.ReferenceTaskIds,
            Extensions = message.Extensions,
            Metadata = message.Metadata,
        };

    private static void Apply(A2ATask task, A2AStreamEvent evt)
    {
        if (!string.IsNullOrEmpty(evt.TaskId))
        {
            task.Id = evt.TaskId;
        }
        if (evt.ContextId is not null)
        {
            task.ContextId = evt.ContextId;
        }

        switch (evt.Kind)
        {
            case A2AStreamEventKind.Status when evt.Status is not null:
                task.Status = evt.Status;
                break;

            case A2AStreamEventKind.Artifact when evt.Artifact is not null:
                (task.Artifacts ??= new List<A2AArtifact>()).Add(evt.Artifact);
                break;
        }
    }
}