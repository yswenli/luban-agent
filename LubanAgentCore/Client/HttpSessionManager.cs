/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： HttpSessionManager
*描述：ISessionManager 的 HTTP 实现（数据在 WebHost 侧库中，本地只保留「活动会话」指针）
*
*****************************************************************************/

namespace LubanAgentCore.Client;

/// <summary>
/// 会话管理的 HTTP 实现。
/// </summary>
/// <remarks>
/// <para>
/// 会话数据全部由 WebHost 读写，本地只镜像「当前活动会话」与客户端会话列表，
/// 避免两个进程各自持有会话缓存导致 UI 与实际数据不一致。
/// </para>
/// <para>
/// 归属校验（不存在 404 / 不属于本用户 403）由服务端完成，异常以
/// <see cref="AgentServerException"/> 抛出；<see cref="GetSessionAsync"/> 按接口约定把 404 回落为 null。
/// </para>
/// </remarks>
public sealed class HttpSessionManager(AgentServerClient client) : ISessionManager, ICurrentSessionNotifier
{
    private readonly Lock _gate = new();
    private SessionInfo? _current;

    /// <summary>当前活动会话变更（服务端切换、创建或回填时触发）。</summary>
    public event Action<string>? CurrentSessionChanged;

    /// <inheritdoc />
    public SessionInfo? CurrentSession
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public async Task<SessionInfo> CreateSessionAsync(string? title = null, string? workspaceId = null)
    {
        var session = await client.PostAsync<SessionInfo>(
            AgentApiRoutes.Session.Create,
            new SessionCreateRequest { Title = title, WorkspaceId = workspaceId })
            .ConfigureAwait(false);

        SetCurrent(session);
        return session;
    }

    /// <inheritdoc />
    public async Task<SessionInfo?> GetSessionAsync(string sessionId)
    {
        try
        {
            return await client.GetOrNullAsync<SessionInfo>(
                AgentApiRoutes.Session.Get,
                new Dictionary<string, string?> { ["sessionId"] = sessionId })
                .ConfigureAwait(false);
        }
        catch (AgentServerException ex) when (ex.IsNotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SessionInfo>> GetUserSessionsAsync()
        => await client.GetAsync<List<SessionInfo>>(AgentApiRoutes.Session.List).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task UpdateSessionTitleAsync(string sessionId, string title)
    {
        await client.PostAsync(
            AgentApiRoutes.Session.Rename,
            new SessionRenameRequest { SessionId = sessionId, Title = title })
            .ConfigureAwait(false);

        lock (_gate)
        {
            if (_current?.SessionId == sessionId)
            {
                _current.Title = title;
            }
        }
    }

    /// <inheritdoc />
    public async Task DeleteSessionAsync(string sessionId)
    {
        await client.PostAsync(
            AgentApiRoutes.Session.Delete,
            new SessionIdRequest { SessionId = sessionId })
            .ConfigureAwait(false);

        lock (_gate)
        {
            if (_current?.SessionId == sessionId)
            {
                _current = null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<SessionMessage> AddMessageAsync(
        string sessionId,
        string role,
        string content,
        int? tokens = null,
        string? thinking = null,
        string? attachments = null,
        string? toolCalls = null)
        => await client.PostAsync<SessionMessage>(
            AgentApiRoutes.Session.AddMessage,
            new SessionAddMessageRequest
            {
                SessionId = sessionId,
                Role = role,
                Content = content,
                Tokens = tokens,
                Thinking = thinking,
                Attachments = attachments,
                ToolCalls = toolCalls
            })
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IEnumerable<SessionMessage>> GetMessagesAsync(string sessionId, int? limit = null)
        => await client.GetAsync<List<SessionMessage>>(
            AgentApiRoutes.Session.Messages,
            new Dictionary<string, string?>
            {
                ["sessionId"] = sessionId,
                ["limit"] = limit?.ToString()
            })
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IEnumerable<SessionMessage>> GetLatestMessagesAsync(string sessionId, int count)
        => await client.GetAsync<List<SessionMessage>>(
            AgentApiRoutes.Session.LatestMessages,
            new Dictionary<string, string?>
            {
                ["sessionId"] = sessionId,
                ["count"] = count.ToString()
            })
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task ClearMessagesAsync(string sessionId)
        => await client.PostAsync(
            AgentApiRoutes.Session.ClearMessages,
            new SessionIdRequest { SessionId = sessionId })
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<SessionStats> GetSessionStatsAsync(string sessionId)
        => await client.GetAsync<SessionStats>(
            AgentApiRoutes.Session.Stats,
            new Dictionary<string, string?> { ["sessionId"] = sessionId })
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SetCurrentSessionAsync(string sessionId)
    {
        await client.PostAsync(
            AgentApiRoutes.Session.Switch,
            new SessionIdRequest { SessionId = sessionId })
            .ConfigureAwait(false);

        var session = await GetSessionAsync(sessionId).ConfigureAwait(false);
        SetCurrent(session ?? new SessionInfo { SessionId = sessionId });
    }

    /// <inheritdoc />
    public void ClearCurrentSession()
    {
        lock (_gate)
        {
            _current = null;
        }
    }

    /// <inheritdoc />
    public async Task ClearAllSessionsAsync(string? workspaceId = null)
    {
        await client.PostAsync(
            AgentApiRoutes.Session.ClearAll,
            new SessionCreateRequest { WorkspaceId = workspaceId })
            .ConfigureAwait(false);

        ClearCurrentSession();
    }

    /// <inheritdoc />
    public async Task<GlobalSessionStats> GetGlobalStatsAsync(int? days = null)
        => await client.GetAsync<GlobalSessionStats>(
            AgentApiRoutes.Session.GlobalStats,
            new Dictionary<string, string?> { ["days"] = days?.ToString() })
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IEnumerable<SessionMessage>> GetActiveMessagesAsync(string sessionId)
        => await client.GetAsync<List<SessionMessage>>(
            AgentApiRoutes.Session.ActiveMessages,
            new Dictionary<string, string?> { ["sessionId"] = sessionId })
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task MarkMessagesCompactedAsync(string sessionId, IEnumerable<long> messageIds)
    {
        var ids = messageIds as IReadOnlyList<long> ?? messageIds.ToList();
        if (ids.Count == 0)
        {
            return;
        }

        await client.PostAsync(
            AgentApiRoutes.Session.MarkCompacted,
            new SessionCompactionRequest { SessionId = sessionId, MessageIds = ids })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 由宿主按 SSE 会话帧回填活动会话（服务端已确定实际使用的会话）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    internal void SetCurrentFromRemote(string sessionId)
    {
        bool changed;
        lock (_gate)
        {
            changed = _current?.SessionId != sessionId;
            if (changed)
            {
                _current = new SessionInfo { SessionId = sessionId };
            }
        }

        if (changed)
        {
            CurrentSessionChanged?.Invoke(sessionId);
        }
    }

    /// <summary>
    /// 从服务端拉取当前活动会话（工作区切换后服务端已恢复/新建会话，本地镜像需同步）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>服务端的当前会话；未设置时为 null。</returns>
    public async Task<SessionInfo?> RefreshCurrentAsync(CancellationToken ct = default)
    {
        var session = await client
            .GetOrNullAsync<SessionInfo>(AgentApiRoutes.Session.Current, null, ct)
            .ConfigureAwait(false);

        string? previousId;
        lock (_gate)
        {
            previousId = _current?.SessionId;
            _current = session;
        }

        if (session is not null && !string.Equals(previousId, session.SessionId, StringComparison.Ordinal))
        {
            CurrentSessionChanged?.Invoke(session.SessionId);
        }

        return session;
    }

    private void SetCurrent(SessionInfo session)
    {
        lock (_gate)
        {
            _current = session;
        }

        CurrentSessionChanged?.Invoke(session.SessionId);
    }
}