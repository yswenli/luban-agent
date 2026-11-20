/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： ProviderModelManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：Provider / 模型管理的唯一实现（CLI 与 Codex 共用）。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：Provider / 模型管理的唯一实现（CLI 与 Codex 共用）。
*
*****************************************************************************/

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

    /// <inheritdoc />
    public Task<ModelProfile?> GetModelProfileAsync(string providerId, string modelId, CancellationToken ct = default)
        => Task.FromResult(_config.GetModelProfile(BuildModelKey(providerId, modelId)));

    /// <inheritdoc />
    public Task SetModelProfileAsync(
        string providerId,
        string modelId,
        ModelProfile? profile,
        CancellationToken ct = default)
    {
        _config.SetModelProfile(BuildModelKey(providerId, modelId), Sanitize(profile));
        return Task.CompletedTask;
    }

    /// <summary>拼 <c>provider:model</c> 键；任一为空时返回空串（由配置层拒绝写入）。</summary>
    private static string BuildModelKey(string? providerId, string? modelId)
        => string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId)
            ? string.Empty
            : $"{providerId.Trim()}:{modelId.Trim()}";

    /// <summary>
    /// 收敛档案：档位只留标准链路可发的枚举名（None/Low/Medium/High/ExtraHigh），
    /// 窗口非正数一律视为未知；全空的用户档案按「删除登记」处理。
    /// </summary>
    private static ModelProfile? Sanitize(ModelProfile? profile)
    {
        if (profile is null) return null;

        var reasoning = profile.Reasoning?
            .Select(item => item?.Trim())
            .Where(item => !string.IsNullOrEmpty(item))
            .Select(item => Enum.TryParse<ReasoningEffort>(item!, true, out var effort) ? effort : (ReasoningEffort?)null)
            .Where(effort => effort is not null && IsSendable(effort.Value))
            .Select(effort => effort!.Value.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // 按枚举值排序，落盘结果不受用户勾选顺序影响
            .OrderBy(effort => Enum.Parse<ReasoningEffort>(effort))
            .ToList();

        if (profile.Reasoning is { Count: 0 })
        {
            // 明确登记「不支持」：保留空集合，不能退化成未知
            reasoning = [];
        }
        else if (reasoning is { Count: 0 })
        {
            // 给了档位但全部非法：按未知处理，不把「用户填错」解释成「模型不支持」
            reasoning = null;
        }

        var window = profile.ContextWindow is > 0 ? profile.ContextWindow : null;

        return reasoning is null && window is null
            ? null
            : new ModelProfile { Reasoning = reasoning, ContextWindow = window };
    }

    /// <summary>标准 <c>reasoning_effort</c> 链路能发出的档位。</summary>
    private static bool IsSendable(ReasoningEffort effort)
        => effort is ReasoningEffort.None or ReasoningEffort.Low or ReasoningEffort.Medium
            or ReasoningEffort.High or ReasoningEffort.ExtraHigh;

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