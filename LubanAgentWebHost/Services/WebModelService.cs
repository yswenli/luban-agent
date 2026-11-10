/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebModelService
*描述：模型设置业务（浏览器端，按供应商列出与维护自定义模型）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 模型设置业务。
/// </summary>
/// <remarks>
/// 只读本地清单（内置预设 + 缓存 + 自定义），不触发远端刷新；刷新走 <see cref="WebProviderService.ModelsAsync"/>。
/// 全局模型选择走 <see cref="WebProviderService.SelectAsync"/>。
/// </remarks>
public static class WebModelService
{
    /// <summary>按供应商列出模型清单。</summary>
    /// <param name="providerId">供应商名；缺省返回全部供应商。</param>
    /// <returns>按供应商分组的模型清单。</returns>
    public static Task<ActionResult> ListAsync(string? providerId) => WebApi.RunGuardedAsync(async () =>
    {
        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = requestScope.GetRequiredService<IProviderModelManagementService>();
        var (selectedProvider, selectedModel) = service.GetSelection();

        var providers = await service.GetProvidersAsync().ConfigureAwait(false);
        var groups = new List<ProviderModelsDto>();
        foreach (var provider in providers)
        {
            if (!string.IsNullOrWhiteSpace(providerId)
                && !string.Equals(provider.Name, providerId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var models = await service.GetModelsAsync(provider.Name).ConfigureAwait(false);
            var items = models
                .Select(model => new ModelItemDto(
                    model,
                    provider.CustomModels.Contains(model),
                    string.Equals(selectedProvider, provider.Name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(selectedModel, model, StringComparison.Ordinal)))
                .ToList();
            groups.Add(new ProviderModelsDto(
                provider.Name,
                string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Name : provider.DisplayName!,
                items));
        }

        return WebApi.OkJson(new { providers = groups });
    });

    /// <summary>新增自定义模型。</summary>
    /// <param name="payload">包装后的 <see cref="ModelAddRequest"/> JSON。</param>
    /// <returns>新增后的模型条目。</returns>
    public static Task<ActionResult> AddAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ModelAddRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.ProviderId) || string.IsNullOrWhiteSpace(input.ModelId))
        {
            return WebApi.FailJson(400, "providerId / modelId 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        await requestScope.GetRequiredService<IProviderModelManagementService>()
            .AddModelAsync(input.ProviderId.Trim(), input.ModelId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson(new ModelItemDto(input.ModelId.Trim(), true, false));
    });

    /// <summary>重命名自定义模型。</summary>
    /// <param name="payload">包装后的 <see cref="ModelRenameRequest"/> JSON。</param>
    /// <returns>重命名后的模型条目。</returns>
    public static Task<ActionResult> RenameAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ModelRenameRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.ProviderId)
            || string.IsNullOrWhiteSpace(input.OldModelId)
            || string.IsNullOrWhiteSpace(input.NewModelId))
        {
            return WebApi.FailJson(400, "providerId / oldModelId / newModelId 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = requestScope.GetRequiredService<IProviderModelManagementService>();
        await service.UpdateModelAsync(input.ProviderId.Trim(), input.OldModelId.Trim(), input.NewModelId.Trim())
            .ConfigureAwait(false);

        var (selectedProvider, selectedModel) = service.GetSelection();
        return WebApi.OkJson(new ModelItemDto(
            input.NewModelId.Trim(),
            true,
            string.Equals(selectedProvider, input.ProviderId.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(selectedModel, input.NewModelId.Trim(), StringComparison.Ordinal)));
    });

    /// <summary>删除自定义模型。</summary>
    /// <param name="payload">包装后的 <see cref="ModelTargetRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    public static Task<ActionResult> DeleteAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ModelTargetRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.ProviderId) || string.IsNullOrWhiteSpace(input.ModelId))
        {
            return WebApi.FailJson(400, "providerId / modelId 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        await requestScope.GetRequiredService<IProviderModelManagementService>()
            .DeleteModelAsync(input.ProviderId.Trim(), input.ModelId.Trim())
            .ConfigureAwait(false);
        return WebApi.OkJson();
    });
}