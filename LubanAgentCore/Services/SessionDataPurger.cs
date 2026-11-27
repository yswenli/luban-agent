/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services
*文件名： SessionDataPurger
*描述：会话级联物理清理（消息 → worktree 登记 → 会话行）
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 会话级联物理清理器。
/// </summary>
/// <remarks>
/// 消息表与 worktree 登记表只带 SessionId、没有工作区与用户列，必须先按会话集合清从属数据、
/// 再从会话表删行；顺序集中在此处，避免清理全部、清理指定工作区、删除工作区三处各写一份而逐渐漂移。
/// 只清理数据库行：worktree 目录与分支不在这里删除，留给用户或 worktree 对账流程处理。
/// </remarks>
internal sealed class SessionDataPurger
{
    private readonly SessionRepository _sessions;
    private readonly SessionMessageRepository _messages;
    private readonly WorktreeSessionRepository _worktrees;

    /// <summary>
    /// 按默认仓储创建清理器。
    /// </summary>
    public SessionDataPurger()
        : this(new SessionRepository(), new SessionMessageRepository(), new WorktreeSessionRepository())
    {
    }

    /// <summary>
    /// 按指定仓储创建清理器（供已持有仓储实例的调用方复用）。
    /// </summary>
    /// <param name="sessions">会话仓储。</param>
    /// <param name="messages">会话消息仓储。</param>
    /// <param name="worktrees">worktree 会话仓储。</param>
    public SessionDataPurger(
        SessionRepository sessions,
        SessionMessageRepository messages,
        WorktreeSessionRepository worktrees)
    {
        _sessions = sessions;
        _messages = messages;
        _worktrees = worktrees;
    }

    /// <summary>
    /// 物理删除指定用户的全部会话及其从属数据（含此前软删除的会话）。
    /// </summary>
    /// <param name="userId">归属用户。</param>
    /// <returns>被清理的会话ID集合（含软删除）。</returns>
    public async Task<IReadOnlyList<string>> PurgeAllForUserAsync(string userId)
    {
        var sessionIds = await _sessions.GetSessionIdsAsync(userId);
        await PurgeChildrenAsync(sessionIds);
        await _sessions.PurgeAllAsync(userId);
        return sessionIds;
    }

    /// <summary>
    /// 物理删除指定工作区下当前用户的会话及其从属数据（含此前软删除的会话）。
    /// </summary>
    /// <param name="workspaceId">目标工作区；为空时不做任何删除。</param>
    /// <param name="userId">归属用户。</param>
    /// <returns>被清理的会话ID集合（含软删除）；目标为空时为空集合。</returns>
    public async Task<IReadOnlyList<string>> PurgeWorkspaceAsync(string workspaceId, string userId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId)) return Array.Empty<string>();

        var sessionIds = await _sessions.GetAllSessionIdsByWorkspaceAsync(workspaceId, userId);
        await PurgeChildrenAsync(sessionIds);
        await _sessions.PurgeByWorkspaceAsync(workspaceId, userId);
        return sessionIds;
    }

    /// <summary>
    /// 按会话集合清理从属数据：消息与 worktree 登记。
    /// </summary>
    /// <param name="sessionIds">会话标识集合。</param>
    private async Task PurgeChildrenAsync(IReadOnlyCollection<string> sessionIds)
    {
        if (sessionIds.Count == 0) return;

        await _messages.PurgeBySessionIdsAsync(sessionIds).ConfigureAwait(false);
        await _worktrees.PurgeBySessionIdsAsync(sessionIds).ConfigureAwait(false);
    }
}