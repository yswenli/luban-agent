using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>LLM Wiki 索引管理实现。</summary>
public sealed class LlmWikiManagementService : ILlmWikiManagementService
{
    private const string Source = "LlmWikiCommand";

    /// <inheritdoc />
    public Task<WikiStatus> GetStatusAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task RebuildAsync(string workspaceId, IProgress<double>? progress, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task ClearAsync(string workspaceId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);
}