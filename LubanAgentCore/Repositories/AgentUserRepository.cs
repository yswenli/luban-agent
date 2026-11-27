/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Repositories
*文件名： AgentUserRepository
*描述：Web 宿主用户仓储
*
*****************************************************************************/

namespace LubanAgentCore.Repositories;

/// <summary>
/// Web 宿主用户仓储。
/// </summary>
public class AgentUserRepository : AgentRepository<DbAgentUser>
{
    /// <summary>创建仓储。</summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户。</param>
    public AgentUserRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>按用户标识查找（未删除）。</summary>
    /// <param name="userId">用户标识。</param>
    /// <returns>用户；不存在时为 <c>null</c>。</returns>
    public Task<DbAgentUser?> GetByUserIdAsync(string userId)
        => GetFirstAsync(u => u.UserId == userId && !u.IsDelete);

    /// <summary>按用户标识查找（含已删除，仅用于注册时判重）。</summary>
    /// <param name="userId">用户标识。</param>
    /// <returns>用户；不存在时为 <c>null</c>。</returns>
    public Task<DbAgentUser?> GetAnyByUserIdAsync(string userId)
        => GetFirstAsync(u => u.UserId == userId);

    /// <summary>按登录名查找（大小写不敏感，未删除）。</summary>
    /// <param name="userName">登录名。</param>
    /// <returns>用户；不存在时为 <c>null</c>。</returns>
    public Task<DbAgentUser?> GetByUserNameAsync(string userName)
    {
        var normalized = userName.Trim().ToLowerInvariant();
        return GetFirstAsync(u => u.UserId == normalized && !u.IsDelete);
    }

    /// <summary>列出全部未删除用户。</summary>
    /// <returns>用户列表。</returns>
    public Task<List<DbAgentUser>> ListAsync()
        => AsQueryable()
            .Where(u => !u.IsDelete)
            .OrderBy(u => u.CreateTime)
            .ToListAsync();

    /// <summary>记录一次成功登录，同时清零失败计数与锁定状态。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="at">登录时间（UTC）。</param>
    /// <returns>是否更新到行。</returns>
    public Task<bool> TouchLoginAsync(string userId, DateTime at)
        => UpdateAsync(
            u => new DbAgentUser
            {
                LastLoginAt = at,
                FailedLoginCount = 0,
                LockedUntil = null,
                UpdateTime = DateTime.Now
            },
            u => u.UserId == userId && !u.IsDelete);

    /// <summary>失败计数原子加一，并返回加一后的真实值。</summary>
    /// <remarks>
    /// 计数用单条 UPDATE 自增，避免并发登录各读各写漏计；读取回真实值后再由业务层决定是否落锁定。
    /// </remarks>
    /// <param name="userId">用户标识。</param>
    /// <returns>本次失败后的连续失败次数；用户不存在时为 0。</returns>
    public async Task<int> RegisterLoginFailureAsync(string userId)
    {
        await Context.Updateable<DbAgentUser>()
            .SetColumns(u => u.FailedLoginCount == u.FailedLoginCount + 1)
            .SetColumns(u => u.UpdateTime == DateTime.Now)
            .Where(u => u.UserId == userId && !u.IsDelete)
            .ExecuteCommandAsync()
            .ConfigureAwait(false);

        var user = await GetByUserIdAsync(userId).ConfigureAwait(false);
        return user?.FailedLoginCount ?? 0;
    }

    /// <summary>写入失败计数与锁定到期时间。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="failedCount">失败次数。</param>
    /// <param name="lockedUntil">锁定到期时间（UTC）；为空表示未锁定。</param>
    /// <returns>是否更新到行。</returns>
    public Task<bool> SetLoginLockAsync(string userId, int failedCount, DateTime? lockedUntil)
        => UpdateAsync(
            u => new DbAgentUser
            {
                FailedLoginCount = failedCount,
                LockedUntil = lockedUntil,
                UpdateTime = DateTime.Now
            },
            u => u.UserId == userId && !u.IsDelete);

    /// <summary>清零失败计数并解除锁定（管理员解锁、锁定到期后重新计数、改密重置）。</summary>
    /// <param name="userId">用户标识。</param>
    /// <returns>是否更新到行。</returns>
    public Task<bool> ClearLoginFailureAsync(string userId)
        => UpdateAsync(
            u => new DbAgentUser { FailedLoginCount = 0, LockedUntil = null, UpdateTime = DateTime.Now },
            u => u.UserId == userId && !u.IsDelete);

    /// <summary>修改显示名。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="displayName">显示名（非空；空白已在业务层回退登录名）。</param>
    /// <returns>是否更新到行。</returns>
    public Task<bool> UpdateDisplayNameAsync(string userId, string displayName)
        => UpdateAsync(
            u => new DbAgentUser { DisplayName = displayName, UpdateTime = DateTime.Now },
            u => u.UserId == userId && !u.IsDelete);
}