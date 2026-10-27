namespace LubanAgentCore.Services.Management;

/// <summary>
/// Provider / 模型管理的唯一实现（CLI 与 Codex 共用）。
/// 业务规则全部委托 <see cref="ConfigManager"/>（持久化）与 <see cref="ProviderService"/>（按用户隔离的模型缓存），
/// 本类只负责「同一份逻辑只写一次」。
/// </summary>
public sealed class ProviderModelManagementService : IProviderModelManagementService
{
    private readonly ConfigManager _config;
    private readonly ProviderService _providers;

    /// <summary>构造。</summary>
    /// <param name="config">当前用户的配置管理器。</param>
    /// <param name="providers">当前用户的 Provider 门面（远端刷新缓存）。</param>
    public ProviderModelManagementService(ConfigManager config, ProviderService providers)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderConfig>> GetProvidersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProviderConfig>>(_config.Providers.Select(Clone).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetModelsAsync(string providerId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(GetAllModels(providerId));

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> RefreshModelsAsync(string providerId, CancellationToken ct = default)
    {
        var provider = _config.GetProvider(providerId)
            ?? throw new InvalidOperationException($"Provider '{providerId}' 不存在");

        // 与命令层原逻辑一致：没有 API Key 就不发请求，直接返回本地列表。
        if (string.IsNullOrEmpty(provider.ApiKey))
        {
            return GetAllModels(providerId);
        }

        await _providers.RefreshModelsAsync(provider.Name, provider.ApiKey, provider.BaseUrl, ct).ConfigureAwait(false);
        return GetAllModels(providerId);
    }

    /// <inheritdoc />
    public (string? ProviderId, string? ModelId) GetSelection()
    {
        var selected = _config.SelectedModel;
        if (string.IsNullOrWhiteSpace(selected))
        {
            return (null, null);
        }

        var parts = selected.Split(':', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (null, null);
    }

    /// <inheritdoc />
    public Task SetSelectionAsync(string providerId, string modelId, CancellationToken ct = default)
    {
        _config.SetSelectedModel($"{providerId}:{modelId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertProviderAsync(ProviderConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 复用既有语义：同名 Provider 只更新 API Key 与 BaseUrl，自定义模型等由别的入口维护。
        _config.AddProvider(config.Name, config.ApiKey, config.BaseUrl);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteProviderAsync(string providerId, CancellationToken ct = default)
    {
        var provider = _config.GetProvider(providerId)
            ?? throw new InvalidOperationException($"Provider '{providerId}' 不存在");

        _config.Providers.Remove(provider);
        _config.Save();

        if (_config.SelectedModel?.StartsWith($"{provider.Name}:", StringComparison.Ordinal) == true)
        {
            // 修正既有缺陷：原实现在此调用 SetSelectedModel("")，会抛 ArgumentException（模型不能为空），
            // 导致命令层 catch 后只打印红色错误、丢失成功文案（Provider 实际已删除并保存）。
            // 与 Codex 设置页删除供应商的实现保持一致：清空选择而非写入非法空值。
            _config.ClearSelectedModel();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddModelAsync(string providerId, string modelId, CancellationToken ct = default)
    {
        _config.AddCustomModel(providerId, modelId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(string providerId, string oldModelId, string newModelId, CancellationToken ct = default)
    {
        _config.UpdateCustomModel(providerId, oldModelId, newModelId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteModelAsync(string providerId, string modelId, CancellationToken ct = default)
    {
        _config.RemoveCustomModel(providerId, modelId);
        return Task.CompletedTask;
    }

    private List<string> GetAllModels(string providerId)
        => _providers.GetAllModels(providerId, _config.GetProvider(providerId)?.CustomModels);

    /// <summary>返回配置快照，避免调用方直接改动配置对象后忘记 <see cref="ConfigManager.Save"/>。</summary>
    private static ProviderConfig Clone(ProviderConfig source) => new()
    {
        Name = source.Name,
        ApiKey = source.ApiKey,
        BaseUrl = source.BaseUrl,
        DisplayName = source.DisplayName,
        SupportedModels = new List<string>(source.SupportedModels),
        CustomModels = new List<string>(source.CustomModels),
        NetworkTimeoutSeconds = source.NetworkTimeoutSeconds,
    };
}