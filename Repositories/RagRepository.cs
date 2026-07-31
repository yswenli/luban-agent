/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Repositories
*文件名： RagFileRepository
*版本号： V1.0.0.0
*唯一标识：RAG 文件仓储
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：RAG 文件仓储
*
*****************************************************************************/
namespace LubanAgent.Repositories;

/// <summary>
/// RAG 文件仓储
/// </summary>
public class RagFileRepository : BaseRepository<DbRagFile>
{
    public RagFileRepository(long tenantId = LuBanOrmConst.DefaultTenantId) : base(tenantId) { }

    /// <summary>
    /// 按文件路径和工作区查找
    /// </summary>
    public async Task<DbRagFile?> GetByFilePathAsync(string filePath, string workspaceId)
    {
        return await GetFirstAsync(f => f.FilePath == filePath && f.WorkspaceId == workspaceId && !f.IsDelete);
    }

    /// <summary>
    /// 获取工作区的所有已索引文件
    /// </summary>
    public async Task<List<DbRagFile>> GetByWorkspaceAsync(string workspaceId)
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
/// RAG 切块仓储
/// </summary>
public class RagChunkRepository : BaseRepository<DbRagChunk>
{
    public RagChunkRepository(long tenantId = LuBanOrmConst.DefaultTenantId) : base(tenantId) { }

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
        await UpdateAsync(c => new DbRagChunk { IsDelete = true, UpdateTime = DateTime.Now }, c => c.FileId == fileId);
    }

    /// <summary>
    /// 获取工作区切块数量
    /// </summary>
    public async Task<int> CountByWorkspaceAsync(string workspaceId)
    {
        return await AsQueryable().Where(c => c.WorkspaceId == workspaceId && !c.IsDelete).CountAsync();
    }
}
