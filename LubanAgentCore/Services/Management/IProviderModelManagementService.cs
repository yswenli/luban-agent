namespace LubanAgentCore.Services.Management;

/// <summary>
/// Provider / 模型管理（CLI 与 Codex 共用的唯一入口）。
/// 方法面 = 现有 <c>/model</c>、<c>/provider</c> 命令与 Codex 设置页的能力，不新增能力。
/// </summary>
public interface IProviderModelManagementService
{
    /// <summary>全部 Provider（含显示名与端点模型）。</summary>
    Task<IReadOnlyList<ProviderInfo>> GetProvidersAsync(CancellationToken ct = default);

    /// <summary>指定 Provider 的可选模型（优先读缓存，缺失时回退静态表）。</summary>
    Task<IReadOnlyList<string>> GetModelsAsync(string providerId, CancellationToken ct = default);

    /// <summary>从远端刷新指定 Provider 的模型列表并写回缓存。</summary>
    Task<IReadOnlyList<string>> RefreshModelsAsync(string providerId, CancellationToken ct = default);

    /// <summary>当前选中的模型（<c>provider:model</c>）。</summary>
    (string? ProviderId, string? ModelId) GetSelection();

    /// <summary>切换当前模型并持久化；调用方随后负责重置会话运行时。</summary>
    Task SetSelectionAsync(string providerId, string modelId, CancellationToken ct = default);

    /// <summary>新增或更新 Provider 配置。</summary>
    Task UpsertProviderAsync(ProviderConfig config, CancellationToken ct = default);

    /// <summary>删除 Provider 配置。</summary>
    Task DeleteProviderAsync(string providerId, CancellationToken ct = default);
}