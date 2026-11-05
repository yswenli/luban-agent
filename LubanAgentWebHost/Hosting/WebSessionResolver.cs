/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebSessionResolver
*描述：解析一轮对话实际使用的会话（显式指定 / 复用最后一次 / 新建）
*
*****************************************************************************/

using LuBan.AIAgent.Sessions;

using LubanAgentCore.Repositories;
using LubanAgentCore.Services;

namespace LubanAgentWebHost.Hosting;

/// <summary>会话解析结果。</summary>
/// <param name="Ok">是否解析成功。</param>
/// <param name="Code">失败时的 HTTP 状态码口径。</param>
/// <param name="Error">失败文案。</param>
/// <param name="SessionId">实际使用的会话标识。</param>
/// <param name="Created">是否为本轮新建。</param>
public sealed record SessionResolution(bool Ok, int Code, string? Error, string SessionId, bool Created)
{
    /// <summary>成功结果。</summary>
    public static SessionResolution Success(string sessionId, bool created) => new(true, 200, null, sessionId, created);

    /// <summary>失败结果。</summary>
    public static SessionResolution Fail(int code, string error) => new(false, code, error, "", false);
}

/// <summary>
/// 解析一轮对话使用的会话。
/// </summary>
/// <remarks>
/// 未指定会话时复用该用户最近活跃的会话；一个都没有时新建并标记 <c>created</c>，
/// 不静默新建替代「指定但不存在」的会话（后者按 404 处理）。
/// </remarks>
public static class WebSessionResolver
{
    /// <summary>会话不存在。</summary>
    public const string SessionNotFound = "会话不存在或已过期";

    /// <summary>会话不属于当前用户。</summary>
    public const string SessionForbidden = "无权访问该会话";

    /// <summary>解析会话。</summary>
    /// <param name="services">请求作用域容器。</param>
    /// <param name="userId">宿主身份。</param>
    /// <param name="requestedSessionId">调用方指定的会话；空白表示复用最后一次。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析结果。</returns>
    public static async Task<SessionResolution> ResolveAsync(
        DiContainer services,
        string userId,
        string? requestedSessionId,
        CancellationToken ct = default)
    {
        var sessions = services.GetRequiredService<ISessionManager>();

        if (!string.IsNullOrWhiteSpace(requestedSessionId))
        {
            var sessionId = requestedSessionId.Trim();
            var owner = await services.GetRequiredService<SessionRepository>()
                .GetOwnerUserIdAsync(sessionId)
                .ConfigureAwait(false);

            if (owner is null)
            {
                return SessionResolution.Fail(404, SessionNotFound);
            }

            if (!string.Equals(owner, userId, StringComparison.Ordinal))
            {
                return SessionResolution.Fail(403, SessionForbidden);
            }

            return SessionResolution.Success(sessionId, false);
        }

        var latest = (await sessions.GetUserSessionsAsync().ConfigureAwait(false))
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .FirstOrDefault();

        if (latest is not null)
        {
            return SessionResolution.Success(latest.SessionId, false);
        }

        var workspaceId = services.GetRequiredService<IWorkspaceManager>().CurrentWorkspace?.WorkspaceId;
        var created = await sessions.CreateSessionAsync(null, workspaceId).ConfigureAwait(false);
        return SessionResolution.Success(created.SessionId, true);
    }
}