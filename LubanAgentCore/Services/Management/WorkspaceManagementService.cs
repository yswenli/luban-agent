/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： WorkspaceManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：工作区管理实现：严格转发 <see cref="IWorkspaceManager"/>，不承载关键字解析、会话数聚合与渲染。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：工作区管理实现：严格转发 <see cref="IWorkspaceManager"/>，不承载关键字解析、会话数聚合与渲染。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 工作区管理实现：严格转发 <see cref="IWorkspaceManager"/>，不承载关键字解析、会话数聚合与渲染。
/// </summary>
/// <remarks>
/// 授权确认依赖 UI 委托（<see cref="IWorkspaceManager.AuthorizationPrompt"/>），故 <see cref="GrantAccessAsync"/>
/// 仍走 <see cref="IWorkspaceManager.EnsureAuthorizedAsync"/>，由宿主设置确认回调。
/// </remarks>
public sealed class WorkspaceManagementService : IWorkspaceManagementService
{
    private readonly IWorkspaceManager _workspaces;

    /// <summary>创建工作区管理服务。</summary>
    /// <param name="workspaces">工作区管理器。</param>
    public WorkspaceManagementService(IWorkspaceManager workspaces)
    {
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceInfo>> ListAsync(CancellationToken ct = default)
    {
        var workspaces = await _workspaces.GetUserWorkspacesAsync();
        return workspaces.ToList();
    }

    /// <inheritdoc />
    public Task<WorkspaceInfo?> GetCurrentAsync(CancellationToken ct = default)
        => Task.FromResult(_workspaces.CurrentWorkspace);

    /// <inheritdoc />
    public Task<WorkspaceInfo> CreateAsync(string? name, string rootPath, string type, CancellationToken ct = default)
        => _workspaces.CreateWorkspaceAsync(rootPath, name, type);

    /// <inheritdoc />
    public async Task DeleteAsync(string workspaceId, CancellationToken ct = default)
    {
        await _workspaces.DeleteWorkspaceAsync(workspaceId);
    }

    /// <inheritdoc />
    public Task SwitchAsync(string workspaceId, CancellationToken ct = default)
        => _workspaces.SwitchWorkspaceAsync(workspaceId);

    /// <inheritdoc />
    public Task<WorkspaceInfo?> FindByRootPathAsync(string rootPath, CancellationToken ct = default)
        => _workspaces.GetByRootPathAsync(rootPath);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkspaceInfo>> SearchAsync(string keyword, CancellationToken ct = default)
        => _workspaces.SearchByNameAsync(keyword);

    /// <inheritdoc />
    public async Task GrantAccessAsync(string workspaceId, CancellationToken ct = default)
    {
        var workspace = await _workspaces.GetWorkspaceAsync(workspaceId);
        if (workspace is null) return;

        await _workspaces.EnsureAuthorizedAsync(workspace);
    }
}