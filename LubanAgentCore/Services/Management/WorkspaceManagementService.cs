namespace LubanAgentCore.Services.Management;

/// <summary>工作区管理实现。</summary>
public sealed class WorkspaceManagementService : IWorkspaceManagementService
{
    private const string Source = "WorkCommand / Codex 工作区面板";

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkspaceInfo>> ListAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task<WorkspaceInfo?> GetCurrentAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task<WorkspaceInfo> CreateAsync(string name, string rootPath, string type, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task DeleteAsync(string workspaceId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task SwitchAsync(string workspaceId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task GrantAccessAsync(string workspaceId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);
}