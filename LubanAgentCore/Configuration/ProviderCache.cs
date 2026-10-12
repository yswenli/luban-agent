namespace LubanAgentCore.Configuration;

/// <summary>按用户隔离的 Provider 可变缓存（远程刷新得到的模型列表）。</summary>
public sealed class ProviderCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> _fetchedModels
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>获取已刷新的模型列表；无记录时返回空集合。</summary>
    public IReadOnlyList<string> GetFetched(string providerKey)
        => _fetchedModels.TryGetValue(providerKey, out var v) ? v : Array.Empty<string>();

    /// <summary>记录刷新得到的模型列表。</summary>
    public void SetFetched(string providerKey, List<string> models) => _fetchedModels[providerKey] = models;
}