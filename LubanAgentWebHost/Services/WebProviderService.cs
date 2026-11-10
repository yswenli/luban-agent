/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebProviderService
*描述：供应商与模型业务（浏览器端目前只用到「全局模型选择」读写）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 供应商与模型业务。
/// </summary>
public static class WebProviderService
{
    /// <summary>读取全局模型选择。</summary>
    /// <returns><c>{ providerId, modelId }</c>；未设置时两者为 null。</returns>
    public static Task<ActionResult> SelectionAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (providerId, modelId) = scope.GetRequiredService<IProviderModelManagementService>().GetSelection();
        return WebApi.OkJson(new ModelSelectionDto(providerId, modelId));
    });

    /// <summary>
    /// 拉取指定供应商的可用模型。
    /// </summary>
    /// <remarks>
    /// 与桌面端设置页「刷新模型」同一入口：有 API Key 时向远端刷新并写入该用户缓存，失败自动回落本地列表；
    /// 没有只读缓存接口可用，所以首屏 bootstrap 拿到的模型可能是空的（缓存未建），由前端在需要时调用本接口补齐。
    /// </remarks>
    /// <param name="providerId">供应商名（即 <c>ProviderConfig.Name</c>）。</param>
    /// <returns><c>{ providerId, models }</c>。</returns>
    public static Task<ActionResult> ModelsAsync(string? providerId) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return WebApi.FailJson(400, "providerId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var models = await scope.GetRequiredService<IProviderModelManagementService>()
            .RefreshModelsAsync(providerId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new { providerId = providerId.Trim(), models });
    });

    /// <summary>切换全局模型选择。</summary>
    /// <param name="payload">包装后的 <see cref="ModelSelectRequest"/> JSON。</param>
    /// <returns>切换后的选择。</returns>
    public static Task<ActionResult> SelectAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ModelSelectRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.ProviderId) || string.IsNullOrWhiteSpace(input.ModelId))
        {
            return WebApi.FailJson(400, "providerId / modelId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<IProviderModelManagementService>();
        await service.SetSelectionAsync(input.ProviderId.Trim(), input.ModelId.Trim()).ConfigureAwait(false);
        var (providerId, modelId) = service.GetSelection();
        return WebApi.OkJson(new ModelSelectionDto(providerId, modelId));
    });

    /// <summary>
    /// 列出供应商（API Key 掩码）。
    /// </summary>
    /// <returns>供应商清单。</returns>
    public static Task<ActionResult> ListAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<IProviderModelManagementService>();
        var (selectedProvider, _) = service.GetSelection();
        var providers = await service.GetProvidersAsync().ConfigureAwait(false);

        var items = providers
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ProviderListItemDto(
                p.Name,
                string.IsNullOrWhiteSpace(p.DisplayName) ? p.Name : p.DisplayName!,
                p.BaseUrl,
                SecretMasker.MaskApiKey(p.ApiKey),
                p.NetworkTimeoutSeconds,
                p.CustomModels.ToList(),
                string.Equals(selectedProvider, p.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return WebApi.OkJson(new { items });
    });

    /// <summary>
    /// 新建或更新供应商。
    /// </summary>
    /// <remarks>
    /// 与桌面端设置页同一语义：同名只更新密钥与基址，显示名 / 超时 / 自定义模型按表单差值落盘；
    /// <see cref="ProviderSaveRequest.ApiKey"/> 留空表示沿用已有密钥（新建时不接受留空）。
    /// </remarks>
    /// <param name="payload">包装后的 <see cref="ProviderSaveRequest"/> JSON。</param>
    /// <returns>保存后的供应商项（密钥仍为掩码）。</returns>
    public static Task<ActionResult> SaveAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ProviderSaveRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        var name = input.Name.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name))
        {
            return WebApi.FailJson(400, "Name 不能为空");
        }

        var apiKey = input.ApiKey?.Trim() ?? string.Empty;
        var baseUrl = string.IsNullOrWhiteSpace(input.BaseUrl) ? null : input.BaseUrl.Trim();

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var config = scope.GetRequiredService<ConfigManager>();
        var existing = config.GetProvider(name);
        if (existing is null && string.IsNullOrEmpty(apiKey))
        {
            return WebApi.FailJson(400, "新建供应商时 API Key 不能为空");
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            config.AddProvider(name, apiKey, baseUrl);
        }
        else
        {
            existing!.BaseUrl = baseUrl;
        }

        var provider = config.GetProvider(name)!;
        provider.DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim();
        provider.NetworkTimeoutSeconds = input.NetworkTimeoutSeconds > 0 ? input.NetworkTimeoutSeconds : null;

        if (input.CustomModels is not null)
        {
            var desired = input.CustomModels
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (var model in desired.Where(m => !provider.CustomModels.Contains(m)).ToList())
            {
                config.AddCustomModel(name, model);
            }

            foreach (var model in provider.CustomModels.Where(m => !desired.Contains(m)).ToList())
            {
                config.RemoveCustomModel(name, model);
            }
        }

        config.Save();

        var (selectedProvider, _) = scope.GetRequiredService<IProviderModelManagementService>().GetSelection();
        return WebApi.OkJson(new ProviderListItemDto(
            provider.Name,
            string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Name : provider.DisplayName!,
            provider.BaseUrl,
            SecretMasker.MaskApiKey(provider.ApiKey),
            provider.NetworkTimeoutSeconds,
            provider.CustomModels.ToList(),
            string.Equals(selectedProvider, provider.Name, StringComparison.OrdinalIgnoreCase)));
    });

    /// <summary>删除供应商；当前模型属于该供应商时一并清空选择。</summary>
    /// <param name="payload">包装后的 <see cref="ProviderDeleteRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    public static Task<ActionResult> DeleteAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ProviderDeleteRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return WebApi.FailJson(400, "Name 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        await scope.GetRequiredService<IProviderModelManagementService>()
            .DeleteProviderAsync(input.Name.Trim().ToLowerInvariant())
            .ConfigureAwait(false);
        return WebApi.OkJson();
    });
}