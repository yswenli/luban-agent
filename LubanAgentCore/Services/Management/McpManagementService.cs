using LuBan.AIAgent.Tools.McpManage;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>MCP 客户端管理实现。</summary>
public sealed class McpManagementService : IMcpManagementService
{
    private const string Source = "MCPCommand / Codex 设置页 MCP 页";

    /// <inheritdoc />
    public Task<IReadOnlyList<McpInfo>> ListAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);

    /// <inheritdoc />
    public Task AddAsync(McpServerConfig config, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);

    /// <inheritdoc />
    public Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);

    /// <inheritdoc />
    public Task<McpTestResult> TestAsync(string name, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 16", Source);
}