/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Configuration
*文件名： ProviderCache
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/12
*描述：按用户隔离的 Provider 可变缓存（远程刷新得到的模型列表）。
*
*=================================================
*修改标记
*修改时间：2026/10/12
*修改人： yswenli
*版本号： V1.0.0.0
*描述：按用户隔离的 Provider 可变缓存（远程刷新得到的模型列表）。
*
*****************************************************************************/

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