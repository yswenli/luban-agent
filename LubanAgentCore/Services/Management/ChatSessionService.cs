/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： ChatSessionService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：会话管理实现：转发 <see cref="ISessionManager"/>。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：会话管理实现：转发 <see cref="ISessionManager"/>。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 会话管理实现：转发 <see cref="ISessionManager"/>。
/// </summary>
/// <remarks>
/// 工作区过滤走 <see cref="SessionRepository"/>：<see cref="ISessionManager"/> 的
/// <c>GetUserSessionsAsync</c> 不区分工作区，而 <c>/session -list</c> 的既有语义是「当前工作区内的会话」。
/// 首条用户消息预览与「当前会话」标记仍由命令层/界面自行读取，不进服务。
/// </remarks>
public sealed class ChatSessionService : IChatSessionService
{
    private readonly ISessionManager _sessions;
    private readonly ISessionWorkspaceQuery _sessionQuery;
    private readonly string _userId;

    /// <summary>创建会话管理服务。</summary>
    /// <param name="sessions">会话管理器（按用户绑定）。</param>
    /// <param name="sessionQuery">会话仓储缝（按工作区查询）。</param>
    /// <param name="userContext">当前用户上下文。</param>
    public ChatSessionService(ISessionManager sessions, ISessionWorkspaceQuery sessionQuery, IUserContext userContext)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _sessionQuery = sessionQuery ?? throw new ArgumentNullException(nameof(sessionQuery));
        _userId = userContext.ResolveUserKey();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionInfo>> ListAsync(string? workspaceId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(workspaceId))
        {
            var all = await _sessions.GetUserSessionsAsync();
            return all.ToList();
        }

        var sessions = await _sessionQuery.GetByWorkspaceAsync(workspaceId, _userId);
        return sessions.Select(ToSessionInfo).ToList();
    }

    /// <inheritdoc />
    public Task<SessionInfo> CreateAsync(string? title, string? workspaceId, CancellationToken ct = default)
        => _sessions.CreateSessionAsync(title, workspaceId);

    /// <inheritdoc />
    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
        => _sessions.DeleteSessionAsync(sessionId);

    /// <inheritdoc />
    public Task RenameAsync(string sessionId, string title, CancellationToken ct = default)
        => _sessions.UpdateSessionTitleAsync(sessionId, title);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionMessage>> GetMessagesAsync(string sessionId, CancellationToken ct = default)
    {
        var messages = await _sessions.GetMessagesAsync(sessionId);
        return messages.ToList();
    }

    /// <inheritdoc />
    public Task ClearMessagesAsync(string sessionId, CancellationToken ct = default)
        => _sessions.ClearMessagesAsync(sessionId);

    /// <inheritdoc />
    public Task SwitchAsync(string sessionId, CancellationToken ct = default)
        => _sessions.SetCurrentSessionAsync(sessionId);

    /// <inheritdoc />
    public Task ClearAllAsync(string? workspaceId = null, CancellationToken ct = default)
        => _sessions.ClearAllSessionsAsync(workspaceId);

    private static SessionInfo ToSessionInfo(DbSession session) => new()
    {
        SessionId = session.SessionId,
        UserId = session.UserId,
        Title = session.Title,
        CreatedAt = session.CreateTime,
        UpdatedAt = session.UpdateTime,
        MessageCount = session.MessageCount,
        TotalTokens = session.TotalTokens
    };
}