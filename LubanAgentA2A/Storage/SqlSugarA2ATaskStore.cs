/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Storage
*文件名： SqlSugarA2ATaskStore
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：基于 SqlSugar/SQLite 的 A2A 任务存储实现
*
*****************************************************************************/
using System.Text.Json;

using LubanAgentCore.Entities;
using LubanAgentCore.Repositories;

using Sdk = A2A;

namespace LubanAgentA2A.Storage;

/// <summary>
/// 基于 SqlSugar（SQLite <c>luban-ai-agent.db</c>）的 <see cref="Sdk.ITaskStore"/> 实现。
/// <para>完整 <see cref="Sdk.AgentTask"/> 快照以 JSON 落库到 <see cref="DbA2ATask.Payload"/>，
/// 关键检索列（TaskId/ContextId/State/StatusTimestamp）单独持久化以便查询与分页。</para>
/// </summary>
public sealed class SqlSugarA2ATaskStore : Sdk.ITaskStore
{
    /// <summary>
    /// 查询/保存时使用的 AgentTask 快照 JSON 序列化选项（与 SDK 内部保持一致）。
    /// </summary>
    private static readonly JsonSerializerOptions TaskJsonOptions = Sdk.A2AJsonUtilities.DefaultOptions;

    /// <summary>
    /// 创建 <see cref="DbA2ATask"/> 仓储实例（共享 SQLite 连接）。
    /// </summary>
    private static A2ATaskRepository CreateRepository() => new();

    /// <inheritdoc />
    public async Task<Sdk.AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var entity = await CreateRepository().GetByTaskIdAsync(taskId);
        return entity == null ? null : Deserialize(entity.Payload);
    }

    /// <inheritdoc />
    public async Task SaveTaskAsync(string taskId, Sdk.AgentTask task, CancellationToken cancellationToken = default)
    {
        var entity = new DbA2ATask
        {
            TaskId = taskId,
            ContextId = task.ContextId,
            State = (int)task.Status.State,
            StatusTimestamp = task.Status.Timestamp?.UtcDateTime,
            Payload = JsonSerializer.Serialize(task, TaskJsonOptions),
        };

        await CreateRepository().UpsertAsync(entity);
    }

    /// <inheritdoc />
    public async Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await CreateRepository().DeleteByTaskIdAsync(taskId);
    }

    /// <inheritdoc />
    public async Task<Sdk.ListTasksResponse> ListTasksAsync(Sdk.ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        int startIndex = 0;
        if (!string.IsNullOrEmpty(request.PageToken))
        {
            if (!int.TryParse(request.PageToken, out var offset) || offset < 0)
                throw new Sdk.A2AException($"Invalid pageToken: {request.PageToken}", Sdk.A2AErrorCode.InvalidParams);

            startIndex = offset;
        }

        var pageSize = request.PageSize ?? 50;
        if (pageSize < 0) pageSize = 0;

        int? stateFilter = request.Status is { } status ? (int)status : null;
        DateTime? timestampAfter = request.StatusTimestampAfter?.UtcDateTime;

        var (entities, totalSize) = await CreateRepository()
            .QueryAsync(request.ContextId, stateFilter, timestampAfter, startIndex, pageSize);

        var page = entities.Select(e => Deserialize(e.Payload)).ToList();

        if (request.HistoryLength is { } historyLength)
        {
            foreach (var task in page)
            {
                if (historyLength == 0)
                {
                    task.History = null;
                }
                else if (task.History is { Count: > 0 })
                {
                    task.History = task.History
                        .Skip(Math.Max(0, task.History.Count - historyLength))
                        .ToList();
                }
            }
        }

        if (request.IncludeArtifacts is not true)
        {
            foreach (var task in page)
            {
                task.Artifacts = null;
            }
        }

        var nextIndex = startIndex + page.Count;
        var nextPageToken = nextIndex < totalSize
            ? nextIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        return new Sdk.ListTasksResponse
        {
            Tasks = page,
            NextPageToken = nextPageToken,
            PageSize = page.Count,
            TotalSize = totalSize,
        };
    }

    /// <summary>
    /// 反序列化任务快照；若损坏则返回一个仅含 ID 的占位任务，避免列表整体失败。
    /// </summary>
    private static Sdk.AgentTask Deserialize(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<Sdk.AgentTask>(payload, TaskJsonOptions)
                   ?? CreatePlaceholder();
        }
        catch (JsonException)
        {
            return CreatePlaceholder();
        }
    }

    /// <summary>
    /// 构造一个状态为 <see cref="Sdk.TaskState.Unspecified"/> 的占位任务。
    /// </summary>
    private static Sdk.AgentTask CreatePlaceholder()
        => new() { Status = new Sdk.TaskStatus { State = Sdk.TaskState.Unspecified } };
}