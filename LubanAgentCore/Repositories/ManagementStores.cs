/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Repositories
*文件名： ManagementStores
*版本号： V1.0.0.0
*唯一标识：管理服务用的最小仓储缝
*创建人：yswenli
*创建时间：2026/10/27
*描述：管理服务（工作区/会话/LLM Wiki）所需的仓储能力最小抽象。
*      目的：管理服务只依赖自己真正用到的方法，既可脱离数据库做单测，
*      也避免把整张 ORM 仓储类型泄漏到服务契约里。
*
*****************************************************************************/
namespace LubanAgentCore.Repositories;

/// <summary>
/// 会话的工作区查询（<c>/session -list</c> 与 Codex 会话树的数据源）。
/// </summary>
public interface ISessionWorkspaceQuery
{
    /// <summary>
    /// 获取工作区下指定用户的有效会话（更新时间倒序）。
    /// </summary>
    /// <param name="workspaceId">工作区标识；为空时返回空列表。</param>
    /// <param name="userId">归属用户。</param>
    Task<List<DbSession>> GetByWorkspaceAsync(string workspaceId, string userId);
}

/// <summary>
/// LLM Wiki 文件索引的读取与按工作区清理。
/// </summary>
public interface ILlmWikiFileStore
{
    /// <summary>获取工作区内有效的已索引文件（索引时间倒序）。</summary>
    /// <param name="workspaceId">工作区标识。</param>
    Task<List<DbLlmWikiFile>> GetByWorkspaceAsync(string workspaceId);

    /// <summary>物理删除工作区的全部文件索引。</summary>
    /// <param name="workspaceId">工作区标识。</param>
    Task DeleteByWorkspaceAsync(string workspaceId);
}

/// <summary>
/// LLM Wiki 切块索引的计数与按工作区清理。
/// </summary>
public interface ILlmWikiChunkStore
{
    /// <summary>获取工作区有效切块数量。</summary>
    /// <param name="workspaceId">工作区标识。</param>
    Task<int> CountByWorkspaceAsync(string workspaceId);

    /// <summary>物理删除工作区的全部切块。</summary>
    /// <param name="workspaceId">工作区标识。</param>
    Task DeleteByWorkspaceAsync(string workspaceId);
}