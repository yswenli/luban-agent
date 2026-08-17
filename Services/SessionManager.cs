/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： SessionManager
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：Session 管理服务实现
*
*****************************************************************************/
namespace LubanAgentCli.Services;

/// <summary>
/// Session 管理服务实现 - 使用 SQLite 数据库
/// </summary>
public class SessionManager : ISessionManager
{
    private readonly SessionRepository _sessionRepo;
    private readonly SessionMessageRepository _messageRepo;

    /// <summary>
    /// 当前活动会话
    /// </summary>
    private SessionInfo? _currentSession;

    /// <summary>
    /// 创建 SessionManager 实例
    /// </summary>
    /// <remarks>
    /// 工作区绑定通过 <see cref="WorkspaceManager.Current"/> 静态访问器在
    /// <see cref="CreateSessionAsync"/> 中完成，无需构造函数注入。
    /// </remarks>
    public SessionManager()
    {
        _sessionRepo = new SessionRepository();
        _messageRepo = new SessionMessageRepository();
    }

    /// <summary>
    /// 当前活动会话
    /// </summary>
    public SessionInfo? CurrentSession => _currentSession;

    /// <summary>当前活动会话变更时触发。</summary>
    public event Action<string>? CurrentSessionChanged;

    /// <summary>
    /// 创建新会话（自动绑定当前工作区，若存在）
    /// </summary>
    public async Task<SessionInfo> CreateSessionAsync(string? userId = null, string? title = null)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new DbSession
        {
            SessionId = sessionId,
            UserId = userId,
            Title = title ?? "新对话",
            CreateTime = DateTime.Now,
            // 同步 UpdateTime，避免 GetLatestSessionAsync 排序时多个 NULL 并列的不确定性
            UpdateTime = DateTime.Now,
            IsDelete = false,
            // 绑定当前工作区（通过 WorkspaceManager 静态访问器，避免循环依赖）
            WorkspaceId = WorkspaceManager.Current?.WorkspaceId
        };

        await _sessionRepo.InsertAsync(session);

        _currentSession = ToSessionInfo(session);
        return _currentSession;
    }

    /// <summary>
    /// 获取会话
    /// </summary>
    public async Task<SessionInfo?> GetSessionAsync(string sessionId)
    {
        var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
        if (session == null)
            return null;

        return ToSessionInfo(session);
    }

    /// <summary>
    /// 获取用户的所有会话
    /// </summary>
    public async Task<IEnumerable<SessionInfo>> GetUserSessionsAsync(string userId)
    {
        var sessions = await _sessionRepo.GetUserSessionsAsync(userId);
        return sessions.Select(ToSessionInfo);
    }

    /// <summary>
    /// 更新会话标题
    /// </summary>
    public async Task UpdateSessionTitleAsync(string sessionId, string title)
    {
        await _sessionRepo.UpdateTitleAsync(sessionId, title);

        if (_currentSession?.SessionId == sessionId)
        {
            _currentSession.Title = title;
        }
    }

    /// <summary>
    /// 删除会话
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId)
    {
        await _sessionRepo.SoftDeleteAsync(sessionId);
        await _messageRepo.ClearMessagesAsync(sessionId);

        if (_currentSession?.SessionId == sessionId)
        {
            _currentSession = null;
        }
    }

    /// <summary>
    /// 添加消息到会话
    /// </summary>
    public async Task<SessionMessage> AddMessageAsync(string sessionId, string role, string content, int? tokens = null)
    {
        var message = new DbSessionMessage
        {
            SessionId = sessionId,
            Role = role,
            Content = content,
            Tokens = tokens,
            CreateTime = DateTime.Now,
            IsDelete = false
        };

        var id = await _messageRepo.InsertReturnIdentityAsync(message);
        message.Id = id;

        if (role == "summary")
        {
            // 摘要消息：token 计入，消息数不计
            await _sessionRepo.IncrementTokenCountAsync(sessionId, tokens ?? 0);
            if (_currentSession?.SessionId == sessionId)
            {
                _currentSession.TotalTokens += tokens ?? 0;
            }
        }
        else
        {
            await _sessionRepo.IncrementMessageCountAsync(sessionId, tokens ?? 0);
            if (_currentSession?.SessionId == sessionId)
            {
                _currentSession.MessageCount++;
                _currentSession.TotalTokens += tokens ?? 0;
            }
        }

        return ToSessionMessage(message);
    }

    /// <summary>
    /// 获取会话消息
    /// </summary>
    public async Task<IEnumerable<SessionMessage>> GetMessagesAsync(string sessionId, int? limit = null)
    {
        var messages = await _messageRepo.GetSessionMessagesAsync(sessionId, limit);
        return messages.Select(ToSessionMessage);
    }

    /// <summary>
    /// 获取会话最近 N 条消息。
    /// </summary>
    public async Task<IEnumerable<SessionMessage>> GetLatestMessagesAsync(string sessionId, int count)
    {
        var messages = await _messageRepo.GetLatestMessagesAsync(sessionId, count);
        return messages.Select(ToSessionMessage);
    }

    /// <summary>
    /// 清除会话消息
    /// </summary>
    public async Task ClearMessagesAsync(string sessionId)
    {
        await _messageRepo.ClearMessagesAsync(sessionId);

        if (_currentSession?.SessionId == sessionId)
        {
            _currentSession.MessageCount = 0;
            _currentSession.TotalTokens = 0;
        }
    }

    /// <summary>
    /// 获取会话统计信息
    /// </summary>
    public async Task<SessionStats> GetSessionStatsAsync(string sessionId)
    {
        var (total, userMsgs, assistantMsgs, totalTokens) = await _messageRepo.GetStatsAsync(sessionId);

        return new SessionStats
        {
            TotalMessages = total,
            UserMessages = userMsgs,
            AssistantMessages = assistantMsgs,
            TotalTokens = totalTokens,
            AverageMessageLength = total > 0 ? totalTokens / (double)total : 0
        };
    }

    /// <summary>
    /// 设置当前活动会话
    /// </summary>
    public async Task SetCurrentSessionAsync(string sessionId)
    {
        _currentSession = await GetSessionAsync(sessionId);
        CurrentSessionChanged?.Invoke(sessionId);
    }

    /// <summary>
    /// 清除当前会话（切换到无会话状态）
    /// </summary>
    public void ClearCurrentSession()
    {
        _currentSession = null;
    }

    /// <summary>
    /// 物理删除全部会话及消息数据
    /// </summary>
    public async Task ClearAllSessionsAsync()
    {
        await _messageRepo.DeleteAllAsync();
        await _sessionRepo.DeleteAllAsync();
        _currentSession = null;

        // 保持"有当前工作区必有当前会话"不变量：清空后立即重建默认会话，
        // 否则对话持久化前提（CurrentSession 非空）被破坏，每轮对话再次孤立
        if (WorkspaceManager.Current is not null)
        {
            await CreateSessionAsync(userId: "default", title: "默认会话");
        }
    }

    /// <summary>
    /// 获取全局会话统计
    /// </summary>
    public async Task<GlobalSessionStats> GetGlobalStatsAsync(int? days = null)
    {
        var since = days.HasValue ? DateTime.Now.AddDays(-days.Value) : (DateTime?)null;
        var (sessions, messages, tokens, earliest) = await _sessionRepo.GetGlobalStatsAsync(since);

        var spanDays = days ?? (earliest.HasValue
            ? Math.Max(1, (int)(DateTime.Now - earliest.Value).TotalDays + 1)
            : 1);

        return new GlobalSessionStats
        {
            TotalSessions = sessions,
            TotalMessages = messages,
            TotalTokens = tokens,
            Days = spanDays,
            AverageDailyTokens = spanDays > 0 ? tokens / (double)spanDays : 0
        };
    }

    /// <summary>
    /// 获取会话的活跃消息（未被压缩的）
    /// </summary>
    public async Task<IEnumerable<SessionMessage>> GetActiveMessagesAsync(string sessionId)
    {
        var messages = await _messageRepo.GetActiveMessagesAsync(sessionId);
        return messages.Select(ToSessionMessage);
    }

    /// <summary>
    /// 将指定消息标记为已压缩
    /// </summary>
    public async Task MarkMessagesCompactedAsync(string sessionId, IEnumerable<long> messageIds)
    {
        await _messageRepo.MarkCompactedAsync(sessionId, messageIds);
    }

    /// <summary>
    /// 转换为 SessionInfo
    /// </summary>
    private static SessionInfo ToSessionInfo(DbSession session)
    {
        return new SessionInfo
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

    /// <summary>
    /// 转换为 SessionMessage
    /// </summary>
    private static SessionMessage ToSessionMessage(DbSessionMessage message)
    {
        return new SessionMessage
        {
            Id = message.Id,
            SessionId = message.SessionId,
            Role = message.Role,
            Content = message.Content,
            Tokens = message.Tokens,
            CreatedAt = message.CreateTime
        };
    }
}