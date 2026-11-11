/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebLlmWikiService
*描述：LLM Wiki 知识库业务（创建 / 删除 / 列文件 / 索引 / 检索 / 状态 / lint / 重建 / ingest / 清空）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// LLM Wiki 知识库业务，对应 <c>/llmwiki</c> 命令与 Codex 知识库窗口。
/// </summary>
/// <remarks>
/// 每个动作都按请求开一个用户作用域：知识库的仓储、检索与 wiki 服务都在宿主进程内，
/// 客户端只传工作区标识与入参。后台任务（<c>Start*</c>）需要宿主侧进度回调，
/// 客户端拿不到进度，故客户端的 <c>Start*</c> 会降级为同步调用。
/// </remarks>
public static class WebLlmWikiService
{
    /// <summary>创建知识库工作区。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiCreateRequest"/> JSON。</param>
    /// <returns>新工作区。</returns>
    public static Task<ActionResult> CreateAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<LlmWikiCreateRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.RootPath))
        {
            return WebApi.FailJson(400, "rootPath 不能为空");
        }

        var rootPath = input!.RootPath.Trim();
        if (!Path.IsPathFullyQualified(rootPath))
        {
            return WebApi.FailJson(400, "rootPath 必须是绝对路径");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspace = await scope.GetRequiredService<ILlmWikiManagementService>()
            .CreateAsync(rootPath, string.IsNullOrWhiteSpace(input.Name) ? null : input.Name!.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(WorkspaceMapping.ToDto(workspace));
    });

    /// <summary>删除知识库工作区并级联清理会话与索引。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>删除结果。</returns>
    public static Task<ActionResult> DeleteAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!TryReadWorkspaceId(payload, out var workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);

        // 彼此独立的守卫先放行：级联删除会连同会话一起清掉，其中有正在跑的轮次时先拒绝
        var sessions = await scope.GetRequiredService<ISessionWorkspaceQuery>()
            .GetByWorkspaceAsync(workspaceId, WebApi.Runtime.UserId)
            .ConfigureAwait(false);
        if (sessions.Any(s => WebApi.Runtime.Runs.IsRunning(WebApi.Runtime.UserId, s.SessionId)))
        {
            return WebApi.FailJson(409, "该工作区下有正在进行中的会话，请先停止");
        }

        await scope.GetRequiredService<ILlmWikiManagementService>()
            .DeleteAsync(workspaceId)
            .ConfigureAwait(false);
        return WebApi.OkJson(new { deleted = true });
    });

    /// <summary>列出已索引文件。</summary>
    /// <param name="workspaceId">知识库工作区标识。</param>
    /// <returns>已索引文件列表。</returns>
    public static Task<ActionResult> ListFilesAsync(string? workspaceId) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var files = await scope.GetRequiredService<ILlmWikiManagementService>()
            .ListFilesAsync(workspaceId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(files);
    });

    /// <summary>同步索引原始资料。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiIndexRequest"/> JSON。</param>
    /// <returns>索引报告。</returns>
    /// <remarks>索引属长耗时动作，受宿主同步等待上限约束（<c>LubanAgentWeb:TimeoutSeconds</c>）。</remarks>
    public static Task<ActionResult> IndexAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<LlmWikiIndexRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var report = await scope.GetRequiredService<ILlmWikiManagementService>()
            .IndexFilesAsync(input!.WorkspaceId.Trim(), NormalizeGlob(input.Glob))
            .ConfigureAwait(false);
        return WebApi.OkJson(report);
    });

    /// <summary>后台索引原始资料；宿主未启用后台任务服务时返回 null。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiIndexRequest"/> JSON。</param>
    /// <returns>后台任务信息。</returns>
    public static Task<ActionResult> StartIndexAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<LlmWikiIndexRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var task = await scope.GetRequiredService<ILlmWikiManagementService>()
            .StartIndexAsync(input!.WorkspaceId.Trim(), NormalizeGlob(input.Glob))
            .ConfigureAwait(false);
        return WebApi.OkJson(task);
    });

    /// <summary>检索知识库。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiSearchRequest"/> JSON。</param>
    /// <returns>命中列表。</returns>
    public static Task<ActionResult> SearchAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<LlmWikiSearchRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input!.Query))
        {
            return WebApi.FailJson(400, "query 不能为空");
        }

        var searchScope = WikiSearchScope.WikiThenRaw;
        if (!string.IsNullOrWhiteSpace(input.Scope)
            && !Enum.TryParse(input.Scope, ignoreCase: true, out searchScope))
        {
            return WebApi.FailJson(400, "scope 必须是 WikiThenRaw / WikiOnly / RawOnly");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var results = await scope.GetRequiredService<ILlmWikiManagementService>()
            .SearchAsync(input.WorkspaceId.Trim(), input.Query.Trim(), searchScope)
            .ConfigureAwait(false);
        return WebApi.OkJson(results);
    });

    /// <summary>读取知识库统计。</summary>
    /// <param name="workspaceId">知识库工作区标识；为空时取宿主当前工作区。</param>
    /// <returns>统计。</returns>
    public static Task<ActionResult> StatusAsync(string? workspaceId) => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var status = await scope.GetRequiredService<ILlmWikiManagementService>()
            .GetStatusAsync(string.IsNullOrWhiteSpace(workspaceId) ? null : workspaceId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(status);
    });

    /// <summary>客观项 lint。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>lint 报告。</returns>
    public static Task<ActionResult> LintAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!TryReadWorkspaceId(payload, out var workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var report = await scope.GetRequiredService<ILlmWikiManagementService>()
            .LintAsync(workspaceId)
            .ConfigureAwait(false);
        return WebApi.OkJson(report);
    });

    /// <summary>同步重建 wiki 向量索引。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>重建报告。</returns>
    public static Task<ActionResult> RebuildAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!TryReadWorkspaceId(payload, out var workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var report = await scope.GetRequiredService<ILlmWikiManagementService>()
            .RebuildAsync(workspaceId)
            .ConfigureAwait(false);
        return WebApi.OkJson(report);
    });

    /// <summary>后台重建 wiki 向量索引；宿主未启用后台任务服务时返回 null。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>后台任务信息。</returns>
    public static Task<ActionResult> StartRebuildAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!TryReadWorkspaceId(payload, out var workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var task = await scope.GetRequiredService<ILlmWikiManagementService>()
            .StartRebuildAsync(workspaceId)
            .ConfigureAwait(false);
        return WebApi.OkJson(task);
    });

    /// <summary>同步提取 ingest 源文件并合并为工作区内临时 markdown。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiIngestRequest"/> JSON。</param>
    /// <returns>合并草稿。</returns>
    public static Task<ActionResult> PrepareIngestAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<LlmWikiIngestRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input!.Target))
        {
            return WebApi.FailJson(400, "target 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        // 逐文件进度只在宿主侧可见，客户端不传 IProgress
        var draft = await scope.GetRequiredService<ILlmWikiManagementService>()
            .PrepareIngestAsync(input.WorkspaceId.Trim(), input.Target.Trim(), progress: null)
            .ConfigureAwait(false);
        return WebApi.OkJson(draft);
    });

    /// <summary>后台提取 ingest 源文件；宿主未启用后台任务服务时返回 null。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiIngestRequest"/> JSON。</param>
    /// <returns>后台任务信息。</returns>
    public static Task<ActionResult> StartIngestAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<LlmWikiIngestRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input!.Target))
        {
            return WebApi.FailJson(400, "target 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var task = await scope.GetRequiredService<ILlmWikiManagementService>()
            .StartIngestAsync(input.WorkspaceId.Trim(), input.Target.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(task);
    });

    /// <summary>清空指定工作区的文件与切块索引（不删除工作区记录）。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>清空结果。</returns>
    public static Task<ActionResult> ClearAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!TryReadWorkspaceId(payload, out var workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        await scope.GetRequiredService<ILlmWikiManagementService>()
            .ClearAsync(workspaceId)
            .ConfigureAwait(false);
        return WebApi.OkJson(new { cleared = true });
    });

    /// <summary>解析只带工作区标识的入参。</summary>
    private static bool TryReadWorkspaceId(string? payload, out string workspaceId)
    {
        workspaceId = "";
        if (!WebApi.TryReadPayload<LlmWikiWorkspaceRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return false;
        }

        workspaceId = input!.WorkspaceId.Trim();
        return true;
    }

    private static string? NormalizeGlob(string? glob)
        => string.IsNullOrWhiteSpace(glob) ? null : glob.Trim();
}