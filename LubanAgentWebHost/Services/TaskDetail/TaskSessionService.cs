/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.TaskDetail
*文件名： TaskSessionService
*描述：任务会话绑定校验与读写编排
*****************************************************************************/

namespace LubanAgentWebHost.Services.TaskDetail;

/// <summary>任务会话绑定服务。</summary>
public sealed class TaskSessionService
{
    private readonly TaskSessionStore _store;

    /// <summary>创建任务会话绑定服务。</summary>
    public TaskSessionService(TaskSessionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>读取任务当前有效绑定。</summary>
    public async Task<YunxiaoWorkbenchResult<TaskSession?>> GetAsync(
        string? workitemId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workitemId))
            return YunxiaoWorkbenchResult<TaskSession?>.Failure(400, "工作项 ID 不能为空");
        var session = await _store.GetAsync(workitemId.Trim()).ConfigureAwait(false);
        return YunxiaoWorkbenchResult<TaskSession?>.Success(session);
    }

    /// <summary>保存任务当前有效绑定。</summary>
    public async Task<YunxiaoWorkbenchResult<TaskSession>> SaveAsync(
        TaskSession? session, CancellationToken cancellationToken = default)
    {
        if (session is null
            || string.IsNullOrWhiteSpace(session.WorkItemId)
            || string.IsNullOrWhiteSpace(session.WorkspaceId)
            || string.IsNullOrWhiteSpace(session.SessionId)
            || string.IsNullOrWhiteSpace(session.SeedDigest))
        {
            return YunxiaoWorkbenchResult<TaskSession>.Failure(400, "工作项、工作区、会话与首轮摘要不能为空");
        }

        session.WorkItemId = session.WorkItemId.Trim();
        session.WorkspaceId = session.WorkspaceId.Trim();
        session.SessionId = session.SessionId.Trim();
        session.SeedDigest = session.SeedDigest.Trim();
        if (string.IsNullOrWhiteSpace(session.LinkId)) session.LinkId = Guid.NewGuid().ToString("N");
        if (session.SeedAt == default) session.SeedAt = DateTime.Now;
        session.LastActiveAt = DateTime.Now;
        await _store.UpsertAsync(session).ConfigureAwait(false);
        return YunxiaoWorkbenchResult<TaskSession>.Success(session);
    }
}
