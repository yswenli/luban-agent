/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebWorkspaceService
*描述：工作区业务：列表 / 当前 / 创建 / 切换 / 删除 / 重命名 / 授权 / 配置目录
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 工作区业务。
/// </summary>
public static class WebWorkspaceService
{
    /// <summary>当前用户的工作区列表。</summary>
    /// <returns>工作区列表。</returns>
    public static Task<ActionResult> ListAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspaces = await scope.GetRequiredService<IWorkspaceManager>()
            .GetUserWorkspacesAsync()
            .ConfigureAwait(false);
        return WebApi.OkJson(workspaces.Select(WorkspaceMapping.ToDto).ToList());
    });

    /// <summary>当前工作区。</summary>
    /// <returns>工作区；未设置时 data 为 null。</returns>
    public static Task<ActionResult> CurrentAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var current = scope.GetRequiredService<IWorkspaceManager>().CurrentWorkspace;
        return WebApi.OkJson(current is null ? null : WorkspaceMapping.ToDto(current));
    });

    /// <summary>按根目录取工作区。</summary>
    /// <param name="rootPath">根目录路径。</param>
    /// <returns>工作区；不存在时 data 为 null。</returns>
    public static Task<ActionResult> ByRootPathAsync(string? rootPath) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return WebApi.FailJson(400, "rootPath 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspace = await scope.GetRequiredService<IWorkspaceManager>()
            .GetByRootPathAsync(rootPath.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(workspace is null ? null : WorkspaceMapping.ToDto(workspace));
    });

    /// <summary>按名称片段模糊搜索工作区。</summary>
    /// <param name="keyword">名称关键字。</param>
    /// <returns>匹配的工作区列表。</returns>
    public static Task<ActionResult> SearchAsync(string? keyword) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return WebApi.OkJson(new List<WorkspaceDto>());
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspaces = await scope.GetRequiredService<IWorkspaceManager>()
            .SearchByNameAsync(keyword.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(workspaces.Select(WorkspaceMapping.ToDto).ToList());
    });

    /// <summary>创建工作区。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceCreateRequest"/> JSON。</param>
    /// <returns>新工作区。</returns>
    public static Task<ActionResult> CreateAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<WorkspaceCreateRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.RootPath))
        {
            return WebApi.FailJson(400, "rootPath 不能为空");
        }

        var rootPath = input!.RootPath.Trim();
        if (!Path.IsPathFullyQualified(rootPath))
        {
            return WebApi.FailJson(400, "rootPath 必须是绝对路径");
        }

        var type = string.IsNullOrWhiteSpace(input.Type) ? WorkspaceTypes.Normal : input.Type.Trim();
        if (!string.Equals(type, WorkspaceTypes.Normal, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(type, WorkspaceTypes.LlmWiki, StringComparison.OrdinalIgnoreCase))
        {
            return WebApi.FailJson(400, "type 必须是 Normal 或 LlmWiki");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspace = await scope.GetRequiredService<IWorkspaceManager>()
            .CreateWorkspaceAsync(
                rootPath,
                string.IsNullOrWhiteSpace(input.Name) ? null : input.Name!.Trim(),
                string.Equals(type, WorkspaceTypes.LlmWiki, StringComparison.OrdinalIgnoreCase)
                    ? WorkspaceTypes.LlmWiki
                    : WorkspaceTypes.Normal)
            .ConfigureAwait(false);
        return WebApi.OkJson(WorkspaceMapping.ToDto(workspace));
    });

    /// <summary>切换工作区。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>切换后的工作区。</returns>
    public static Task<ActionResult> SwitchAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<WorkspaceIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var manager = scope.GetRequiredService<IWorkspaceManager>();
        var target = await manager.GetWorkspaceAsync(input!.WorkspaceId.Trim()).ConfigureAwait(false);
        if (target is null)
        {
            return WebApi.FailJson(404, "工作区不存在");
        }

        await manager.SwitchWorkspaceAsync(target.WorkspaceId).ConfigureAwait(false);
        var current = manager.CurrentWorkspace ?? target;
        return WebApi.OkJson(WorkspaceMapping.ToDto(current));
    });

    /// <summary>删除工作区（级联清理会话与知识库索引）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>删除结果。</returns>
    public static Task<ActionResult> DeleteAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<WorkspaceIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);

        // 级联删除会连同会话一起清掉：其中若有正在跑的轮次，先拒绝
        var sessions = await scope.GetRequiredService<ISessionWorkspaceQuery>()
            .GetByWorkspaceAsync(input!.WorkspaceId.Trim(), WebApi.Runtime.UserId)
            .ConfigureAwait(false);
        if (sessions.Any(s => WebApi.Runtime.Runs.IsRunning(WebApi.Runtime.UserId, s.SessionId)))
        {
            return WebApi.FailJson(409, "该工作区下有正在进行中的会话，请先停止");
        }

        var deleted = await scope.GetRequiredService<IWorkspaceManager>()
            .DeleteWorkspaceAsync(input.WorkspaceId.Trim())
            .ConfigureAwait(false);
        return deleted
            ? WebApi.OkJson(new { deleted = true })
            : WebApi.FailJson(404, "工作区不存在");
    });

    /// <summary>重命名工作区（仅改显示名）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceRenameRequest"/> JSON。</param>
    /// <returns>重命名结果。</returns>
    public static Task<ActionResult> RenameAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<WorkspaceRenameRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input!.Name))
        {
            return WebApi.FailJson(400, "name 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var renamed = await scope.GetRequiredService<IWorkspaceManager>()
            .RenameWorkspaceAsync(input.WorkspaceId.Trim(), input.Name.Trim())
            .ConfigureAwait(false);
        return renamed
            ? WebApi.OkJson(new { renamed = true })
            : WebApi.FailJson(404, "工作区不存在或名称非法");
    });

    /// <summary>
    /// 确认并持久化工作区授权。
    /// </summary>
    /// <remarks>
    /// 授权是一次人工确认：客户端弹窗得到用户同意后调本接口落库。
    /// 服务端无人值守，故此处直接放行提示，只负责持久化。
    /// </remarks>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>授权后的工作区。</returns>
    public static Task<ActionResult> AuthorizeAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<WorkspaceIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var manager = scope.GetRequiredService<IWorkspaceManager>();
        var workspace = await manager.GetWorkspaceAsync(input!.WorkspaceId.Trim()).ConfigureAwait(false);
        if (workspace is null)
        {
            return WebApi.FailJson(404, "工作区不存在");
        }

        if (manager is WorkspaceManager concrete)
        {
            concrete.AuthorizationPrompt = _ => Task.FromResult(true);
        }

        var authorized = await manager.EnsureAuthorizedAsync(workspace).ConfigureAwait(false);
        return authorized
            ? WebApi.OkJson(WorkspaceMapping.ToDto(workspace))
            : WebApi.FailJson(500, "工作区授权失败");
    });

    /// <summary>确保工作区配置目录已初始化。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>工作区。</returns>
    public static Task<ActionResult> EnsureConfigAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<WorkspaceIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var manager = scope.GetRequiredService<IWorkspaceManager>();
        var workspace = await manager.GetWorkspaceAsync(input!.WorkspaceId.Trim()).ConfigureAwait(false);
        if (workspace is null)
        {
            return WebApi.FailJson(404, "工作区不存在");
        }

        await manager.EnsureConfigDirectoryAsync(workspace).ConfigureAwait(false);
        return WebApi.OkJson(WorkspaceMapping.ToDto(workspace));
    });
}