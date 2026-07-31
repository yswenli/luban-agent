/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
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
using LubanAgent.Entities;
using LuBan.Orm;

namespace LubanAgent.Repositories;

/// <summary>
/// 工作区仓储
/// </summary>
public class WorkspaceRepository : BaseRepository<DbWorkspace>
{
    public WorkspaceRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>
    /// 按根路径查找未删除的工作区
    /// </summary>
    public async Task<DbWorkspace?> GetByRootPathAsync(string rootPath)
    {
        return await GetFirstAsync(w => w.RootPath == rootPath && !w.IsDelete);
    }

    /// <summary>
    /// 按工作区ID查找
    /// </summary>
    public async Task<DbWorkspace?> GetByWorkspaceIdAsync(string workspaceId)
    {
        return await GetFirstAsync(w => w.WorkspaceId == workspaceId && !w.IsDelete);
    }

    /// <summary>
    /// 按名称模糊查找
    /// </summary>
    public async Task<List<DbWorkspace>> SearchByNameAsync(string name)
    {
        return await AsQueryable()
            .Where(w => w.Name.Contains(name) && !w.IsDelete)
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
    /// 获取所有未删除的工作区
    /// </summary>
    public async Task<List<DbWorkspace>> GetAllAsync()
    {
        return await AsQueryable()
            .Where(w => !w.IsDelete)
            .OrderByDescending(w => w.LastActiveAt)
            .ToListAsync();
    }

    /// <summary>
    /// 更新授权状态
    /// </summary>
    public async Task UpdateAuthorizationAsync(string workspaceId, bool authorized)
    {
        await UpdateAsync(w => new DbWorkspace { IsAuthorized = authorized, UpdateTime = DateTime.Now },
            w => w.WorkspaceId == workspaceId);
    }

    /// <summary>
    /// 更新最后活跃时间
    /// </summary>
    public async Task UpdateLastActiveAtAsync(string workspaceId)
    {
        await UpdateAsync(w => new DbWorkspace { LastActiveAt = DateTime.Now, UpdateTime = DateTime.Now },
            w => w.WorkspaceId == workspaceId);
    }
}
