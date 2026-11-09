/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： WorkspaceController
*描述：工作区接口：列表 / 当前 / 创建 / 切换 / 删除 / 重命名
*
*****************************************************************************/

using LuBan.AIAgent.Sessions;

using LubanAgentCore.Protocol.Dtos;

using LubanAgentWebHost.Hosting;

using SAEA.MVC;

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 工作区接口。
/// </summary>
public sealed class WorkspaceController : Controller
{
    /// <summary>当前用户的工作区列表。</summary>
    /// <returns>工作区列表。</returns>
    [HttpGet]
    public Task<ActionResult> List() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspaces = await scope.GetRequiredService<IWorkspaceManager>()
            .GetUserWorkspacesAsync()
            .ConfigureAwait(false);
        return WebApi.OkJson(workspaces.Select(ToDto).ToList());
    });

    /// <summary>当前工作区。</summary>
    /// <returns>工作区；未设置时 data 为 null。</returns>
    [HttpGet]
    public Task<ActionResult> Current() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var current = scope.GetRequiredService<IWorkspaceManager>().CurrentWorkspace;
        return WebApi.OkJson(current is null ? null : ToDto(current));
    });

    /// <summary>按根目录取工作区。</summary>
    /// <param name="rootPath">根目录路径。</param>
    /// <returns>工作区；不存在时 data 为 null。</returns>
    [HttpGet]
    public Task<ActionResult> ByRootPath(string? rootPath) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return WebApi.FailJson(400, "rootPath 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspace = await scope.GetRequiredService<IWorkspaceManager>()
            .GetByRootPathAsync(rootPath.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(workspace is null ? null : ToDto(workspace));
    });

    /// <summary>按名称片段模糊搜索工作区。</summary>
    /// <param name="keyword">名称关键字。</param>
    /// <returns>匹配的工作区列表。</returns>
    [HttpGet]
    public Task<ActionResult> Search(string? keyword) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return WebApi.OkJson(new List<WorkspaceDto>());
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var workspaces = await scope.GetRequiredService<IWorkspaceManager>()
            .SearchByNameAsync(keyword.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(workspaces.Select(ToDto).ToList());
    });

    /// <summary>创建工作区。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceCreateRequest"/> JSON。</param>
    /// <returns>新工作区。</returns>
    [HttpPost]
    public Task<ActionResult> Create(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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
        return WebApi.OkJson(ToDto(workspace));
    });

    /// <summary>切换工作区。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>切换后的工作区。</returns>
    [HttpPost]
    public Task<ActionResult> Switch(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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
        return WebApi.OkJson(ToDto(current));
    });

    /// <summary>删除工作区（级联清理会话与知识库索引）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>删除结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<WorkspaceIdRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var deleted = await scope.GetRequiredService<IWorkspaceManager>()
            .DeleteWorkspaceAsync(input!.WorkspaceId.Trim())
            .ConfigureAwait(false);
        return deleted
            ? WebApi.OkJson(new { deleted = true })
            : WebApi.FailJson(404, "工作区不存在");
    });

    /// <summary>重命名工作区（仅改显示名）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceRenameRequest"/> JSON。</param>
    /// <returns>重命名结果。</returns>
    [HttpPost]
    public Task<ActionResult> Rename(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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
    [HttpPost]
    public Task<ActionResult> Authorize(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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
            ? WebApi.OkJson(ToDto(workspace))
            : WebApi.FailJson(500, "工作区授权失败");
    });

    /// <summary>确保工作区配置目录已初始化。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>工作区。</returns>
    [HttpPost]
    public Task<ActionResult> EnsureConfig(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

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
        return WebApi.OkJson(ToDto(workspace));
    });

    private static WorkspaceDto ToDto(WorkspaceInfo workspace) => new(
        workspace.WorkspaceId,
        workspace.Name,
        workspace.RootPath,
        workspace.Type,
        workspace.UserId,
        workspace.ConfigPath,
        workspace.LastActiveAt,
        workspace.IsAuthorized);
}