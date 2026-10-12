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

    /// <summary>从远程端点刷新 Provider 的模型列表，失败时回退到本地模型列表。</summary>
    public Task<List<string>> RefreshModelsAsync(string providerName, string apiKey, string? baseUrl = null, CancellationToken cancellationToken = default)
        => ProviderHelper.RefreshModelsAsync(providerName, apiKey, _cache, baseUrl, cancellationToken);
}