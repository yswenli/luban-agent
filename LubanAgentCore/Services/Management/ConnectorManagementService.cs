using LuBan.AIAgent.Connectors;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>连接器管理实现。</summary>
public sealed class ConnectorManagementService : IConnectorManagementService
{
    private const string Source = "Codex 设置页连接器页";

    /// <inheritdoc />
    public Task<IReadOnlyList<ConnectorStatus>> ListAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);

    /// <inheritdoc />
    public Task AddAsync(ConnectorConfig config, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);

    /// <inheritdoc />
    public Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);

    /// <inheritdoc />
    public Task<ConnectorTestResult> TestAsync(string name, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);
}