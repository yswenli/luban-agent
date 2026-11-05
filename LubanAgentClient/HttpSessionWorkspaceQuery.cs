/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Client
*文件名： HttpSessionWorkspaceQuery
*描述：ISessionWorkspaceQuery 的 HTTP 实现（会话与工作区的归属关系以服务端为准）
*
*****************************************************************************/

using LubanAgentCore.Entities;
using LubanAgentCore.Repositories;

namespace LubanAgent.Client;

/// <summary>
/// 会话工作区查询的 HTTP 实现。
/// </summary>
/// <remarks>
/// <see cref="GetByWorkspaceAsync"/> 返回的是数据库实体，客户端没有对应实体形态，
/// 故按服务端返回的对外模型重建——调用方只用 <c>SessionId</c> / <c>WorkspaceId</c> /
/// <c>MessageCount</c> / <c>TotalTokens</c> 这类标量字段做聚合与展示。
/// </remarks>
public sealed class HttpSessionWorkspaceQuery(AgentServerClient client) : ISessionWorkspaceQuery
{
    /// <inheritdoc />
    public async Task<List<DbSession>> GetByWorkspaceAsync(string workspaceId, string userId)
    {
        if (string.IsNullOrEmpty(workspaceId))
        {
            return new List<DbSession>();
        }

        var sessions = await client.GetAsync<List<SessionInfo>>(
            AgentApiRoutes.Session.ListByWorkspace,
            new Dictionary<string, string?> { ["workspaceId"] = workspaceId })
            .ConfigureAwait(false);

        return sessions.Select(session => new DbSession
        {
            SessionId = session.SessionId,
            UserId = session.UserId,
            Title = session.Title,
            CreateTime = session.CreatedAt,
            UpdateTime = session.UpdatedAt,
            MessageCount = session.MessageCount,
            TotalTokens = session.TotalTokens,
            WorkspaceId = workspaceId,
            // Id 在本地库是自增主键，远程没有对应值：用会话标识的进程内稳定哈希填充，
            // 保证同一会话多次查询取值一致（列表下标会随排序变化，不能当主键）
            Id = session.SessionId.GetHashCode(StringComparison.Ordinal)
        }).ToList();
    }

    /// <inheritdoc />
    public Task<string?> GetSessionWorkspaceIdAsync(string sessionId, string userId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return Task.FromResult<string?>(null);
        }

        return client.GetOrNullAsync<string>(
            AgentApiRoutes.Session.WorkspaceId,
            new Dictionary<string, string?> { ["sessionId"] = sessionId });
    }
}