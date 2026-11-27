/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Repositories
*文件名： InviteCodeRepository
*描述：注册邀请码仓储
*
*****************************************************************************/

namespace LubanAgentCore.Repositories;

/// <summary>
/// 注册邀请码仓储。
/// </summary>
public class InviteCodeRepository : AgentRepository<DbInviteCode>
{
    /// <summary>创建仓储。</summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户。</param>
    public InviteCodeRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>按邀请码查找（未删除）。</summary>
    /// <param name="code">邀请码。</param>
    /// <returns>邀请码；不存在时为 <c>null</c>。</returns>
    public Task<DbInviteCode?> GetByCodeAsync(string code)
        => GetFirstAsync(c => c.Code == code && !c.IsDelete);

    /// <summary>列出未删除的邀请码（按创建时间倒序）。</summary>
    /// <returns>邀请码列表。</returns>
    public Task<List<DbInviteCode>> ListAsync()
        => AsQueryable()
            .Where(c => !c.IsDelete)
            .OrderByDescending(c => c.CreateTime)
            .ToListAsync();

    /// <summary>
    /// 原子占用邀请码：只有「未使用、未过期、未删除」时才会成功，避免并发注册重复使用同一码。
    /// </summary>
    /// <param name="code">邀请码。</param>
    /// <param name="userId">使用者用户标识。</param>
    /// <param name="at">使用时间（UTC）。</param>
    /// <returns>占用成功返回 true。</returns>
    public async Task<bool> TryClaimAsync(string code, string userId, DateTime at)
    {
        var rows = await Context.Updateable<DbInviteCode>()
            .SetColumns(c => new DbInviteCode
            {
                UsedBy = userId,
                UsedAt = at,
                UpdateTime = DateTime.Now
            })
            .Where(c => c.Code == code && !c.IsDelete && c.UsedBy == null && c.ExpiresAt > at)
            .ExecuteCommandAsync()
            .ConfigureAwait(false);
        return rows > 0;
    }
}