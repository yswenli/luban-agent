/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： SessionController
*描述：会话接口：列表 / 详情 / 增删改 / 消息 / 统计
*
*****************************************************************************/

using LuBan.AIAgent.Sessions;

using LubanAgent.Protocol.Dtos;

using LubanAgentCore.Repositories;

using LubanAgentWebHost.Hosting;

using SAEA.MVC;

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 会话接口。
/// </summary>
/// <remarks>
/// 会话归属校验统一走 <see cref="SessionRepository.GetOwnerUserIdAsync"/>：
/// 不存在按 404、不属于当前用户按 403，与对话接口同口径。
/// </remarks>
public sealed class SessionController : Controller
{
    /// <summary>当前用户的会话列表。</summary>
    /// <returns>会话列表。</returns>
    [HttpGet]
    public Task<ActionResult> List() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var sessions = await scope.GetRequiredService<ISessionManager>()
            .GetUserSessionsAsync()
            .ConfigureAwait(false);
        return WebApi.OkJson(sessions.ToList());
    });

    /// <summary>指定工作区下的会话列表（更新时间倒序）。</summary>
    /// <param name="workspaceId">工作区标识。</param>
    /// <returns>会话列表。</returns>
    [HttpGet]
    public Task<ActionResult> ListByWorkspace(string? workspaceId) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var sessions = await scope.GetRequiredService<ISessionWorkspaceQuery>()
            .GetByWorkspaceAsync(workspaceId.Trim(), WebApi.Runtime.UserId)
            .ConfigureAwait(false);
        return WebApi.OkJson(sessions.Select(ToSessionInfo).ToList());
    });

    /// <summary>批量取各会话首条用户消息预览。</summary>
    /// <param name="sessionIds">逗号分隔的会话标识。</param>
    /// <param name="maxLength">预览最大长度；缺省 60。</param>
    /// <returns>会话标识到预览文本的映射。</returns>
    [HttpGet]
    public Task<ActionResult> Previews(string? sessionIds, string? maxLength) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        var ids = (sessionIds ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (ids.Count == 0)
        {
            return WebApi.OkJson(new Dictionary<string, string>());
        }

        var take = int.TryParse(maxLength, out var parsed) && parsed > 0 ? parsed : 60;

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);

        // 批量接口按归属过滤：不属于当前用户（或不存在）的会话静默跳过，避免整批失败也避免泄露存在性
        var owned = (await scope.GetRequiredService<ISessionManager>()
                .GetUserSessionsAsync()
                .ConfigureAwait(false))
            .Select(s => s.SessionId)
            .ToHashSet(StringComparer.Ordinal);
        ids = ids.Where(owned.Contains).ToList();
        if (ids.Count == 0)
        {
            return WebApi.OkJson(new Dictionary<string, string>());
        }

        var previews = await scope.GetRequiredService<ISessionMessageQuery>()
            .GetFirstUserMessagePreviewAsync(ids, take)
            .ConfigureAwait(false);
        return WebApi.OkJson(previews);
    });

    /// <summary>按标识取会话。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>会话详情。</returns>
    [HttpGet]
    public Task<ActionResult> Get(string? sessionId) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var session = await scope.GetRequiredService<ISessionManager>()
            .GetSessionAsync(sessionId.Trim())
            .ConfigureAwait(false);
        if (session is null)
        {
            return WebApi.FailJson(404, WebSessionResolver.SessionNotFound);
        }

        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        return WebApi.OkJson(session);
    });

    /// <summary>当前活动会话。</summary>
    /// <returns>会话详情；尚未确定活动会话时 data 为 null。</returns>
    /// <remarks>
    /// 工作区切换后服务端会恢复/新建活动会话，客户端据此刷新本地镜像（远程模式下客户端不持有会话数据）。
    /// </remarks>
    [HttpGet]
    public Task<ActionResult> Current() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        return WebApi.OkJson(scope.GetRequiredService<ISessionManager>().CurrentSession);
    });

    /// <summary>会话绑定的工作区标识。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>工作区标识；会话不存在或未绑定工作区时 data 为 null。</returns>
    [HttpGet]
    public Task<ActionResult> WorkspaceId(string? sessionId) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspaceId = await scope.GetRequiredService<ISessionWorkspaceQuery>()
            .GetSessionWorkspaceIdAsync(sessionId.Trim(), WebApi.Runtime.UserId)
            .ConfigureAwait(false);
        return WebApi.OkJson(workspaceId);
    });

    /// <summary>新建会话。</summary>
    /// <param name="payload">包装后的 <see cref="SessionCreateRequest"/> JSON；可为空。</param>
    /// <returns>新会话。</returns>
    [HttpPost]
    public Task<ActionResult> Create(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        var input = WebApi.TryReadPayload<SessionCreateRequest>(payload, out var parsed) ? parsed : null;

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspaceId = string.IsNullOrWhiteSpace(input?.WorkspaceId)
            ? scope.GetRequiredService<IWorkspaceManager>().CurrentWorkspace?.WorkspaceId
            : input!.WorkspaceId.Trim();

        var session = await scope.GetRequiredService<ISessionManager>()
            .CreateSessionAsync(string.IsNullOrWhiteSpace(input?.Title) ? null : input!.Title!.Trim(), workspaceId)
            .ConfigureAwait(false);
        return WebApi.OkJson(session);
    });

    /// <summary>删除会话。</summary>
    /// <param name="payload">包装后的 <see cref="SessionIdRequest"/> JSON。</param>
    /// <returns>删除结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SessionIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, input!.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        await scope.GetRequiredService<ISessionManager>()
            .DeleteSessionAsync(input.SessionId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new { deleted = true });
    });

    /// <summary>重命名会话。</summary>
    /// <param name="payload">包装后的 <see cref="SessionRenameRequest"/> JSON。</param>
    /// <returns>重命名结果。</returns>
    [HttpPost]
    public Task<ActionResult> Rename(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SessionRenameRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input!.Title))
        {
            return WebApi.FailJson(400, "title 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, input.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        await scope.GetRequiredService<ISessionManager>()
            .UpdateSessionTitleAsync(input.SessionId.Trim(), input.Title.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new { renamed = true });
    });

    /// <summary>会话消息。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="limit">限制条数；缺省取全部。</param>
    /// <returns>消息列表。</returns>
    [HttpGet]
    public Task<ActionResult> Messages(string? sessionId, string? limit) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        int? take = int.TryParse(limit, out var parsed) && parsed > 0 ? parsed : null;
        var messages = await scope.GetRequiredService<ISessionManager>()
            .GetMessagesAsync(sessionId.Trim(), take)
            .ConfigureAwait(false);
        return WebApi.OkJson(messages.ToList());
    });

    /// <summary>会话最近 N 条消息。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="count">条数；缺省 20。</param>
    /// <returns>消息列表（时间正序）。</returns>
    [HttpGet]
    public Task<ActionResult> LatestMessages(string? sessionId, string? count) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var take = int.TryParse(count, out var parsed) && parsed > 0 ? parsed : 20;
        var messages = await scope.GetRequiredService<ISessionManager>()
            .GetLatestMessagesAsync(sessionId.Trim(), take)
            .ConfigureAwait(false);
        return WebApi.OkJson(messages.ToList());
    });

    /// <summary>会话活跃消息（未压缩）。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>消息列表。</returns>
    [HttpGet]
    public Task<ActionResult> ActiveMessages(string? sessionId) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var messages = await scope.GetRequiredService<ISessionManager>()
            .GetActiveMessagesAsync(sessionId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(messages.ToList());
    });

    /// <summary>追加一条消息。</summary>
    /// <param name="payload">包装后的 <see cref="SessionAddMessageRequest"/> JSON。</param>
    /// <returns>新消息。</returns>
    [HttpPost]
    public Task<ActionResult> AddMessage(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SessionAddMessageRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input!.Role))
        {
            return WebApi.FailJson(400, "role 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, input.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var message = await scope.GetRequiredService<ISessionManager>()
            .AddMessageAsync(
                input.SessionId.Trim(),
                input.Role.Trim(),
                input.Content ?? "",
                input.Tokens,
                input.Thinking,
                input.Attachments,
                input.ToolCalls)
            .ConfigureAwait(false);
        return WebApi.OkJson(message);
    });

    /// <summary>标记消息为已压缩。</summary>
    /// <param name="payload">包装后的 <see cref="SessionCompactionRequest"/> JSON。</param>
    /// <returns>标记结果。</returns>
    [HttpPost]
    public Task<ActionResult> MarkCompacted(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SessionCompactionRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, input!.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var ids = input.MessageIds ?? [];
        if (ids.Count > 0)
        {
            await scope.GetRequiredService<ISessionManager>()
                .MarkMessagesCompactedAsync(input.SessionId.Trim(), ids)
                .ConfigureAwait(false);
        }

        return WebApi.OkJson(new { marked = ids.Count });
    });

    /// <summary>会话统计。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>统计信息。</returns>
    [HttpGet]
    public Task<ActionResult> Stats(string? sessionId) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var stats = await scope.GetRequiredService<ISessionManager>()
            .GetSessionStatsAsync(sessionId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(stats);
    });

    /// <summary>全局统计。</summary>
    /// <param name="days">限定最近 N 天；缺省统计全部。</param>
    /// <returns>全局统计。</returns>
    [HttpGet]
    public Task<ActionResult> GlobalStats(string? days) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        int? span = int.TryParse(days, out var parsed) && parsed > 0 ? parsed : null;
        var stats = await scope.GetRequiredService<ISessionManager>()
            .GetGlobalStatsAsync(span)
            .ConfigureAwait(false);
        return WebApi.OkJson(stats);
    });

    /// <summary>清空会话消息。</summary>
    /// <param name="payload">包装后的 <see cref="SessionIdRequest"/> JSON。</param>
    /// <returns>清空结果。</returns>
    [HttpPost]
    public Task<ActionResult> ClearMessages(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SessionIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, input!.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        await scope.GetRequiredService<ISessionManager>()
            .ClearMessagesAsync(input.SessionId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new { cleared = true });
    });

    /// <summary>物理删除全部会话（可重建一个空会话）。</summary>
    /// <param name="payload">包装后的 <see cref="SessionCreateRequest"/> JSON；可为空。</param>
    /// <returns>清空结果。</returns>
    [HttpPost]
    public Task<ActionResult> ClearAll(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        var input = WebApi.TryReadPayload<SessionCreateRequest>(payload, out var parsed) ? parsed : null;

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var manager = scope.GetRequiredService<ISessionManager>();
        var workspaceId = string.IsNullOrWhiteSpace(input?.WorkspaceId)
            ? scope.GetRequiredService<IWorkspaceManager>().CurrentWorkspace?.WorkspaceId
            : input!.WorkspaceId.Trim();

        await manager.ClearAllSessionsAsync(workspaceId).ConfigureAwait(false);
        return WebApi.OkJson(new { cleared = true, current = manager.CurrentSession?.SessionId });
    });

    /// <summary>切换当前会话（仅影响宿主内的「活动会话」指针）。</summary>
    /// <param name="payload">包装后的 <see cref="SessionIdRequest"/> JSON。</param>
    /// <returns>切换结果。</returns>
    [HttpPost]
    public Task<ActionResult> Switch(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SessionIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        if (await CheckOwnerAsync(scope, input!.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        await scope.GetRequiredService<ISessionManager>()
            .SetCurrentSessionAsync(input.SessionId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new { sessionId = input.SessionId.Trim() });
    });

    /// <summary>把数据库会话映射为对外模型。</summary>
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

    /// <summary>校验会话归属；通过返回 null。</summary>
    private static async Task<ActionResult?> CheckOwnerAsync(DiContainer scope, string sessionId)
    {
        var owner = await scope.GetRequiredService<SessionRepository>()
            .GetOwnerUserIdAsync(sessionId.Trim())
            .ConfigureAwait(false);

        if (owner is null)
        {
            return WebApi.FailJson(404, WebSessionResolver.SessionNotFound);
        }

        return string.Equals(owner, WebApi.Runtime.UserId, StringComparison.Ordinal)
            ? null
            : WebApi.FailJson(403, WebSessionResolver.SessionForbidden);
    }
}