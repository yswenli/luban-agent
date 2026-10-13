/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Repositories
*文件名： WorkspaceRepository
*版本号： V1.0.0.0
*唯一标识：工作区仓储
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：工作区仓储
*
*****************************************************************************/
using LubanAgentCore.Infrastructure;

namespace LubanAgentCore.Repositories;

/// <summary>
/// 工作区仓储
/// </summary>
public class WorkspaceRepository : BaseRepository<DbWorkspace>
{
    /// <summary>
    /// 创建工作区仓储实例。
    /// </summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户</param>
    public WorkspaceRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>
    /// 按根路径查找当前用户未删除的工作区（同路径跨用户各持一行，故须按用户过滤）
    /// </summary>
    public async Task<DbWorkspace?> GetByRootPathAsync(string rootPath, string userId)
    {
        // 归一化后再比较：库中 RootPath 已由 WorkspaceManager 归一化写入，
        // 此处同步归一化入参，避免尾斜杠/大小写差异导致漏命中而重复建工作区
        var normalized = WorkspaceIdGenerator.Normalize(rootPath);
        return await GetFirstAsync(w => w.RootPath == normalized && w.UserId == userId && !w.IsDelete);
    }

    /// <summary>
    /// 按工作区ID查找当前用户的工作区
    /// </summary>
    public async Task<DbWorkspace?> GetByWorkspaceIdAsync(string workspaceId, string userId)
    {
        return await GetFirstAsync(w => w.WorkspaceId == workspaceId && w.UserId == userId && !w.IsDelete);
    }

    /// <summary>
    /// 在当前用户工作区内按名称模糊查找
    /// </summary>
    public async Task<List<DbWorkspace>> SearchByNameAsync(string name, string userId)
    {
        return await AsQueryable()
            .Where(w => w.Name.Contains(name) && w.UserId == userId && !w.IsDelete)
            .OrderByDescending(w => w.LastActiveAt)
            .ToListAsync();
    }

    /// <summary>
    /// 获取用户的所有工作区
    /// </summary>
    public async Task<List<DbWorkspace>> GetUserWorkspacesAsync(string userId)
    {
        return await AsQueryable()
            .Where(w => w.UserId == userId && !w.IsDelete)
            .OrderByDescending(w => w.LastActiveAt)
            .ToListAsync();
    }

    /// <summary>
    /// 更新授权状态
    /// </summary>
    public async Task UpdateAuthorizationAsync(string workspaceId, bool authorized, string userId)
    {
        await UpdateAsync(w => new DbWorkspace { IsAuthorized = authorized, UpdateTime = DateTime.Now },
            w => w.WorkspaceId == workspaceId && w.UserId == userId);
    }

    /// <summary>
    /// 更新最后活跃时间
    /// </summary>
    public async Task UpdateLastActiveAtAsync(string workspaceId, string userId)
    {
        await UpdateAsync(w => new DbWorkspace { LastActiveAt = DateTime.Now, UpdateTime = DateTime.Now },
            w => w.WorkspaceId == workspaceId && w.UserId == userId);
    }

    /// <summary>
    /// 更新工作区显示名
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    /// <param name="name">新的显示名</param>
    /// <param name="userId">所属用户</param>
    public async Task UpdateNameAsync(string workspaceId, string name, string userId)
    {
        await UpdateAsync(w => new DbWorkspace { Name = name, UpdateTime = DateTime.Now },
            w => w.WorkspaceId == workspaceId && w.UserId == userId);
    }
}
