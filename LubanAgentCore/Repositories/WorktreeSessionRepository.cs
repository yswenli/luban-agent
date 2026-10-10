/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Repositories
*文件名： WorktreeSessionRepository
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：worktree 会话仓储
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions.Models;

namespace LubanAgentCore.Repositories;

/// <summary>
/// worktree 会话仓储
/// </summary>
public class WorktreeSessionRepository : BaseRepository<DbWorktreeSession>
{
    /// <summary>
    /// 创建 worktree 会话仓储实例。
    /// </summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户</param>
    public WorktreeSessionRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>
    /// 保存（存在则更新，否则插入）一条 worktree 会话记录。
    /// </summary>
    /// <param name="session">worktree 会话记录。</param>
    public async Task SaveAsync(WorktreeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var existing = await GetFirstAsync(x => x.SessionId == session.SessionId && !x.IsDelete);
        if (existing is null)
        {
            await InsertAsync(DbWorktreeSession.From(session));
        }
        else
        {
            var entity = DbWorktreeSession.From(session);
            entity.Id = existing.Id;
            await UpdateAsync(entity, x => x.Id == existing.Id);
        }
    }

    /// <summary>
    /// 根据会话ID获取 worktree 记录。
    /// </summary>
    /// <param name="sessionId">会话ID。</param>
    /// <returns>worktree 会话记录；未找到返回 null。</returns>
    public async Task<WorktreeSession?> GetBySessionAsync(string sessionId)
    {
        var entity = await GetFirstAsync(x => x.SessionId == sessionId && !x.IsDelete);
        return entity?.ToModel();
    }

    /// <summary>
    /// 获取全部 worktree 会话记录。
    /// </summary>
    /// <returns>worktree 会话记录列表。</returns>
    public async Task<List<WorktreeSession>> GetAllAsync()
    {
        var list = await AsQueryable()
            .Where(x => !x.IsDelete)
            .OrderByDescending(x => x.CreateTime)
            .ToListAsync();
        return list.Select(x => x.ToModel()).ToList();
    }

    /// <summary>
    /// 物理删除指定会话的 worktree 记录。
    /// </summary>
    /// <param name="sessionId">会话ID。</param>
    public async Task DeleteBySessionAsync(string sessionId)
    {
        await DeleteAsync(x => x.SessionId == sessionId);
    }
}