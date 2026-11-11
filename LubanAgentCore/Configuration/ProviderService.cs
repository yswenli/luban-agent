/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Configuration
*文件名： ProviderService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/12
*描述：按当前用户隔离的 Provider 门面：包装 <see cref="ProviderCache"/>， 使调用点无需直接操作缓存即可读写刷新得到的模型列表。 由 DI 按 <c>userId</c> keyed 注册。
*
*=================================================
*修改标记
*修改时间：2026/10/12
*修改人： yswenli
*版本号： V1.0.0.0
*描述：按当前用户隔离的 Provider 门面：包装 <see cref="ProviderCache"/>， 使调用点无需直接操作缓存即可读写刷新得到的模型列表。 由 DI 按 <c>userId</c> keyed 注册。
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 按当前用户隔离的 Provider 门面：包装 <see cref="ProviderCache"/>，
/// 使调用点无需直接操作缓存即可读写刷新得到的模型列表。
/// 由 DI 按 <c>userId</c> keyed 注册。
/// </summary>
public sealed class ProviderService
{
    private readonly ProviderCache _cache;

    /// <summary>构造。</summary>
    /// <param name="cache">当前用户的可变缓存。</param>
    public ProviderService(ProviderCache cache)
        => _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    /// <summary>获取 Provider 的模型列表（内置预设与刷新获取的模型）。</summary>
    public List<string> GetModels(string providerName) => ProviderHelper.GetModels(providerName, _cache);

    /// <summary>获取 Provider 的全部模型（内置预设、刷新获取及用户自定义模型），并去重。</summary>
    public List<string> GetAllModels(string providerName, List<string>? customModels = null)
        => ProviderHelper.GetAllModels(providerName, _cache, customModels);

    /// <summary>把刷新得到的模型列表从旧名称迁移到新名称（重命名 Provider 时用）。</summary>
    /// <param name="oldProviderName">原名称</param>
    /// <param name="newProviderName">新名称</param>
    public void MoveFetchedModels(string oldProviderName, string newProviderName)
    {
        if (string.IsNullOrWhiteSpace(oldProviderName) || string.IsNullOrWhiteSpace(newProviderName))
            return;

        var models = _cache.GetFetched(oldProviderName);
        if (models.Count > 0)
            _cache.SetFetched(newProviderName, models.ToList());
    }

    /// <summary>从远程端点刷新 Provider 的模型列表，失败时回退到本地模型列表。</summary>
    public Task<List<string>> RefreshModelsAsync(string providerName, string apiKey, string? baseUrl = null, CancellationToken cancellationToken = default)
        => ProviderHelper.RefreshModelsAsync(providerName, apiKey, _cache, baseUrl, cancellationToken);
}