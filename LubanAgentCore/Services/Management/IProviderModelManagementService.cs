/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： IProviderModelManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：Provider / 模型管理（CLI 与 Codex 共用的唯一入口）。 方法面 = 现有 <c>/model</c>、<c>/provider</c> 命令与 Codex 设置页的能力，不新增能力。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：Provider / 模型管理（CLI 与 Codex 共用的唯一入口）。 方法面 = 现有 <c>/model</c>、<c>/provider</c> 命令与 Codex 设置页的能力，不新增能力。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// Provider / 模型管理（CLI 与 Codex 共用的唯一入口）。
/// 方法面 = 现有 <c>/model</c>、<c>/provider</c> 命令与 Codex 设置页的能力，不新增能力。
/// </summary>
public interface IProviderModelManagementService
{
    /// <summary>全部 Provider 配置快照（含 API Key / BaseUrl / 自定义模型，供列表、刷新与设置页使用）。</summary>
    Task<IReadOnlyList<ProviderConfig>> GetProvidersAsync(CancellationToken ct = default);

    /// <summary>指定 Provider 的可选模型（内置预设 + 远端刷新缓存 + 用户自定义）。</summary>
    Task<IReadOnlyList<string>> GetModelsAsync(string providerId, CancellationToken ct = default);

    /// <summary>从远端刷新指定 Provider 的模型列表；未配置 API Key 时直接返回本地列表。</summary>
    /// <remarks>刷新失败由调用方决定如何提示（CLI 回退到本地预定义模型）。</remarks>
    Task<IReadOnlyList<string>> RefreshModelsAsync(string providerId, CancellationToken ct = default);

    /// <summary>当前选中的模型（<c>provider:model</c> 拆分为二元组；未选中返回 <c>(null, null)</c>）。</summary>
    (string? ProviderId, string? ModelId) GetSelection();

    /// <summary>切换当前模型并持久化；对已建会话的运行时**不**做重建（与原命令行为一致）。</summary>
    Task SetSelectionAsync(string providerId, string modelId, CancellationToken ct = default);

    /// <summary>新增或更新 Provider 配置（已存在时更新 API Key 与 BaseUrl，保留其自定义模型）。</summary>
    Task UpsertProviderAsync(ProviderConfig config, CancellationToken ct = default);

    /// <summary>删除 Provider 配置（删除后若当前模型属于该 Provider，会清空模型选择）。</summary>
    Task DeleteProviderAsync(string providerId, CancellationToken ct = default);

    /// <summary>为 Provider 添加自定义模型。</summary>
    Task AddModelAsync(string providerId, string modelId, CancellationToken ct = default);

    /// <summary>重命名 Provider 的自定义模型（当前选中项随之更新）。</summary>
    Task UpdateModelAsync(string providerId, string oldModelId, string newModelId, CancellationToken ct = default);

    /// <summary>删除 Provider 的自定义模型（被选中时同时清除选择）。</summary>
    Task DeleteModelAsync(string providerId, string modelId, CancellationToken ct = default);
}