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

    /// <summary>
    /// 创建工作区（S2d §13.2 起升级为 <b>ensure 幂等语义</b>：存在即复用，不重复建）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三态口径（设计 §13.2）：
    /// ① rootPath 与既有工作区规范化命中（<see cref="WorkspaceIdGenerator.IsSameRoot"/>，
    /// 大小写不敏感、尾分隔符归一）→ 返回<b>既有</b>工作区（<c>reused=true</c>，零新建）；
    /// ② 磁盘已存在目录但未注册 → 注册为工作区（<c>CreateWorkspaceAsync</c> 的配置目录只补缺、
    /// 不清空重来，不重建目录）；③ 不存在 → 建目录 + 注册。②③ 均返回 <c>reused=false</c>。
    /// </para>
    /// <para>
    /// S2d §13.2 实测 500（「服务端处理失败」）根因即旧实现的重复 rootPath 抛
    /// <see cref="InvalidOperationException"/>（<c>WorkspaceManager.CreateWorkspaceAsync</c>
    /// 路径唯一性校验）落入 <see cref="WebApi.SafeErrorMessage"/> 默认分支；ensure 化后该路径
    /// 改为直接返回既有工作区，不再抛异常。并发 TOCTOU 由捕获回读兜底，仍幂等。
    /// 出参在 <see cref="WorkspaceDto"/> 全字段上平铺 <c>reused</c> 标记（既有按
    /// <see cref="WorkspaceDto"/> 反序列化的消费方忽略多余字段，不受影响）。
    /// 沿途失败按宿主 400/500 语义透出 message。
    /// </para>
    /// </remarks>
    /// <param name="payload">包装后的 <see cref="WorkspaceCreateRequest"/> JSON。</param>
    /// <returns>工作区（含 <c>reused</c> 标记）。</returns>
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

        var normalizedType = string.Equals(type, WorkspaceTypes.LlmWiki, StringComparison.OrdinalIgnoreCase)
            ? WorkspaceTypes.LlmWiki
            : WorkspaceTypes.Normal;
        var name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name!.Trim();

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var manager = scope.GetRequiredService<IWorkspaceManager>();

        // ① 规范化命中既有工作区 → 直接复用（ensure 幂等，S2d §13.2）
        var existing = (await manager.GetUserWorkspacesAsync().ConfigureAwait(false))
            .FirstOrDefault(w => WorkspaceIdGenerator.IsSameRoot(w.RootPath, rootPath));
        if (existing is not null)
        {
            return WebApi.OkJson(WithReusedFlag(existing, reused: true));
        }

        // ② 磁盘已存在目录未注册 → 注册（不重建目录）；③ 不存在 → 建目录 + 注册。
        // 两者同一注册路径（CreateWorkspaceAsync 幂等补缺配置目录，不触碰既有目录内容），
        // 差异仅在磁盘副作用（Directory.Exists 为真时不会重建目录），无需分支。
        try
        {
            var created = await manager
                .CreateWorkspaceAsync(rootPath, name, normalizedType)
                .ConfigureAwait(false);
            return WebApi.OkJson(WithReusedFlag(created, reused: false));
        }
        catch (InvalidOperationException)
        {
            // TOCTOU 兜底：并发下另一请求先注册同 rootPath → 回读既有工作区，幂等复用返回
            var hit = await manager.GetByRootPathAsync(rootPath).ConfigureAwait(false);
            return hit is null
                ? WebApi.FailJson(500, "工作区创建失败")
                : WebApi.OkJson(WithReusedFlag(hit, reused: true));
        }
    });

    /// <summary>工作区出参平铺 <c>reused</c> 标记（ensure 语义；多余字段对旧消费方透明）。</summary>
    /// <param name="workspace">工作区实体。</param>
    /// <param name="reused">是否复用既有工作区。</param>
    /// <returns>扁平出参对象。</returns>
    private static object WithReusedFlag(WorkspaceInfo workspace, bool reused)
    {
        var dto = WorkspaceMapping.ToDto(workspace);
        return new
        {
            dto.WorkspaceId,
            dto.Name,
            dto.RootPath,
            dto.Type,
            dto.UserId,
            dto.ConfigPath,
            dto.LastActiveAt,
            dto.IsAuthorized,
            Reused = reused,
        };
    }

    /// <summary>
    /// 浏览本机目录（S2d §13.1，<b>只读</b>）：列指定路径的子目录（单层、不递归、不列文件）；
    /// <paramref name="path"/> 空/缺省列盘符根。越界/不存在/无权限 → 400/404 语义 message。
    /// </summary>
    /// <remarks>
    /// 前端迷你目录浏览器逐层导航用；仅回子目录 <c>{name,path}</c>，无文件内容暴露；
    /// 浏览器侧数据仅内存渲染不落盘。<paramref name="path"/> 必须是绝对路径（相对路径按越界拒绝）。
    /// </remarks>
    /// <param name="path">目录路径（绝对）；空/缺省列盘符（<c>DriveInfo.GetDrives()</c>，取 Name/根路径）。</param>
    /// <returns><c>{dirs:[{name,path}]}</c>。</returns>
    public static Task<ActionResult> ListDirsAsync(string? path) => WebApi.RunGuardedAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            // 缺省层：盘符根（Windows 盘符；非 Windows 平台返回根挂载点）
            var drives = DriveInfo.GetDrives()
                .Select(d => new { name = d.Name, path = d.RootDirectory.FullName })
                .ToList<object>();
            return Task.FromResult(WebApi.OkJson(new { dirs = drives }));
        }

        var trimmed = path!.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            return Task.FromResult(WebApi.FailJson(400, "path 必须是绝对路径"));
        }

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Task.FromResult(WebApi.FailJson(400, "路径不合法"));
        }

        if (!Directory.Exists(full))
        {
            return Task.FromResult(WebApi.FailJson(404, "目录不存在"));
        }

        try
        {
            // 只列子目录（Directory.GetDirectories 单层、不递归、不含文件）
            var dirs = Directory.GetDirectories(full)
                .Select(p => new { name = Path.GetFileName(p) ?? p, path = p })
                .ToList<object>();
            return Task.FromResult(WebApi.OkJson(new { dirs }));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(WebApi.FailJson(400, "无权访问该目录"));
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult(WebApi.FailJson(404, "目录不存在"));
        }
        catch (IOException)
        {
            return Task.FromResult(WebApi.FailJson(400, "读取目录失败"));
        }
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

    /// <summary>删除工作区（物理清理会话、索引与 .luban-agent 配置目录，保留根目录文件）。</summary>
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