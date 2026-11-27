/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebSessionService
*描述：会话业务：列表 / 详情 / 增删改 / 消息 / 统计
*
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Nodes;

namespace LubanAgentWebHost.Services;

/// <summary>
/// 会话业务。
/// </summary>
/// <remarks>
/// 会话归属校验统一走 <see cref="SessionRepository.GetOwnerUserIdAsync"/>：
/// 不存在按 404、不属于当前用户按 403，与对话接口同口径。
/// </remarks>
public static class WebSessionService
{
    /// <summary>当前用户的会话列表。</summary>
    /// <returns>会话列表。</returns>
    public static Task<ActionResult> ListAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var sessions = await scope.GetRequiredService<ISessionManager>()
            .GetUserSessionsAsync()
            .ConfigureAwait(false);
        return WebApi.OkJson(sessions.ToList());
    });

    /// <summary>当前用户的会话摘要列表（含工作区与模型，浏览器端会话列表用）。</summary>
    /// <returns>按更新时间倒序的会话摘要。</returns>
    public static Task<ActionResult> ListDetailedAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var sessions = await scope.GetRequiredService<SessionRepository>()
            .GetUserSessionsAsync(WebApi.UserId)
            .ConfigureAwait(false);

        return WebApi.OkJson(sessions
            .OrderByDescending(s => s.UpdateTime ?? s.CreateTime)
            .Select(s => new SessionSummaryDto(
                s.SessionId,
                s.Title,
                s.CreateTime,
                s.UpdateTime,
                s.MessageCount,
                s.TotalTokens,
                s.WorkspaceId,
                s.Provider,
                s.ModelName))
            .ToList());
    });

    /// <summary>指定工作区下的会话列表（更新时间倒序）。</summary>
    /// <param name="workspaceId">工作区标识。</param>
    /// <returns>会话列表。</returns>
    public static Task<ActionResult> ListByWorkspaceAsync(string? workspaceId) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var sessions = await scope.GetRequiredService<ISessionWorkspaceQuery>()
            .GetByWorkspaceAsync(workspaceId.Trim(), WebApi.UserId)
            .ConfigureAwait(false);
        return WebApi.OkJson(sessions.Select(ToSessionInfo).ToList());
    });

    /// <summary>批量取各会话首条用户消息预览。</summary>
    /// <param name="sessionIds">逗号分隔的会话标识。</param>
    /// <param name="maxLength">预览最大长度；缺省 60。</param>
    /// <returns>会话标识到预览文本的映射。</returns>
    public static Task<ActionResult> PreviewsAsync(string? sessionIds, string? maxLength) => WebApi.RunGuardedAsync(async () =>
    {
        var ids = (sessionIds ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (ids.Count == 0)
        {
            return WebApi.OkJson(new Dictionary<string, string>());
        }

        var take = int.TryParse(maxLength, out var parsed) && parsed > 0 ? parsed : 60;

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);

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
    public static Task<ActionResult> GetAsync(string? sessionId) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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
    public static Task<ActionResult> CurrentAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        return WebApi.OkJson(scope.GetRequiredService<ISessionManager>().CurrentSession);
    });

    /// <summary>会话绑定的工作区标识。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>工作区标识；会话不存在或未绑定工作区时 data 为 null。</returns>
    public static Task<ActionResult> WorkspaceIdAsync(string? sessionId) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var workspaceId = await scope.GetRequiredService<ISessionWorkspaceQuery>()
            .GetSessionWorkspaceIdAsync(sessionId.Trim(), WebApi.UserId)
            .ConfigureAwait(false);
        return WebApi.OkJson(workspaceId);
    });

    /// <summary>新建会话。</summary>
    /// <param name="payload">包装后的 <see cref="SessionCreateRequest"/> JSON；可为空。</param>
    /// <returns>新会话。</returns>
    public static Task<ActionResult> CreateAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        var input = WebApi.TryReadPayload<SessionCreateRequest>(payload, out var parsed) ? parsed : null;

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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
    public static Task<ActionResult> DeleteAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SessionIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        if (await CheckOwnerAsync(scope, input!.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        // 判定必须早于软删：轮次跑完还会往会话里写，删了就成了幽灵写入
        if (WebApi.Runtime.Runs.IsRunning(WebApi.UserId, input.SessionId.Trim()))
        {
            return WebApi.FailJson(409, "该会话正在进行中，请先停止再删除");
        }

        await scope.GetRequiredService<ISessionManager>()
            .DeleteSessionAsync(input.SessionId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new { deleted = true });
    });

    /// <summary>重命名会话。</summary>
    /// <param name="payload">包装后的 <see cref="SessionRenameRequest"/> JSON。</param>
    /// <returns>重命名结果。</returns>
    public static Task<ActionResult> RenameAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SessionRenameRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input!.Title))
        {
            return WebApi.FailJson(400, "title 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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
    public static Task<ActionResult> MessagesAsync(string? sessionId, string? limit) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        int? take = int.TryParse(limit, out var parsed) && parsed > 0 ? parsed : null;
        var messages = (await scope.GetRequiredService<ISessionManager>()
            .GetMessagesAsync(sessionId.Trim(), take)
            .ConfigureAwait(false)).ToList();
        AttachWebPaths(messages, await ResolveSessionRootPathAsync(scope, sessionId).ConfigureAwait(false));
        return WebApi.OkJson(messages);
    });

    /// <summary>会话最近 N 条消息。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="count">条数；缺省 20。</param>
    /// <returns>消息列表（时间正序）。</returns>
    public static Task<ActionResult> LatestMessagesAsync(string? sessionId, string? count) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var take = int.TryParse(count, out var parsed) && parsed > 0 ? parsed : 20;
        var messages = (await scope.GetRequiredService<ISessionManager>()
            .GetLatestMessagesAsync(sessionId.Trim(), take)
            .ConfigureAwait(false)).ToList();
        AttachWebPaths(messages, await ResolveSessionRootPathAsync(scope, sessionId).ConfigureAwait(false));
        return WebApi.OkJson(messages);
    });

    /// <summary>会话活跃消息（未压缩）。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>消息列表。</returns>
    public static Task<ActionResult> ActiveMessagesAsync(string? sessionId) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var messages = (await scope.GetRequiredService<ISessionManager>()
            .GetActiveMessagesAsync(sessionId.Trim())
            .ConfigureAwait(false)).ToList();
        AttachWebPaths(messages, await ResolveSessionRootPathAsync(scope, sessionId).ConfigureAwait(false));
        return WebApi.OkJson(messages);
    });

    /// <summary>追加一条消息。</summary>
    /// <param name="payload">包装后的 <see cref="SessionAddMessageRequest"/> JSON。</param>
    /// <returns>新消息。</returns>
    public static Task<ActionResult> AddMessageAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SessionAddMessageRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input!.Role))
        {
            return WebApi.FailJson(400, "role 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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
                input.ToolCalls,
                input.Segments,
                input.DurationMs)
            .ConfigureAwait(false);
        return WebApi.OkJson(message);
    });

    /// <summary>标记消息为已压缩。</summary>
    /// <param name="payload">包装后的 <see cref="SessionCompactionRequest"/> JSON。</param>
    /// <returns>标记结果。</returns>
    public static Task<ActionResult> MarkCompactedAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SessionCompactionRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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
    public static Task<ActionResult> StatsAsync(string? sessionId) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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
    public static Task<ActionResult> GlobalStatsAsync(string? days) => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        int? span = int.TryParse(days, out var parsed) && parsed > 0 ? parsed : null;
        var stats = await scope.GetRequiredService<ISessionManager>()
            .GetGlobalStatsAsync(span)
            .ConfigureAwait(false);
        return WebApi.OkJson(stats);
    });

    /// <summary>清空会话消息。</summary>
    /// <param name="payload">包装后的 <see cref="SessionIdRequest"/> JSON。</param>
    /// <returns>清空结果。</returns>
    public static Task<ActionResult> ClearMessagesAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SessionIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        if (await CheckOwnerAsync(scope, input!.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        // 清空期间若还有轮次在跑，它会把新一轮消息写回刚清空的会话：先停再清
        var sessionId = input.SessionId.Trim();
        var stopped = await WebApi.Runtime.Runs
            .CancelAndWaitAsync(WebApi.UserId, sessionId)
            .ConfigureAwait(false);
        if (!stopped && WebApi.Runtime.Runs.IsRunning(WebApi.UserId, sessionId))
        {
            return WebApi.FailJson(409, "本轮对话尚未停止，请稍后重试");
        }

        await scope.GetRequiredService<ISessionManager>()
            .ClearMessagesAsync(sessionId)
            .ConfigureAwait(false);
        return WebApi.OkJson(new { cleared = true });
    });

    /// <summary>物理删除会话：指定工作区，或多工作区一次清空（可重建一个空会话）。</summary>
    /// <param name="payload">包装后的 <see cref="SessionClearAllRequest"/> JSON；可为空。</param>
    /// <returns>清空结果。</returns>
    public static Task<ActionResult> ClearAllAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        var input = WebApi.TryReadPayload<SessionClearAllRequest>(payload, out var parsed) ? parsed : null;

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var manager = scope.GetRequiredService<ISessionManager>();
        var workspaces = scope.GetRequiredService<IWorkspaceManager>();

        // 目标工作区按「当前用户名下」校验：既拒绝越权，也让「目标已不存在」能被明确提示而不是假成功
        var ownedWorkspaceIds = (await workspaces.GetUserWorkspacesAsync().ConfigureAwait(false))
            .Select(item => item.WorkspaceId)
            .ToHashSet(StringComparer.Ordinal);
        var clearScope = SessionClearScope.Resolve(
            input?.AllWorkspaces == true,
            input?.WorkspaceId,
            workspaces.CurrentWorkspace?.WorkspaceId,
            ownedWorkspaceIds.Contains);

        if (clearScope.MissingWorkspaceId is not null)
        {
            return WebApi.FailJson(404, "工作区不存在");
        }

        // 批量删会话前先判定：删掉之后运行中这一轮的内容无法回填
        var running = clearScope.ClearAll
            ? (await manager.GetUserSessionsAsync().ConfigureAwait(false)).Select(session => session.SessionId)
            : (await scope.GetRequiredService<ISessionWorkspaceQuery>()
                .GetByWorkspaceAsync(clearScope.WorkspaceId!, WebApi.UserId)
                .ConfigureAwait(false)).Select(session => session.SessionId);
        if (WebApi.Runtime.Runs.IsAnyRunning(WebApi.UserId, running))
        {
            return clearScope.ClearAll
                ? WebApi.FailJson(409, "有会话正在进行中，请先停止")
                : WebApi.FailJson(409, "该工作区下有正在进行中的会话，请先停止");
        }

        if (clearScope.ClearAll)
        {
            // 框架语义即「清空该用户全部会话」；补建由下面统一处理
            await manager.ClearAllSessionsAsync().ConfigureAwait(false);
        }
        else if (manager is IWorkspaceSessionCleaner cleaner)
        {
            await cleaner.ClearWorkspaceSessionsAsync(clearScope.WorkspaceId!).ConfigureAwait(false);
        }
        else
        {
            return WebApi.FailJson(500, "当前会话管理器不支持按工作区清理");
        }

        // 保持「有当前工作区必有当前会话」：清理范围覆盖当前工作区时该区会话已全被删除，
        // 补建一个空会话，否则对话框失去持久化目标；清理其它工作区时 RebuildWorkspaceId 为 null，不动活动会话。
        if (!string.IsNullOrWhiteSpace(clearScope.RebuildWorkspaceId))
        {
            await manager.CreateSessionAsync(title: "新会话", workspaceId: clearScope.RebuildWorkspaceId)
                .ConfigureAwait(false);
        }

        return WebApi.OkJson(new { cleared = true, current = manager.CurrentSession?.SessionId });
    });

    /// <summary>切换当前会话（仅影响宿主内的「活动会话」指针）。</summary>
    /// <param name="payload">包装后的 <see cref="SessionIdRequest"/> JSON。</param>
    /// <returns>切换结果。</returns>
    public static Task<ActionResult> SwitchAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SessionIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        if (await CheckOwnerAsync(scope, input!.SessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        await scope.GetRequiredService<ISessionManager>()
            .SetCurrentSessionAsync(input.SessionId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new { sessionId = input.SessionId.Trim() });
    });

    /// <summary>设置会话单独使用的模型（只影响该会话，不改全局默认）。</summary>
    /// <param name="payload">包装后的 <see cref="SessionModelRequest"/> JSON。</param>
    /// <returns>切换后的选择。</returns>
    public static Task<ActionResult> SelectModelAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SessionModelRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.SessionId))
        {
            return WebApi.FailJson(400, "sessionId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input.ProviderId) || string.IsNullOrWhiteSpace(input.ModelId))
        {
            return WebApi.FailJson(400, "providerId / modelId 不能为空");
        }

        var sessionId = input.SessionId.Trim();
        var providerId = input.ProviderId.Trim().ToLowerInvariant();
        var modelId = input.ModelId.Trim();

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        if (await CheckOwnerAsync(scope, sessionId).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var updated = await scope.GetRequiredService<SessionRepository>()
            .UpdateModelAsync(sessionId, WebApi.UserId, providerId, modelId)
            .ConfigureAwait(false);
        if (!updated)
        {
            return WebApi.FailJson(404, WebSessionResolver.SessionNotFound);
        }

        return WebApi.OkJson(new ModelSelectionDto(providerId, modelId));
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

    /// <summary>
    /// 取会话所属工作区的根目录；只有「没有会话上下文」时才回落宿主当前工作区。
    /// </summary>
    /// <remarks>
    /// 会话绑定工作区后一旦解析不到（工作区被删除、会话被解绑），必须返回 null 而不是回落当前工作区：
    /// 两者根目录不同（甚至可能是父子目录）时，用错误的根换算附件绝对路径会得到指向别处的相对路径，
    /// 点击后 404 或打开错文件。返回 null 时附件按「不可点」降级。
    /// </remarks>
    /// <param name="scope">请求作用域。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>工作区根目录；无法确定时返回 null。</returns>
    private static async Task<string?> ResolveSessionRootPathAsync(DiContainer scope, string? sessionId)
    {
        var manager = scope.GetRequiredService<IWorkspaceManager>();

        // 无会话上下文：附件按宿主当前工作区落盘，读取也用同一根换算
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return manager.CurrentWorkspace?.RootPath;
        }

        var workspaceId = await scope.GetRequiredService<ISessionWorkspaceQuery>()
            .GetSessionWorkspaceIdAsync(sessionId.Trim(), WebApi.UserId)
            .ConfigureAwait(false);

        // 会话不存在或未绑定工作区：没有可信的落盘根（带 sessionId 上传时该情形会被拒绝）
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        // 绑定的工作区可能已被删除：同样不回落当前工作区
        var workspace = await manager.GetWorkspaceAsync(workspaceId).ConfigureAwait(false);
        return workspace?.RootPath;
    }

    /// <summary>
    /// 给消息里的附件补上可交给 <c>/file/read</c> 的相对路径（<c>WebPath</c>）。
    /// </summary>
    /// <remarks>
    /// 历史消息的 <see cref="SessionMessage.Attachments"/> 是序列化后的附件数组，其中源路径是绝对路径，
    /// 而 <c>/file/read</c> 只接受工作区内的相对路径。这里逐条换算并追加 <c>WebPath</c>；
    /// 换算不出来（越界、文件已删、JSON 脏数据）就不写该字段，前端据此判定该附件不可点。
    /// </remarks>
    /// <param name="messages">待回给浏览器的消息（原地修改）。</param>
    /// <param name="rootPath">工作区根目录。</param>
    private static void AttachWebPaths(List<SessionMessage> messages, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return;

        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.Attachments)) continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(message.Attachments);
            }
            catch (JsonException)
            {
                continue;
            }

            if (node is not JsonArray array) continue;

            var changed = false;
            foreach (var item in array)
            {
                if (item is not JsonObject entry) continue;
                // 已有非空 WebPath 才跳过：上传响应写入的是空串（换算失败），空串必须允许重算
                if (entry["WebPath"] is JsonValue existing
                    && existing.TryGetValue<string>(out var existingPath)
                    && !string.IsNullOrWhiteSpace(existingPath))
                {
                    continue;
                }
                if (entry["SourcePath"] is not { } sourceNode) continue;

                string sourcePath;
                try
                {
                    sourcePath = sourceNode.GetValue<string>();
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(sourcePath)) continue;
                if (!WebFilePath.TryGetRelative(rootPath, sourcePath, out var webPath)) continue;

                entry["WebPath"] = webPath;
                changed = true;
            }

            if (changed) message.Attachments = array.ToJsonString();
        }
    }

    internal static async Task<ActionResult?> CheckOwnerAsync(DiContainer scope, string sessionId)
    {
        var owner = await scope.GetRequiredService<SessionRepository>()
            .GetOwnerUserIdAsync(sessionId.Trim())
            .ConfigureAwait(false);

        if (owner is null)
        {
            return WebApi.FailJson(404, WebSessionResolver.SessionNotFound);
        }

        return string.Equals(owner, WebApi.UserId, StringComparison.Ordinal)
            ? null
            : WebApi.FailJson(403, WebSessionResolver.SessionForbidden);
    }
}