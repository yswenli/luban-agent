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
}

/// <summary>
/// RAG 切块仓储
/// </summary>
public class RagChunkRepository : BaseRepository<DbRagChunk>
{
    public RagChunkRepository(long tenantId = LuBanOrmConst.DefaultTenantId) : base(tenantId) { }
}
