/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*Author：yswenli
*命名空间：LubanAgent.Configuration
*文件名： LuBanChatClient
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：多 Provider 路由的聊天客户端，按模型标识分发到对应 Provider
*
*****************************************************************************/
using LuBan.AIAgent.Configuration;
using Microsoft.Extensions.AI;

namespace LubanAgent.Configuration;

/// <summary>
/// 多 Provider 路由的聊天客户端，根据模型标识（providerName:modelName）将请求分发到对应的 Provider
/// </summary>
public class LuBanChatClient : IProviderRouter, IChatClient
{
    private readonly Dictionary<string, IChatClient> _providers;
    private readonly string _defaultProvider;
    private int _disposedInt;

    /// <summary>
    /// 创建 LuBanChatClient 实例
    /// </summary>
    /// <param name="providers">Provider 名称与聊天客户端的键值对集合</param>
    /// <param name="defaultProvider">默认 Provider 名称，默认为 openai</param>
    public LuBanChatClient(
        IEnumerable<KeyValuePair<string, IChatClient>> providers,
        string defaultProvider = "openai")
    {
        _providers = providers?.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value)
            ?? new Dictionary<string, IChatClient>();
        _defaultProvider = defaultProvider.ToLowerInvariant();
    }

    /// <summary>
    /// 根据模型标识创建或获取对应 Provider 的聊天客户端
    /// </summary>
    /// <param name="providerModel">模型标识（格式：providerName:modelName），为空时使用默认 Provider</param>
    /// <returns>对应的聊天客户端</returns>
    public IChatClient CreateChatClient(string? providerModel = null)
    {
        if (string.IsNullOrEmpty(providerModel))
            return GetProvider(null);
        return GetProvider(providerModel);
    }

    /// <summary>
    /// 获取所有已注册的 Provider 信息
    /// </summary>
    /// <returns>Provider 信息列表</returns>
    public IReadOnlyList<ProviderInfo> GetAvailableProviders()
    {
        return _providers.Select(p => new ProviderInfo(p.Key, p.Key, Array.Empty<string>())).ToList();
    }

    /// <summary>
    /// 发送聊天请求并获取完整响应，请求被路由到对应 Provider
    /// </summary>
    /// <param name="messages">聊天消息列表</param>
    /// <param name="options">聊天选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天响应</returns>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(options?.ModelId);
        return provider.GetResponseAsync(messages, options, cancellationToken);
    }

    /// <summary>
    /// 发送聊天请求并返回流式响应更新，请求被路由到对应 Provider
    /// </summary>
    /// <param name="messages">聊天消息列表</param>
    /// <param name="options">聊天选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天响应更新的可异步枚举</returns>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(options?.ModelId);
        await foreach (var update in provider.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// 在所有 Provider 中查找指定类型的服务
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="key">可选的服务键</param>
    /// <returns>找到的服务实例；未找到时返回 null</returns>
    public object? GetService(Type serviceType, object? key = null)
    {
        foreach (var provider in _providers.Values)
        {
            var service = provider.GetService(serviceType, key);
            if (service != null)
                return service;
        }
        return null;
    }

    /// <summary>
    /// 释放所有 Provider 资源
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedInt, 1) == 0)
        {
            foreach (var provider in _providers.Values)
            {
                try { provider.Dispose(); } catch { }
            }
            _providers.Clear();
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 根据模型标识解析并获取对应的 Provider 聊天客户端
    /// </summary>
    /// <param name="modelId">模型标识，为空时使用默认 Provider</param>
    /// <returns>对应 Provider 的聊天客户端</returns>
    private IChatClient GetProvider(string? modelId)
    {
        var providerName = _defaultProvider;

        if (!string.IsNullOrEmpty(modelId))
        {
            var parts = modelId.Split(':', 2);
            if (parts.Length == 2)
                providerName = parts[0].ToLowerInvariant();
        }

        if (_providers.TryGetValue(providerName, out var provider))
            return provider;

        throw new InvalidOperationException($"Provider '{providerName}' not found");
    }
}
