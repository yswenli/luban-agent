/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Repositories
*文件名： LlmWikiFileRepository
*版本号： V1.0.0.0
*唯一标识：LLM Wiki 文件仓储
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：LLM Wiki 文件仓储
*
*****************************************************************************/
namespace LubanAgentCore.Repositories;

/// <summary>
/// LLM Wiki 文件仓储
/// </summary>
public class LlmWikiFileRepository : BaseRepository<DbLlmWikiFile>
{
    /// <summary>
    /// 创建 LLM Wiki 文件仓储实例。
    /// </summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户</param>
    public LlmWikiFileRepository(long tenantId = LuBanOrmConst.DefaultTenantId) : base(tenantId) { }

    /// <summary>
    /// 按文件路径和工作区查找
    /// </summary>
    public async Task<DbLlmWikiFile?> GetByFilePathAsync(string filePath, string workspaceId)
    {
        return await GetFirstAsync(f => f.FilePath == filePath && f.WorkspaceId == workspaceId && !f.IsDelete);
    }

    /// <summary>
    /// 获取工作区的所有已索引文件
    /// </summary>
    public async Task<List<DbLlmWikiFile>> GetByWorkspaceAsync(string workspaceId)
    {
        return await AsQueryable()
            .Where(f => f.WorkspaceId == workspaceId && !f.IsDelete)
            .OrderByDescending(f => f.IndexedTime)
            .ToListAsync();
    }

    /// <summary>
    /// 物理删除工作区的所有文件索引
    /// </summary>
    public async Task DeleteByWorkspaceAsync(string workspaceId)
    {
        await DeleteAsync(f => f.WorkspaceId == workspaceId);
    }
}

/// <summary>
/// LLM Wiki 切块仓储
/// </summary>
public class LlmWikiChunkRepository : BaseRepository<DbLlmWikiChunk>
{
    /// <summary>
    /// 创建 LLM Wiki 切块仓储实例。
    /// </summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户</param>
    public LlmWikiChunkRepository(long tenantId = LuBanOrmConst.DefaultTenantId) : base(tenantId) { }

    /// <summary>
    /// 物理删除工作区的所有切块
    /// </summary>
    public async Task DeleteByWorkspaceAsync(string workspaceId)
    {
        await DeleteAsync(c => c.WorkspaceId == workspaceId);
    }

    /// <summary>
    /// 软删除指定文件的所有切块
    /// </summary>
    public async Task SoftDeleteByFileAsync(long fileId)
    {
        await UpdateAsync(c => new DbLlmWikiChunk { IsDelete = true, UpdateTime = DateTime.Now }, c => c.FileId == fileId);
    }

    /// <summary>
    /// 获取工作区切块数量
    /// </summary>
    public async Task<int> CountByWorkspaceAsync(string workspaceId)
    {
        return await AsQueryable().Where(c => c.WorkspaceId == workspaceId && !c.IsDelete).CountAsync();
    }
}