using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>规则管理实现（内置 + 自定义）。</summary>
public sealed class RuleManagementService : IRuleManagementService
{
    private const string Source = "RuleCommand / Codex 设置页 Rule 页";

    /// <inheritdoc />
    public Task<IReadOnlyList<RuleInfo>> ListAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 15", Source);

    /// <inheritdoc />
    public Task AddAsync(string name, string content, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 15", Source);

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 15", Source);

    /// <inheritdoc />
    public Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 15", Source);
}