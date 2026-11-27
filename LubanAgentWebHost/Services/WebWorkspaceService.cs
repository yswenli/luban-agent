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
    /// <summary>git 克隆目标的进程内互斥（按归一化路径），避免并发克隆同一目录时互相删改。</summary>
    /// <remarks>条目数量与创建过的目标路径同阶，不做回收。</remarks>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CloneGates = new(StringComparer.Ordinal);

    /// <summary>当前用户的工作区列表。</summary>
    /// <returns>工作区列表。</returns>
    public static Task<ActionResult> ListAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var workspaces = await scope.GetRequiredService<IWorkspaceManager>()
            .GetUserWorkspacesAsync()
            .ConfigureAwait(false);
        return WebApi.OkJson(workspaces.Select(WorkspaceMapping.ToDto).ToList());
    });

    /// <summary>当前工作区。</summary>
    /// <returns>工作区；未设置时 data 为 null。</returns>
    public static Task<ActionResult> CurrentAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var workspace = await scope.GetRequiredService<IWorkspaceManager>()
            .GetByRootPathAsync(rootPath.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(workspace is null ? null : WorkspaceMapping.ToDto(workspace));
    });

    /// <summary>根目录是否已被任一用户登记为工作区（跨用户判重，只回布尔值）。</summary>
    /// <param name="rootPath">根目录路径。</param>
    /// <returns><c>{used:true|false}</c>。</returns>
    public static Task<ActionResult> RootUsedAsync(string? rootPath) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return WebApi.OkJson(new { used = false });
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var used = await scope.GetRequiredService<IWorkspaceManager>()
            .IsRootPathUsedByAnyUserAsync(rootPath.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new { used });
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

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var workspaces = await scope.GetRequiredService<IWorkspaceManager>()
            .SearchByNameAsync(keyword.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(workspaces.Select(WorkspaceMapping.ToDto).ToList());
    });

    /// <summary>允许的工作区根目录（机器级；登录用户可读，用于新建时的提示）。</summary>
    /// <returns><c>{roots:[...]}</c>。</returns>
    public static Task<ActionResult> ListRootsAsync() => WebApi.RunGuardedAsync(() =>
        Task.FromResult(WebApi.OkJson(new { roots = WorkspaceRootsStore.Load() })));

    /// <summary>保存工作区根目录（机器级，仅管理员）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceRootsRequest"/> JSON。</param>
    /// <returns>实际写入的根目录。</returns>
    public static Task<ActionResult> SaveRootsAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (await EnsureAdminAsync().ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<WorkspaceRootsRequest>(payload, out var request) || request is null)
        {
            return WebApi.FailJson(400, "请求体不合法");
        }

        try
        {
            var saved = WorkspaceRootsStore.Save(request.Roots);
            return WebApi.OkJson(new { roots = saved });
        }
        catch (ArgumentException ex)
        {
            return WebApi.FailJson(400, ex.Message);
        }
    });

    /// <summary>工作区根目录配置仅管理员可改；宿主固定令牌视为管理员。</summary>
    /// <returns>拒绝响应；有权限返回 null。</returns>
    private static async Task<ActionResult?> EnsureAdminAsync()
    {
        if (WebApi.IsBearerRequest)
        {
            return null;
        }

        if (WebApi.CurrentClaims is not { } claims
            || await WebApi.Runtime.Auth.ValidateAsync(claims).ConfigureAwait(false) is not { IsAdmin: true })
        {
            return WebApi.FailJson(403, "需要管理员权限");
        }

        return null;
    }

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
        if (!WebApi.TryReadPayload<WorkspaceCreateRequest>(payload, out var input))
        {
            return WebApi.FailJson(400, "请求体不合法");
        }

        var source = string.IsNullOrWhiteSpace(input!.Source) ? "directory" : input.Source!.Trim().ToLowerInvariant();
        if (source is not ("directory" or "git"))
        {
            return WebApi.FailJson(400, "source 必须是 directory 或 git");
        }

        var name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name!.Trim();
        var hasSubPath = !string.IsNullOrWhiteSpace(input.SubPath);

        // 分支只做校验与路径计算，不产生写盘副作用：
        // 克隆必须等「已是我自己的 / 已被他人登记」这类冲突判断之后再做。
        string rootPath;
        GitUrlInfo? gitInfo = null;

        if (source == "git")
        {
            var gitUrl = (input.GitUrl ?? string.Empty).Trim();
            if (gitUrl.Length == 0)
            {
                return WebApi.FailJson(400, "gitUrl 不能为空");
            }

            if (!IsSafeGitUrl(gitUrl) || !GitUrl.TryParse(gitUrl, out var parsed))
            {
                return WebApi.FailJson(400, "gitUrl 只支持 http(s) / ssh / git / file 协议或 scp 形式的地址");
            }

            gitInfo = parsed;

            var folder = ToFolderName(name) is { Length: > 0 } explicitName
                ? explicitName
                : ToFolderName(FolderFromGitUrl(gitUrl));
            if (folder.Length == 0)
            {
                return WebApi.FailJson(400, "无法从仓库地址推断项目名，请填写项目名");
            }

            if (string.IsNullOrWhiteSpace(input.RootPath))
            {
                // 旧客户端：未给根目录时保持原行为，克隆到 ~/.luban-agent/repositories/{名称}
                rootPath = Path.GetFullPath(Path.Combine(AgentHome.RepositoriesDirectory, folder));
                name ??= folder;
            }
            else
            {
                var root = input.RootPath.Trim();
                if (!Path.IsPathFullyQualified(root))
                {
                    return WebApi.FailJson(400, "rootPath 必须是绝对路径");
                }

                if (File.Exists(root))
                {
                    return WebApi.FailJson(400, "rootPath 指向的是文件，请填写目录");
                }

                if (!Directory.Exists(root))
                {
                    return WebApi.FailJson(409, $"根目录不存在：{root}", new { reason = "root-missing" });
                }

                // 未给子目录时按项目名/仓库名落一层，避免把仓库直接克隆进管理员根目录
                var sub = hasSubPath ? input.SubPath : folder;
                if (!WorkspaceSubPath.TryResolve(root, sub, out var target, out var pathError))
                {
                    return WebApi.FailJson(400, pathError);
                }

                rootPath = target;
                name ??= Path.GetFileName(target);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.RootPath))
            {
                return WebApi.FailJson(400, "rootPath 不能为空");
            }

            var root = input.RootPath.Trim();
            if (!Path.IsPathFullyQualified(root))
            {
                return WebApi.FailJson(400, "rootPath 必须是绝对路径");
            }

            // 指向文件时后续会当作目录建配置，必然失败；提前拒绝避免半截残留
            if (File.Exists(root))
            {
                return WebApi.FailJson(400, "rootPath 指向的是文件，请填写目录");
            }

            if (hasSubPath)
            {
                if (!Directory.Exists(root))
                {
                    return WebApi.FailJson(409, $"根目录不存在：{root}", new { reason = "root-missing" });
                }

                if (!WorkspaceSubPath.TryResolve(root, input.SubPath, out var target, out var pathError))
                {
                    return WebApi.FailJson(400, pathError);
                }

                rootPath = target;
                name ??= Path.GetFileName(target);
            }
            else
            {
                // 旧语义：rootPath 本身就是工作区目录
                rootPath = root;
            }
        }

        // HTTPS 凭据：请求里带了就用请求的，否则回落到当前用户同域名已保存的凭据。
        // 只认服务端从地址解析出的 host/port，不接受客户端另传。
        string? credentialUserName = null;
        string? credentialSecret = null;
        if (gitInfo is { IsHttps: true })
        {
            var resolved = await ResolveCredentialAsync(gitInfo, input.GitCredential).ConfigureAwait(false);
            if (resolved.Error is { } credentialError)
            {
                return WebApi.FailJson(400, credentialError);
            }

            credentialUserName = resolved.UserName;
            credentialSecret = resolved.Secret;
        }

        var type = string.IsNullOrWhiteSpace(input.Type) ? WorkspaceTypes.Normal : input.Type.Trim();
        if (!string.Equals(type, WorkspaceTypes.Normal, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(type, WorkspaceTypes.LlmWiki, StringComparison.OrdinalIgnoreCase))
        {
            return WebApi.FailJson(400, "type 必须是 Normal 或 LlmWiki");
        }

        // 工作区只能建在机器级白名单根目录下（默认 ~/.luban-agent，含 git 克隆目录）。
        // 只拦新建：放行列表变化后，既有工作区仍可切换与使用。
        if (RejectIfOutsideRoots(rootPath) is { } denied)
        {
            return denied;
        }

        var normalizedType = string.Equals(type, WorkspaceTypes.LlmWiki, StringComparison.OrdinalIgnoreCase)
            ? WorkspaceTypes.LlmWiki
            : WorkspaceTypes.Normal;

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var manager = scope.GetRequiredService<IWorkspaceManager>();

        // ① 规范化命中既有工作区 → 直接复用（ensure 幂等，S2d §13.2）
        var existing = (await manager.GetUserWorkspacesAsync().ConfigureAwait(false))
            .FirstOrDefault(w => WorkspaceIdGenerator.IsSameRoot(w.RootPath, rootPath));
        if (existing is not null)
        {
            return WebApi.OkJson(WithReusedFlag(existing, reused: true));
        }

        // 目录已被其他用户登记：目录本身不分用户，但登记记录按用户各持一行。
        // 未确认共用时先回 409，避免两人在同目录并发写文件却互不知情。
        if (!input.ShareExisting
            && await manager.IsRootPathUsedByAnyUserAsync(rootPath).ConfigureAwait(false))
        {
            return WebApi.FailJson(
                409,
                "此工作区已被使用，是否共用？共用后两人在同一目录读写文件，"
                + "会话与配置各自独立，但文件改动会互相可见。",
                new { reason = "in-use" });
        }

        // ② git 克隆：先查占用（上面已做），再落盘；失败只清理本次新建的目录。
        if (source == "git" && gitInfo is not null)
        {
            // 同一目标串行化：两个请求同时看到「空目录」会互相覆盖，
            // 后失败的一方还会把先成功的一方刚克隆出来的仓库删掉。
            var gate = CloneGates.GetOrAdd(
                WorkspaceIdGenerator.Normalize(rootPath),
                _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var state = InspectGitTarget(rootPath);
                switch (state)
                {
                    case GitTargetState.File:
                        return WebApi.FailJson(
                            409,
                            $"目标路径已存在同名文件：{rootPath}。请改名或先移走该文件。",
                            new { reason = "target-file" });

                    case GitTargetState.NonEmptyDirectory:
                        return WebApi.FailJson(
                            409,
                            $"目标目录非空且不是 git 仓库：{rootPath}。请换一个子目录，或先清空该目录。",
                            new { reason = "target-not-empty" });

                    case GitTargetState.Repository when !input.ReuseExisting:
                        // 不静默复用：目录里可能是别的项目，交给用户确认
                        return WebApi.FailJson(
                            409,
                            $"目标目录已存在 git 仓库：{rootPath}。是否直接在既有仓库上工作？",
                            new { reason = "exists" });
                }

                if (state != GitTargetState.Repository)
                {
                    var parent = Path.GetDirectoryName(rootPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    var git = new GitProcess(new LuBan.AIAgent.Infrastructure.ProcessRunner());
                    var clone = await git.RunWithCredentialAsync(
                            AgentHome.Root,
                            ["clone", "--", input.GitUrl!.Trim(), rootPath],
                            gitInfo.IsHttps ? gitInfo : null,
                            credentialUserName,
                            credentialSecret,
                            timeoutMs: 10 * 60 * 1000)
                        .ConfigureAwait(false);

                    if (!clone.Succeeded)
                    {
                        // 只清理本次克隆写入的内容：用户预先建好的空目录要保留
                        CleanFailedClone(rootPath, deleteDirectory: state == GitTargetState.Missing);
                        var kind = GitErrorClassifier.Classify(clone.StandardError, clone.StandardOutput, clone.TimedOut);
                        AgentWebRuntime.Log($"克隆仓库失败（{GitErrorClassifier.ToReason(kind)}）：{clone.StandardError}");
                        return FailGit(kind);
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }

        // ③ 磁盘已存在目录未注册 → 注册（不重建目录）；不存在 → 建目录 + 注册。
        // 两者同一注册路径（CreateWorkspaceAsync 幂等补缺配置目录，不触碰既有目录内容），
        // 差异仅在磁盘副作用（Directory.Exists 为真时不会重建目录），无需分支。
        try
        {
            var created = await manager
                .CreateWorkspaceAsync(rootPath, name, normalizedType)
                .ConfigureAwait(false);

            if (input.SaveCredential && gitInfo is { IsHttps: true })
            {
                await SaveCredentialAsync(gitInfo, credentialUserName, credentialSecret).ConfigureAwait(false);
            }

            return WebApi.OkJson(WithReusedFlag(created, reused: false));
        }
        catch (InvalidOperationException)
        {
            // TOCTOU 兜底：并发下另一请求先注册同 rootPath → 回读既有工作区，幂等复用返回
            var hit = await manager.GetByRootPathAsync(rootPath).ConfigureAwait(false);
            if (hit is null)
            {
                return WebApi.FailJson(500, "工作区创建失败");
            }

            if (input.SaveCredential && gitInfo is { IsHttps: true })
            {
                await SaveCredentialAsync(gitInfo, credentialUserName, credentialSecret).ConfigureAwait(false);
            }

            return WebApi.OkJson(WithReusedFlag(hit, reused: true));
        }
    });

    /// <summary>
    /// 解析本次请求要用的 HTTPS 凭据。
    /// </summary>
    /// <remarks>
    /// 优先级：请求里显式填写的凭据 → 当前用户在该域名下已保存的凭据 → 匿名。
    /// 用户名与密码必须成对出现：只填一个通常是想「改用户名但仍用旧令牌」，
    /// 这里直接报错，避免拿半截凭据去猜。
    /// </remarks>
    /// <param name="url">仓库地址解析结果。</param>
    /// <param name="input">请求携带的凭据。</param>
    /// <returns>用户名、密码与错误信息（错误信息非空时前两者无意义）。</returns>
    private static async Task<(string? UserName, string? Secret, string? Error)> ResolveCredentialAsync(
        GitUrlInfo url,
        GitCredentialInput? input)
    {
        var userName = input?.UserName?.Trim();
        var secret = input?.Secret;
        var hasUserName = !string.IsNullOrEmpty(userName);
        var hasSecret = !string.IsNullOrEmpty(secret);

        if (hasUserName != hasSecret)
        {
            return (null, null, "用户名与密码 / 令牌需要同时填写");
        }

        if (hasUserName)
        {
            if (secret!.Length > DbGitCredential.SecretMaxLength)
            {
                return (null, null, $"密码 / 令牌过长（最多 {DbGitCredential.SecretMaxLength} 个字符）");
            }

            return (userName, secret, null);
        }

        var saved = await new GitCredentialRepository()
            .GetAsync(WebApi.UserId, url.Scheme, url.Host, url.Port)
            .ConfigureAwait(false);

        return saved is null
            ? (null, null, null)
            : (saved.UserName, saved.Secret, null);
    }

    /// <summary>
    /// 保存本次使用的 HTTPS 凭据（按用户 + 协议 + 域名 + 端口覆盖）。
    /// </summary>
    /// <remarks>保存失败不阻断工作区创建：仓库已经克隆成功，凭据只是下次省输入。</remarks>
    /// <param name="url">仓库地址解析结果。</param>
    /// <param name="userName">用户名。</param>
    /// <param name="secret">密码 / 令牌。</param>
    private static async Task SaveCredentialAsync(GitUrlInfo url, string? userName, string? secret)
    {
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(secret))
        {
            return;
        }

        try
        {
            await new GitCredentialRepository()
                .UpsertAsync(WebApi.UserId, url.Scheme, url.Host, url.Port, userName!, secret!)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AgentWebRuntime.Log($"保存 Git 凭据失败：{url.Host}:{url.Port}：{ex.Message}");
        }
    }

    /// <summary>git 目标目录的即时状态。</summary>
    private enum GitTargetState
    {
        /// <summary>不存在（可直接克隆）。</summary>
        Missing,

        /// <summary>存在且为空目录（可直接克隆）。</summary>
        EmptyDirectory,

        /// <summary>已是 git 仓库（<c>.git</c> 存在）。</summary>
        Repository,

        /// <summary>存在、非空，但不是 git 仓库（克隆会被 git 拒绝）。</summary>
        NonEmptyDirectory,

        /// <summary>同名文件。</summary>
        File,
    }

    /// <summary>
    /// 判定 git 克隆目标的状态。
    /// </summary>
    /// <remarks>
    /// 克隆进非空目录会被 git 拒绝，提前判定才能给出「换个子目录」这类可操作提示，
    /// 而不是把 git 的 <c>destination path already exists</c> 原样透出。
    /// </remarks>
    /// <param name="path">目标目录。</param>
    /// <returns>目标状态。</returns>
    private static GitTargetState InspectGitTarget(string path)
    {
        if (System.IO.File.Exists(path))
        {
            return GitTargetState.File;
        }

        if (!Directory.Exists(path))
        {
            return GitTargetState.Missing;
        }

        var gitEntry = Path.Combine(path, ".git");
        if (Directory.Exists(gitEntry) || System.IO.File.Exists(gitEntry))
        {
            return GitTargetState.Repository;
        }

        return Directory.EnumerateFileSystemEntries(path).Any()
            ? GitTargetState.NonEmptyDirectory
            : GitTargetState.EmptyDirectory;
    }

    /// <summary>git 失败原因对应的可读说明。</summary>
    /// <param name="kind">失败原因。</param>
    /// <returns>提示文案。</returns>
    private static string GitErrorMessage(GitFailureKind kind) => kind switch
    {
        GitFailureKind.Auth => "Git 认证失败：请检查用户名与密码 / 访问令牌，或先在设置页为这个域名保存凭据。",
        GitFailureKind.NotFound => "仓库不存在，或当前账号没有访问权限，请检查地址与权限。",
        GitFailureKind.Network => "无法连接仓库：请检查网络、代理与证书配置。",
        GitFailureKind.Timeout => "连接仓库超时，请检查网络或稍后重试。",
        _ => "访问仓库失败，请检查仓库地址、网络与凭据。",
    };

    /// <summary>把 git 失败原因转成前端可分支的响应。</summary>
    /// <remarks>认证失败不用 401：前端把 401 统一当「登录过期」整页跳转。</remarks>
    /// <param name="kind">失败原因。</param>
    /// <returns>失败响应。</returns>
    private static ActionResult FailGit(GitFailureKind kind)
        => WebApi.FailJson(
            kind == GitFailureKind.Unknown ? 500 : 409,
            GitErrorMessage(kind),
            new { reason = GitErrorClassifier.ToReason(kind) });

    /// <summary>当前请求是否为管理员（宿主固定令牌视为管理员）。</summary>
    /// <returns>管理员返回 true。</returns>
    private static async Task<bool> IsAdminAsync()
    {
        if (WebApi.IsBearerRequest)
        {
            return true;
        }

        if (WebApi.CurrentClaims is not { } claims)
        {
            return false;
        }

        return await WebApi.Runtime.Auth.ValidateAsync(claims).ConfigureAwait(false) is { IsAdmin: true };
    }

    /// <summary>用户标识 → 展示名（管理员凭据视图用；查不到时回落用户标识）。</summary>
    /// <returns>展示名映射。</returns>
    private static async Task<Dictionary<string, string>> LoadUserNamesAsync()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var users = await WebApi.Runtime.Auth.ListUsersAsync().ConfigureAwait(false);
            foreach (var user in users)
            {
                map[user.UserId] = string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.UserName
                    : user.DisplayName!;
            }
        }
        catch (Exception ex)
        {
            AgentWebRuntime.Log($"读取用户列表失败（凭据视图回落用户标识）：{ex.Message}");
        }

        return map;
    }

    /// <summary>
    /// 新建工作区（含 git 克隆目标）必须落在机器级白名单根目录内。
    /// </summary>
    /// <remarks>
    /// 只拦新建：根目录列表变化后，已登记的工作区仍可切换与使用。
    /// </remarks>
    /// <param name="path">待校验的绝对路径。</param>
    /// <returns>越界时返回 403 响应；放行返回 null。</returns>
    private static ActionResult? RejectIfOutsideRoots(string path)
    {
        var roots = WorkspaceRootsStore.Load();

        // 同时校验物理路径：中间段的符号链接 / junction 可能把不存在的尾段带到白名单之外
        var physical = WorkspaceSubPath.ResolvePhysicalPath(path);
        if (WorkspaceRootsStore.IsAllowed(path, roots)
            && WorkspaceRootsStore.IsAllowed(physical, roots))
        {
            return null;
        }

        return WebApi.FailJson(
            403,
            $"目录不在允许的工作区根目录内：{path}。"
            + $"允许的根目录：{string.Join("、", roots)}（需管理员在设置页调整）");
    }

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
            dto.HasGit,
            Reused = reused,
        };
    }

    /// <summary>
    /// git 仓库地址是否可安全交给 <c>git clone</c>。
    /// </summary>
    /// <remarks>
    /// 只接受明确的协议前缀或 scp 形式，并拒绝以 <c>-</c> 开头的值：
    /// 后者会被 git 当成选项（参数注入），换行则可能截断命令行。
    /// </remarks>
    /// <param name="url">仓库地址。</param>
    /// <returns>可用返回 true。</returns>
    internal static bool IsSafeGitUrl(string url)
    {
        if (url.Length == 0 || url[0] == '-' || url.Contains('\r') || url.Contains('\n'))
        {
            return false;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("git://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // scp 形式：user@host:path
        return System.Text.RegularExpressions.Regex.IsMatch(
            url,
            "^[A-Za-z0-9._-]+@[A-Za-z0-9._-]+:[^\\s]+$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    /// <summary>从仓库地址末段推断项目名（去掉 <c>.git</c> 后缀）。</summary>
    /// <param name="url">仓库地址。</param>
    /// <returns>末段文本；取不到时为空串。</returns>
    internal static string FolderFromGitUrl(string url)
    {
        var text = url.Trim().TrimEnd('/', '\\');
        var index = text.LastIndexOfAny(['/', '\\', ':']);
        var name = index >= 0 ? text[(index + 1)..] : text;
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>
    /// 把项目名收敛成合法的单层目录名（不允许分隔符与上跳）。
    /// </summary>
    /// <param name="name">用户输入的项目名。</param>
    /// <returns>可用目录名；无法收敛时为空串。</returns>
    internal static string ToFolderName(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = text.Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '-' : ch).ToArray();
        var folder = new string(chars).Trim().TrimEnd('.');
        return folder is "." or ".." ? string.Empty : folder;
    }

    /// <summary>
    /// 清理克隆失败留下的内容：只删本次写入的部分，用户预先建好的空目录保留。
    /// </summary>
    /// <remarks>
    /// 目标里已有 <c>.git</c> 说明并发请求可能已经克隆成功，此时一律不动，
    /// 宁可在下次创建时提示「已存在仓库」，也不能删掉别人的仓库。
    /// </remarks>
    /// <param name="path">克隆目标。</param>
    /// <param name="deleteDirectory">目标目录是否由本次克隆创建（是则连目录一起删）。</param>
    private static void CleanFailedClone(string path, bool deleteDirectory)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            var gitEntry = Path.Combine(path, ".git");
            if (Directory.Exists(gitEntry) || File.Exists(gitEntry))
            {
                AgentWebRuntime.Log($"克隆失败但目标已是 git 仓库，跳过清理：{path}");
                return;
            }

            if (deleteDirectory)
            {
                TryDeleteDirectory(path);
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                TryDeleteEntry(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentWebRuntime.Log($"清理克隆残留失败：{path}：{ex.Message}");
        }
    }

    /// <summary>删除一个文件或目录；失败只记日志。</summary>
    /// <param name="path">文件或目录路径。</param>
    private static void TryDeleteEntry(string path)
    {
        if (!Directory.Exists(path))
        {
            try
            {
                // 克隆留下的文件可能带只读属性
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentWebRuntime.Log($"清理克隆残留文件失败：{path}：{ex.Message}");
            }

            return;
        }

        TryDeleteDirectory(path);
    }

    /// <summary>删除目录（含只读文件）；失败只记日志。</summary>
    /// <param name="path">目录路径。</param>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            // 克隆中途可能留下只读文件，先去掉只读属性再删
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 单个文件去只读失败不阻断整体删除
                }
            }

            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentWebRuntime.Log($"清理克隆残留目录失败：{path}：{ex.Message}");
        }
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

    /// <summary>
    /// 在白名单根目录内浏览子目录（只读，单层）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ListDirsAsync"/> 的区别：本接口的浏览起点必须是管理员配置的根目录，
    /// 相对路径由 <see cref="WorkspaceSubPath.TryResolve"/> 校验，不能借浏览接口翻到根目录之外。
    /// 只回目录（不含文件），供「在根目录下选一个子目录」的向导使用。
    /// </remarks>
    /// <param name="root">白名单根目录（绝对路径）。</param>
    /// <param name="path">当前目录相对根目录的路径；空表示根目录本身。</param>
    /// <returns><see cref="WorkspaceBrowseDto"/>。</returns>
    public static Task<ActionResult> BrowseAsync(string? root, string? path) => WebApi.RunGuardedAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return Task.FromResult(WebApi.FailJson(400, "root 不能为空"));
        }

        var rootText = root!.Trim();
        if (!Path.IsPathFullyQualified(rootText))
        {
            return Task.FromResult(WebApi.FailJson(400, "root 必须是绝对路径"));
        }

        if (!WorkspaceRootsStore.IsAllowed(rootText, WorkspaceRootsStore.Load()))
        {
            return Task.FromResult(WebApi.FailJson(403, "只能浏览管理员配置的工作区根目录"));
        }

        if (!Directory.Exists(rootText))
        {
            return Task.FromResult(WebApi.FailJson(
                409,
                $"根目录不存在：{rootText}",
                new { reason = "root-missing" }));
        }

        string fullRoot;
        string fullTarget;
        try
        {
            fullRoot = WorkspaceIdGenerator.Normalize(rootText);
            if (!WorkspaceSubPath.TryResolve(fullRoot, path, out fullTarget, out var error))
            {
                return Task.FromResult(WebApi.FailJson(400, error));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Task.FromResult(WebApi.FailJson(400, "路径不合法"));
        }

        if (!Directory.Exists(fullTarget))
        {
            return Task.FromResult(WebApi.FailJson(404, "目录不存在", new { reason = "path-missing" }));
        }

        var relative = Path.GetRelativePath(fullRoot, fullTarget);
        if (relative == ".")
        {
            relative = string.Empty;
        }
        else
        {
            relative = relative.Replace('\\', '/');
        }

        string? parent = null;
        if (relative.Length > 0)
        {
            var slash = relative.LastIndexOf('/');
            parent = slash > 0 ? relative[..slash] : string.Empty;
        }

        try
        {
            var entries = new DirectoryInfo(fullTarget)
                .EnumerateDirectories()
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new WorkspaceBrowseEntryDto(
                    d.Name,
                    relative.Length == 0 ? d.Name : $"{relative}/{d.Name}",
                    Directory.Exists(Path.Combine(d.FullName, ".git")) || System.IO.File.Exists(Path.Combine(d.FullName, ".git"))))
                .ToList();

            return Task.FromResult(WebApi.OkJson(new WorkspaceBrowseDto(fullRoot, relative, parent, entries)));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(WebApi.FailJson(400, "无权访问该目录"));
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException)
        {
            return Task.FromResult(WebApi.FailJson(400, "读取目录失败"));
        }
    });

    /// <summary>
    /// 预检 Git 仓库地址：用 <c>git ls-remote</c> 验证地址、网络与凭据是否可用。
    /// </summary>
    /// <remarks>
    /// 始终 200，用 <see cref="GitVerifyResultDto.Ok"/> 表达成败：
    /// 前端在向导里「下一步」时就要知道凭据对不对，不应等到克隆阶段才报错。
    /// SSH 地址可解析但网页端暂不提供凭据录入，返回 <c>git-ssh-unsupported</c>。
    /// </remarks>
    /// <param name="payload">包装后的 <see cref="GitVerifyRequest"/> JSON。</param>
    /// <returns><see cref="GitVerifyResultDto"/>。</returns>
    public static Task<ActionResult> VerifyGitAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<GitVerifyRequest>(payload, out var request) || request is null)
        {
            return WebApi.FailJson(400, "请求体不合法");
        }

        var text = (request.GitUrl ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return WebApi.FailJson(400, "gitUrl 不能为空");
        }

        if (!IsSafeGitUrl(text) || !GitUrl.TryParse(text, out var info))
        {
            return WebApi.FailJson(400, "gitUrl 只支持 http(s) / ssh / git / file 协议或 scp 形式的地址");
        }

        if (info.Kind == GitUrlKind.Ssh)
        {
            return WebApi.OkJson(new GitVerifyResultDto(
                false,
                info.Scheme,
                info.Host,
                info.Port,
                "git-ssh-unsupported",
                "暂不支持 SSH 地址：请改用 HTTPS 地址（SSH 密钥需在服务器上单独配置）。"));
        }

        string? userName = null;
        string? secret = null;
        if (info.SupportsCredential)
        {
            var resolved = await ResolveCredentialAsync(info, request.GitCredential).ConfigureAwait(false);
            if (resolved.Error is { } credentialError)
            {
                return WebApi.FailJson(400, credentialError);
            }

            userName = resolved.UserName;
            secret = resolved.Secret;
        }

        var git = new GitProcess(new LuBan.AIAgent.Infrastructure.ProcessRunner());
        var result = await git.RunWithCredentialAsync(
                AgentHome.Root,
                ["ls-remote", "--heads", "--", text],
                info.SupportsCredential ? info : null,
                userName,
                secret,
                timeoutMs: 60_000)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            return WebApi.OkJson(new GitVerifyResultDto(true, info.Scheme, info.Host, info.Port, null, null));
        }

        var kind = GitErrorClassifier.Classify(result.StandardError, result.StandardOutput, result.TimedOut);
        AgentWebRuntime.Log($"预检仓库失败（{GitErrorClassifier.ToReason(kind)}）：{info.Host}/{info.Port}");
        return WebApi.OkJson(new GitVerifyResultDto(
            false,
            info.Scheme,
            info.Host,
            info.Port,
            GitErrorClassifier.ToReason(kind),
            GitErrorMessage(kind)));
    });

    /// <summary>某个仓库地址对应的已保存凭据状态（只回用户名，永不回密码）。</summary>
    /// <param name="gitUrl">仓库地址。</param>
    /// <returns><see cref="GitCredentialStatusDto"/>。</returns>
    public static Task<ActionResult> GitCredentialStatusAsync(string? gitUrl) => WebApi.RunGuardedAsync(async () =>
    {
        var text = (gitUrl ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return WebApi.FailJson(400, "gitUrl 不能为空");
        }

        if (!GitUrl.TryParse(text, out var info))
        {
            return WebApi.FailJson(400, "gitUrl 不合法");
        }

        if (!info.SupportsCredential)
        {
            return WebApi.OkJson(new GitCredentialStatusDto(info.Scheme, info.Host, info.Port, false, null));
        }

        var saved = await new GitCredentialRepository()
            .GetAsync(WebApi.UserId, info.Scheme, info.Host, info.Port)
            .ConfigureAwait(false);

        return WebApi.OkJson(new GitCredentialStatusDto(
            info.Scheme,
            info.Host,
            info.Port,
            saved is not null,
            saved?.UserName));
    });

    /// <summary>
    /// 列出 Git 凭据：普通用户只看到自己的，管理员看到全部（含归属用户）。
    /// </summary>
    /// <returns><see cref="GitCredentialDto"/> 列表。</returns>
    public static Task<ActionResult> ListGitCredentialsAsync() => WebApi.RunGuardedAsync(async () =>
    {
        var isAdmin = await IsAdminAsync().ConfigureAwait(false);
        var repository = new GitCredentialRepository();
        var list = isAdmin
            ? await repository.ListAllAsync().ConfigureAwait(false)
            : await repository.ListByUserAsync(WebApi.UserId).ConfigureAwait(false);

        var owners = isAdmin
            ? await LoadUserNamesAsync().ConfigureAwait(false)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var dtos = list.Select(c => new GitCredentialDto(
            c.Id,
            c.UserId,
            owners.TryGetValue(c.UserId, out var owner) ? owner : c.UserId,
            c.Scheme,
            c.Host,
            c.Port,
            c.UserName,
            c.UpdateTime ?? c.CreateTime)).ToList();

        return WebApi.OkJson(dtos);
    });

    /// <summary>
    /// 单独保存一条 HTTPS 凭据（设置页用）。
    /// </summary>
    /// <remarks>
    /// 只接受 <c>host[:port]</c> 或完整的 HTTPS 地址：SSH 凭据网页端不提供，
    /// 这里显式拒绝 <c>ssh://</c>，避免把 token 存成用不上的记录。
    /// </remarks>
    /// <param name="payload">包装后的 <see cref="GitCredentialSaveRequest"/> JSON。</param>
    /// <returns>保存后的 <see cref="GitCredentialDto"/>。</returns>
    public static Task<ActionResult> SaveGitCredentialAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<GitCredentialSaveRequest>(payload, out var request) || request is null)
        {
            return WebApi.FailJson(400, "请求体不合法");
        }

        var hostText = (request.Host ?? string.Empty).Trim();
        var userName = request.UserName?.Trim();
        if (hostText.Length == 0)
        {
            return WebApi.FailJson(400, "域名不能为空");
        }

        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(request.Secret))
        {
            return WebApi.FailJson(400, "用户名与密码 / 令牌不能为空");
        }

        if (request.Secret.Length > DbGitCredential.SecretMaxLength)
        {
            return WebApi.FailJson(400, $"密码 / 令牌过长（最多 {DbGitCredential.SecretMaxLength} 个字符）");
        }

        string host;
        int port;
        if (hostText.Contains("://", StringComparison.Ordinal))
        {
            if (!GitUrl.TryParse(hostText, out var parsed) || parsed.Kind != GitUrlKind.Https)
            {
                return WebApi.FailJson(400, "凭据只支持 HTTPS 地址或域名");
            }

            host = parsed.Host;
            port = parsed.Port;
        }
        else
        {
            port = 443;
            host = hostText;

            if (hostText.StartsWith('['))
            {
                // IPv6 字面量：[::1] / [::1]:8443（与 Uri.Host 的返回形式一致）
                var close = hostText.IndexOf(']');
                if (close < 0)
                {
                    return WebApi.FailJson(400, "IPv6 地址格式不正确，例如 [::1]:8443");
                }

                host = hostText[..(close + 1)].ToLowerInvariant();
                var rest = hostText[(close + 1)..];
                if (rest.Length > 0
                    && (rest[0] != ':' || !int.TryParse(rest[1..], out port)))
                {
                    return WebApi.FailJson(400, "IPv6 地址格式不正确，例如 [::1]:8443");
                }

                if (!IPAddress.TryParse(host[1..^1], out var address)
                    || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    return WebApi.FailJson(400, "IPv6 地址格式不正确，例如 [::1]:8443");
                }
            }
            else
            {
                var colon = hostText.LastIndexOf(':');
                if (colon > 0 && int.TryParse(hostText[(colon + 1)..], out var parsedPort))
                {
                    host = hostText[..colon];
                    port = parsedPort;
                }

                host = host.Trim().TrimEnd('.').ToLowerInvariant();
                if (host.Length == 0
                    || host.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')))
                {
                    return WebApi.FailJson(400, "域名格式不正确，例如 git.example.com 或 git.example.com:8443");
                }
            }

            if (port is < 1 or > 65535)
            {
                return WebApi.FailJson(400, "端口必须在 1-65535 之间");
            }
        }

        var saved = await new GitCredentialRepository()
            .UpsertAsync(WebApi.UserId, "https", host, port, userName, request.Secret)
            .ConfigureAwait(false);

        return WebApi.OkJson(new GitCredentialDto(
            saved.Id,
            saved.UserId,
            WebApi.CurrentClaims?.UserName ?? saved.UserId,
            saved.Scheme,
            saved.Host,
            saved.Port,
            saved.UserName,
            saved.UpdateTime ?? saved.CreateTime));
    });

    /// <summary>
    /// 删除 Git 凭据：普通用户只能删自己的，管理员可按主键删任意用户的。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="GitCredentialDeleteRequest"/> JSON。</param>
    /// <returns><c>{deleted:true}</c>。</returns>
    public static Task<ActionResult> DeleteGitCredentialAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<GitCredentialDeleteRequest>(payload, out var request) || request is null)
        {
            return WebApi.FailJson(400, "请求体不合法");
        }

        var isAdmin = await IsAdminAsync().ConfigureAwait(false);
        var repository = new GitCredentialRepository();

        if (request.Id is { } id)
        {
            var target = await repository.GetByIdAsync(id).ConfigureAwait(false);
            if (target is null)
            {
                return WebApi.FailJson(404, "凭据不存在");
            }

            if (!isAdmin && !string.Equals(target.UserId, WebApi.UserId, StringComparison.Ordinal))
            {
                return WebApi.FailJson(403, "只能删除自己保存的凭据");
            }

            await repository.DeleteByIdAsync(id).ConfigureAwait(false);
            return WebApi.OkJson(new { deleted = true });
        }

        var gitUrl = (request.GitUrl ?? string.Empty).Trim();
        if (gitUrl.Length == 0 || !GitUrl.TryParse(gitUrl, out var info) || !info.SupportsCredential)
        {
            return WebApi.FailJson(400, "需要提供凭据 id，或一个 HTTP(S) 仓库地址");
        }

        var deleted = await repository
            .DeleteAsync(WebApi.UserId, info.Scheme, info.Host, info.Port)
            .ConfigureAwait(false);
        return deleted > 0
            ? WebApi.OkJson(new { deleted = true })
            : WebApi.FailJson(404, "凭据不存在");
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

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);

        // 级联删除会连同会话一起清掉：其中若有正在跑的轮次，先拒绝
        var sessions = await scope.GetRequiredService<ISessionWorkspaceQuery>()
            .GetByWorkspaceAsync(input!.WorkspaceId.Trim(), WebApi.UserId)
            .ConfigureAwait(false);
        if (sessions.Any(s => WebApi.Runtime.Runs.IsRunning(WebApi.UserId, s.SessionId)))
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

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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