/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Repositories
*文件名： SessionRepository
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：Session 仓储
*
*****************************************************************************/
namespace LubanAgentCore.Repositories;

/// <summary>
/// Session 仓储
/// </summary>
public class SessionRepository : BaseRepository<DbSession>
{
    /// <summary>
    /// 创建 Session 仓储实例。
    /// </summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户</param>
    public SessionRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>
    /// 根据会话ID和所属用户获取会话（跨用户不可见）
    /// </summary>
    public async Task<DbSession?> GetBySessionIdAsync(string sessionId, string userId)
    {
        return await GetFirstAsync(s => s.SessionId == sessionId && s.UserId == userId && !s.IsDelete);
    }

    /// <summary>
    /// 判断指定用户是否拥有该会话（含软删除；用于消息读写的越权防护）。
    /// </summary>
    /// <remarks>
    /// 含软删除是刻意的：删除会话为“先软删会话行、再物理清消息”，清理前的归属校验在软删后仍须成立。
    /// <c>ai_session.SessionId</c> 建有唯一索引，故命中索引后过滤 <c>UserId</c>（UserId 无独立索引亦可）。
    /// </remarks>
    public async Task<bool> ExistsForUserAsync(string sessionId, string userId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        return await AnyAsync(s => s.SessionId == sessionId && s.UserId == userId);
    }

    /// <summary>
    /// 获取用户的所有会话
    /// </summary>
    public async Task<List<DbSession>> GetUserSessionsAsync(string userId)
    {
        return await AsQueryable()
            .Where(s => s.UserId == userId && !s.IsDelete)
            .OrderByDescending(s => s.CreateTime)
            .ToListAsync();
    }

    /// <summary>
    /// 获取用户全部会话ID（含软删除，用于级联清理消息）
    /// </summary>
    public async Task<List<string>> GetSessionIdsAsync(string userId)
    {
        return await AsQueryable()
            .Where(s => s.UserId == userId)
            .Select(s => s.SessionId)
            .ToListAsync();
    }

    /// <summary>
    /// 更新会话标题（限所属用户）
    /// </summary>
    public async Task UpdateTitleAsync(string sessionId, string title, string userId)
    {
        await UpdateAsync(s => new DbSession { Title = title }, s => s.SessionId == sessionId && s.UserId == userId);
    }

    /// <summary>
    /// 软删除会话（限所属用户）
    /// </summary>
    public async Task SoftDeleteAsync(string sessionId, string userId)
    {
        await LogicDeleteAsync(s => s.SessionId == sessionId && s.UserId == userId);
    }

    /// <summary>
    /// 增加消息计数（限所属用户）
    /// </summary>
    public async Task IncrementMessageCountAsync(string sessionId, int tokens, string userId)
    {
        await Context.Updateable<DbSession>()
            .SetColumns(s => s.MessageCount == s.MessageCount + 1)
            .SetColumns(s => s.TotalTokens == s.TotalTokens + tokens)
            .SetColumns(s => s.UpdateTime == DateTime.Now)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 物理删除指定用户的全部会话
    /// </summary>
    public async Task DeleteAllAsync(string userId)
    {
        await Context.Deleteable<DbSession>().Where(s => s.UserId == userId).ExecuteCommandAsync();
    }

    /// <summary>
    /// 仅累加 Token（用于摘要消息：token 计入，消息数不计；限所属用户）
    /// </summary>
    public async Task IncrementTokenCountAsync(string sessionId, int tokens, string userId)
    {
        await Context.Updateable<DbSession>()
            .SetColumns(s => s.TotalTokens == s.TotalTokens + tokens)
            .SetColumns(s => s.UpdateTime == DateTime.Now)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 获取工作区下当前用户的所有会话
    /// </summary>
    public async Task<List<DbSession>> GetByWorkspaceAsync(string workspaceId, string userId)
    {
        // 如果工作区ID为空，返回空列表，避免查询所有工作区的会话
        if (string.IsNullOrEmpty(workspaceId))
            return new List<DbSession>();

        return await AsQueryable()
            .Where(s => s.WorkspaceId == workspaceId && s.UserId == userId && !s.IsDelete)
            .OrderByDescending(s => s.UpdateTime)
            .ToListAsync();
    }

    /// <summary>
    /// 获取工作区下当前用户的最近活跃会话
    /// </summary>
    public async Task<DbSession?> GetLatestSessionAsync(string workspaceId, string userId)
    {
        // 无会话时返回 null（而非 FirstAsync 抛异常），调用方据此创建默认会话
        var list = await AsQueryable()
            .Where(s => s.WorkspaceId == workspaceId && s.UserId == userId && !s.IsDelete)
            .OrderByDescending(s => s.UpdateTime)
            .Take(1)
            .ToListAsync();
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>
    /// 按标题和工作区查找当前用户的会话
    /// </summary>
    public async Task<DbSession?> GetByTitleAndWorkspaceAsync(string title, string workspaceId, string userId)
    {
        return await GetFirstAsync(s =>
            s.Title != null && s.Title.Contains(title) &&
            s.WorkspaceId == workspaceId && s.UserId == userId && !s.IsDelete);
    }

    /// <summary>
    /// 软删除工作区下当前用户的所有会话
    /// </summary>
    public async Task SoftDeleteByWorkspaceAsync(string workspaceId, string userId)
    {
        await LogicDeleteAsync(s => s.WorkspaceId == workspaceId && s.UserId == userId);
    }

    /// <summary>
    /// 当前用户的全局聚合统计
    /// </summary>
    public async Task<(int sessions, int messages, long tokens, DateTime? earliest)> GetGlobalStatsAsync(DateTime? since, string userId)
    {
        var query = AsQueryable().Where(s => s.UserId == userId && !s.IsDelete);
        if (since.HasValue)
            query = query.Where(s => s.CreateTime >= since.Value);

        var list = await query.ToListAsync();
        return (list.Count,
                list.Sum(s => s.MessageCount),
                list.Sum(s => (long)s.TotalTokens),
                list.Count > 0 ? list.Min(s => s.CreateTime) : null);
    }
}

/// <summary>
/// Session 消息仓储
/// </summary>
public class SessionMessageRepository : BaseRepository<DbSessionMessage>
{
    /// <summary>
    /// 创建 Session 消息仓储实例。
    /// </summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户</param>
    public SessionMessageRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>
    /// 获取会话的第一条用户消息预览（用于列表展示）
    /// </summary>
    public async Task<Dictionary<string, string>> GetFirstUserMessagePreviewAsync(IEnumerable<string> sessionIds, int maxLength = 60)
    {
        var ids = sessionIds.ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var messages = await Context.Queryable<DbSessionMessage>()
            .Where(m => ids.Contains(m.SessionId) && m.Role == "user" && !m.IsDelete)
            .OrderBy(m => m.CreateTime)
            .Select(m => new { m.SessionId, m.Content })
            .ToListAsync();

        var result = new Dictionary<string, string>();
        foreach (var msg in messages)
        {
            if (!result.ContainsKey(msg.SessionId))
            {
                var preview = msg.Content.Length > maxLength
                    ? msg.Content.Substring(0, maxLength) + "..."
                    : msg.Content;
                preview = preview.Replace("\n", " ").Replace("\r", "");
                result[msg.SessionId] = preview;
            }
        }
        return result;
    }

    /// <summary>
    /// 获取会话消息
    /// </summary>
    public async Task<List<DbSessionMessage>> GetSessionMessagesAsync(string sessionId, int? limit = null)
    {
        var query = AsQueryable()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreateTime, OrderByType.Asc);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// 获取会话最近 N 条消息（时间倒序取 N 条后反转为正序）。
    /// </summary>
    public async Task<List<DbSessionMessage>> GetLatestMessagesAsync(string sessionId, int count)
    {
        var messages = await AsQueryable()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreateTime, OrderByType.Desc)
            .Take(count)
            .ToListAsync();

        messages.Reverse();
        return messages;
    }

    /// <summary>
    /// 清除会话消息
    /// </summary>
    public async Task ClearMessagesAsync(string sessionId)
    {
        await DeleteAsync(m => m.SessionId == sessionId);
    }

    /// <summary>
    /// 获取会话消息统计
    /// </summary>
    public async Task<(int total, int userMsgs, int assistantMsgs, int totalTokens)> GetStatsAsync(string sessionId)
    {
        var stats = await AsQueryable()
            .Where(m => m.SessionId == sessionId)
            .GroupBy(m => m.Role)
            .Select(m => new
            {
                Role = m.Role,
                Count = SqlFunc.AggregateCount(m.Id),
                Tokens = SqlFunc.AggregateSum(m.Tokens ?? 0)
            })
            .ToListAsync();

        int total = 0;
        int userMsgs = 0;
        int assistantMsgs = 0;
        int totalTokens = 0;

        foreach (var stat in stats)
        {
            totalTokens += stat.Tokens;

            if (stat.Role == "summary")
                continue;   // 摘要不计入消息数

            total += stat.Count;
            if (stat.Role == "user")
                userMsgs = stat.Count;
            else if (stat.Role == "assistant")
                assistantMsgs = stat.Count;
        }

        return (total, userMsgs, assistantMsgs, totalTokens);
    }

    /// <summary>
    /// 获取会话活跃消息（未压缩，含摘要消息）
    /// </summary>
    public async Task<List<DbSessionMessage>> GetActiveMessagesAsync(string sessionId)
    {
        return await AsQueryable()
            .Where(m => m.SessionId == sessionId && !m.IsCompacted)
            .OrderBy(m => m.CreateTime, OrderByType.Asc)
            .ToListAsync();
    }

    /// <summary>
    /// 标记消息为已压缩
    /// </summary>
    public async Task MarkCompactedAsync(string sessionId, IEnumerable<long> ids)
    {
        await Context.Updateable<DbSessionMessage>()
            .SetColumns(m => m.IsCompacted == true)
            .Where(m => m.SessionId == sessionId && ids.Contains(m.Id))
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 按会话ID集合物理删除消息（限当前用户的会话集合）
    /// </summary>
    public async Task DeleteBySessionIdsAsync(IEnumerable<string> sessionIds)
    {
        var ids = sessionIds.ToList();
        if (ids.Count == 0) return;

        await Context.Deleteable<DbSessionMessage>()
            .Where(m => ids.Contains(m.SessionId))
            .ExecuteCommandAsync();
    }
}