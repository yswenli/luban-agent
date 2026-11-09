/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： HttpWorkspaceManager
*描述：IWorkspaceManager 的 HTTP 实现（工作区数据在 WebHost 侧，本地镜像当前工作区与根路径缓存）
*
*****************************************************************************/

using System.Collections.Concurrent;

namespace LubanAgentCore.Client;

/// <summary>
/// 工作区管理的 HTTP 实现。
/// </summary>
/// <remarks>
/// <para>
/// 需要人工确认的两步——授权（<see cref="EnsureAuthorizedAsync"/>）与配置目录初始化
/// （<see cref="EnsureConfigDirectoryAsync"/>）——在服务端各有一个动作承接，客户端只负责
/// 弹窗确认后再转发，避免「本地以为授权了、服务端仍拒绝」的状态分叉。
/// </para>
/// <para>
/// <see cref="IsWithinWorkspace"/> / <see cref="GetKnownRootPath"/> 是同步接口，只能基于本地镜像
/// （当前工作区 + 列表缓存），不发起网络请求。
/// </para>
/// </remarks>
public sealed class HttpWorkspaceManager(AgentServerClient client, HttpSessionManager sessions) : IWorkspaceManager
{
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, string> _knownRoots = new(StringComparer.OrdinalIgnoreCase);
    private WorkspaceInfo? _current;

    /// <inheritdoc />
    public WorkspaceInfo? CurrentWorkspace
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
    public event Action<WorkspaceInfo>? CurrentWorkspaceChanged;

    /// <summary>授权提示；未设置时按拒绝处理（与本地实现同口径）。</summary>
    public Func<WorkspaceInfo, Task<bool>>? AuthorizationPrompt { get; set; }

    /// <inheritdoc />
    public async Task<WorkspaceInfo> CreateWorkspaceAsync(
        string rootPath, string? name = null, string type = WorkspaceTypes.Normal)
    {
        var workspace = await client.PostAsync<WorkspaceDto>(
            AgentApiRoutes.Workspace.Create,
            new WorkspaceCreateRequest { RootPath = rootPath, Name = name, Type = type })
            .ConfigureAwait(false);

        Register(workspace);
        return ToInfo(workspace);
    }

    /// <inheritdoc />
    public async Task SwitchWorkspaceAsync(string workspaceId)
    {
        var workspace = await client.PostAsync<WorkspaceDto>(
            AgentApiRoutes.Workspace.Switch,
            new WorkspaceIdRequest { WorkspaceId = workspaceId })
            .ConfigureAwait(false);

        SetCurrent(ToInfo(workspace));

        // 切换工作区时服务端会恢复/新建该工作区的活动会话，本地镜像必须同步，
        // 否则随后读取 CurrentSession 会拿到上一个工作区的会话（知识库会话、页脚统计等）
        await sessions.RefreshCurrentAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WorkspaceInfo>> GetUserWorkspacesAsync()
    {
        var workspaces = await client
            .GetAsync<List<WorkspaceDto>>(AgentApiRoutes.Workspace.List)
            .ConfigureAwait(false);

        foreach (var workspace in workspaces)
        {
            Register(workspace);
        }

        return workspaces.Select(ToInfo).ToList();
    }

    /// <inheritdoc />
    public async Task<WorkspaceInfo?> GetWorkspaceAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        var workspaces = await GetUserWorkspacesAsync().ConfigureAwait(false);
        return workspaces.FirstOrDefault(w => string.Equals(w.WorkspaceId, workspaceId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.IsAuthorized)
        {
            return true;
        }

        if (AuthorizationPrompt is null)
        {
            Logger.Warn("AuthorizationPrompt delegate not set, defaulting to denied");
            return false;
        }

        if (!await AuthorizationPrompt(workspace).ConfigureAwait(false))
        {
            return false;
        }

        var authorized = await client.PostAsync<WorkspaceDto>(
            AgentApiRoutes.Workspace.Authorize,
            new WorkspaceIdRequest { WorkspaceId = workspace.WorkspaceId })
            .ConfigureAwait(false);

        var updated = ToInfo(authorized);
        workspace.IsAuthorized = updated.IsAuthorized;
        Register(authorized);

        lock (_gate)
        {
            if (_current?.WorkspaceId == workspace.WorkspaceId)
            {
                _current.IsAuthorized = updated.IsAuthorized;
            }
        }

        return updated.IsAuthorized;
    }

    /// <inheritdoc />
    public async Task EnsureConfigDirectoryAsync(WorkspaceInfo workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var ensured = await client.PostAsync<WorkspaceDto>(
            AgentApiRoutes.Workspace.EnsureConfig,
            new WorkspaceIdRequest { WorkspaceId = workspace.WorkspaceId })
            .ConfigureAwait(false);

        Register(ensured);
    }

    /// <inheritdoc />
    public async Task SetCurrentAsync(string workspaceId) => await SwitchWorkspaceAsync(workspaceId).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> DeleteWorkspaceAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return false;
        }

        try
        {
            await client.PostAsync(
                AgentApiRoutes.Workspace.Delete,
                new WorkspaceIdRequest { WorkspaceId = workspaceId })
                .ConfigureAwait(false);
        }
        catch (AgentServerException ex) when (ex.IsNotFound)
        {
            return false;
        }

        _knownRoots.TryRemove(workspaceId, out _);

        lock (_gate)
        {
            if (_current?.WorkspaceId == workspaceId)
            {
                _current = null;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<WorkspaceInfo?> GetByRootPathAsync(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var workspace = await client.GetOrNullAsync<WorkspaceDto>(
            AgentApiRoutes.Workspace.ByRootPath,
            new Dictionary<string, string?> { ["rootPath"] = rootPath })
            .ConfigureAwait(false);

        if (workspace is null)
        {
            return null;
        }

        Register(workspace);
        return ToInfo(workspace);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceInfo>> SearchByNameAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Array.Empty<WorkspaceInfo>();
        }

        var workspaces = await client.GetAsync<List<WorkspaceDto>>(
            AgentApiRoutes.Workspace.Search,
            new Dictionary<string, string?> { ["keyword"] = keyword })
            .ConfigureAwait(false);

        foreach (var workspace in workspaces)
        {
            Register(workspace);
        }

        return workspaces.Select(ToInfo).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> RenameWorkspaceAsync(string workspaceId, string name)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            await client.PostAsync(
                AgentApiRoutes.Workspace.Rename,
                new WorkspaceRenameRequest { WorkspaceId = workspaceId, Name = name })
                .ConfigureAwait(false);
        }
        catch (AgentServerException ex) when (ex.IsNotFound)
        {
            return false;
        }

        lock (_gate)
        {
            if (_current?.WorkspaceId == workspaceId)
            {
                _current.Name = name;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public string? GetKnownRootPath(string? workspaceId)
        => string.IsNullOrEmpty(workspaceId)
            ? null
            : _knownRoots.TryGetValue(workspaceId, out var root) ? root : null;

    /// <inheritdoc />
    public bool IsWithinWorkspace(string path)
    {
        WorkspaceInfo? workspace;
        lock (_gate)
        {
            workspace = _current;
        }

        if (workspace is null || string.IsNullOrEmpty(workspace.RootPath) || string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var rootPath = Path.GetFullPath(workspace.RootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 从服务端刷新当前工作区（客户端启动时调用一次，填充同步接口所需的镜像）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>当前工作区；未设置时为 null。</returns>
    public async Task<WorkspaceInfo?> RefreshAsync(CancellationToken ct = default)
    {
        var dto = await client
            .GetOrNullAsync<WorkspaceDto>(AgentApiRoutes.Workspace.Current, null, ct)
            .ConfigureAwait(false);

        if (dto is null)
        {
            WorkspaceInfo? previous;
            lock (_gate)
            {
                previous = _current;
                _current = null;
            }

            if (previous is not null)
            {
                CurrentWorkspaceChanged?.Invoke(previous);
            }

            return null;
        }

        Register(dto);
        var info = ToInfo(dto);
        SetCurrent(info);
        return info;
    }

    private void Register(WorkspaceDto workspace)
    {
        if (!string.IsNullOrWhiteSpace(workspace.RootPath))
        {
            _knownRoots[workspace.WorkspaceId] = workspace.RootPath;
        }
    }

    private void SetCurrent(WorkspaceInfo workspace)
    {
        WorkspaceInfo? previous;
        lock (_gate)
        {
            previous = _current;
            _current = workspace;
        }

        if (previous is null || !string.Equals(previous.WorkspaceId, workspace.WorkspaceId, StringComparison.Ordinal))
        {
            CurrentWorkspaceChanged?.Invoke(workspace);
        }
    }

    private static WorkspaceInfo ToInfo(WorkspaceDto workspace) => new()
    {
        WorkspaceId = workspace.WorkspaceId,
        Name = workspace.Name,
        RootPath = workspace.RootPath,
        Type = workspace.Type,
        UserId = workspace.UserId,
        ConfigPath = workspace.ConfigPath,
        LastActiveAt = workspace.LastActiveAt,
        IsAuthorized = workspace.IsAuthorized
    };
}