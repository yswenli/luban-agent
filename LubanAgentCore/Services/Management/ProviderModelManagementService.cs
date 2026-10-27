namespace LubanAgentCore.Services.Management;

/// <summary>Provider / 模型管理的唯一实现（CLI 与 Codex 共用）。</summary>
public sealed class ProviderModelManagementService : IProviderModelManagementService
{
    private const string Source = "ModelCommand / ProviderCommand / Codex 设置页";

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 14", Source);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetModelsAsync(string providerId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 14", Source);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> RefreshModelsAsync(string providerId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 14", Source);

    /// <inheritdoc />
    public (string? ProviderId, string? ModelId) GetSelection()
        => throw NotYetImplemented.For("Task 14", Source);

    /// <inheritdoc />
    public Task SetSelectionAsync(string providerId, string modelId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 14", Source);

    /// <inheritdoc />
    public Task UpsertProviderAsync(ProviderConfig config, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 14", Source);

    /// <inheritdoc />
    public Task DeleteProviderAsync(string providerId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 14", Source);
}