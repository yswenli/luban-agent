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
using OpenAI;

namespace LubanAgentCli.Configuration;

/// <summary>
/// 多 Provider 路由的聊天客户端，按模型标识（providerName:modelName）分发请求到对应的 OpenAI Provider，
/// 并缓存已创建的 IChatClient 实例以避免重复创建
/// </summary>
public class LuBanChatClient : IProviderRouter, IChatClient
{
    private readonly Dictionary<string, (OpenAIClient Client, OpenAIClientOptions? Options)> _openAIClients;
    private readonly Dictionary<string, IChatClient> _cachedClients;
    private readonly string _defaultProvider;
    private int _disposedInt;

    /// <summary>
    /// 创建多 Provider 路由聊天客户端实例
    /// </summary>
    /// <param name="openAIClients">各 Provider 对应的 OpenAI 客户端及其配置</param>
    /// <param name="defaultProvider">默认 Provider 名称，省略模型前缀时使用此 Provider</param>
    public LuBanChatClient(
        IEnumerable<KeyValuePair<string, (OpenAIClient Client, OpenAIClientOptions? Options)>> openAIClients,
        string defaultProvider = "openai")
    {
        _openAIClients = openAIClients?.ToDictionary(
            p => p.Key.ToLowerInvariant(),
            p => p.Value) ?? new Dictionary<string, (OpenAIClient, OpenAIClientOptions?)>();
        _cachedClients = new Dictionary<string, IChatClient>();
        _defaultProvider = defaultProvider.ToLowerInvariant();
    }

    /// <summary>
    /// 根据模型标识创建或获取缓存的聊天客户端
    /// </summary>
    /// <param name="providerModel">模型标识（格式：providerName:modelName），为空时使用默认 Provider</param>
    /// <returns>对应模型的 IChatClient 实例</returns>
    /// <exception cref="InvalidOperationException">Provider 不存在或模型名称为空时抛出</exception>
    public IChatClient CreateChatClient(string? providerModel = null)
    {
        var (providerName, modelName) = ParseModelId(providerModel);
        
        if (!_openAIClients.TryGetValue(providerName, out var clientInfo))
            throw new InvalidOperationException($"Provider '{providerName}' not found");

        if (string.IsNullOrEmpty(modelName))
            throw new InvalidOperationException($"Model name is required in '{providerModel}'");

        var cacheKey = $"{providerName}:{modelName}";
        if (_cachedClients.TryGetValue(cacheKey, out var cached))
            return cached;

        var chatClient = clientInfo.Client.GetChatClient(modelName).AsIChatClient();
        _cachedClients[cacheKey] = chatClient;
        return chatClient;
    }

    /// <summary>
    /// 获取所有已注册的可用 Provider 列表
    /// </summary>
    /// <returns>Provider 信息列表</returns>
    public IReadOnlyList<ProviderInfo> GetAvailableProviders()
    {
        return _openAIClients.Select(p => new ProviderInfo(p.Key, p.Key, Array.Empty<string>())).ToList();
    }

    /// <summary>
    /// 发送消息并获取完整响应，自动根据 ModelId 路由到对应 Provider
    /// </summary>
    /// <param name="messages">聊天消息列表</param>
    /// <param name="options">聊天选项，ModelId 用于路由</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>完整的聊天响应</returns>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chatClient = CreateChatClient(options?.ModelId);
        var cleanOptions = RemoveProviderPrefix(options);
        return chatClient.GetResponseAsync(messages, cleanOptions, cancellationToken);
    }

    /// <summary>
    /// 发送消息并获取流式响应，自动根据 ModelId 路由到对应 Provider
    /// </summary>
    /// <param name="messages">聊天消息列表</param>
    /// <param name="options">聊天选项，ModelId 用于路由</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>流式聊天响应更新</returns>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatClient = CreateChatClient(options?.ModelId);
        var cleanOptions = RemoveProviderPrefix(options);
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cleanOptions, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// 从已缓存的客户端中获取指定类型的服务
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="key">服务键，可省略</param>
    /// <returns>服务实例；未找到时返回 null</returns>
    public object? GetService(Type serviceType, object? key = null)
    {
        foreach (var client in _cachedClients.Values)
        {
            var service = client.GetService(serviceType, key);
            if (service != null)
                return service;
        }
        return null;
    }

    /// <summary>
    /// 释放所有缓存的聊天客户端资源，确保只释放一次
    /// </summary>
    public void Dispose()
    {
        // 使用原子操作确保只释放一次
        if (Interlocked.Exchange(ref _disposedInt, 1) == 0)
        {
            foreach (var client in _cachedClients.Values)
            {
                try { client.Dispose(); } catch { }
            }
            _cachedClients.Clear();
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 解析模型标识，拆分为 Provider 名称和模型名称
    /// </summary>
    /// <param name="modelId">模型标识（格式：providerName:modelName），为空时使用默认 Provider</param>
    /// <returns>Provider 名称和模型名称的元组</returns>
    private (string providerName, string modelName) ParseModelId(string? modelId)
    {
        var providerName = _defaultProvider;
        var modelName = "";

        if (!string.IsNullOrEmpty(modelId))
        {
            var parts = modelId.Split(':', 2);
            if (parts.Length == 2)
            {
                providerName = parts[0].ToLowerInvariant();
                modelName = parts[1];
            }
        }

        return (providerName, modelName);
    }

    /// <summary>
    /// 移除 ChatOptions 中 ModelId 的 Provider 前缀，仅保留纯模型名称传递给下游客户端
    /// </summary>
    /// <param name="options">原始聊天选项</param>
    /// <returns>移除 Provider 前缀后的聊天选项；无需处理时返回原对象</returns>
    private ChatOptions? RemoveProviderPrefix(ChatOptions? options)
    {
        if (options == null || string.IsNullOrEmpty(options.ModelId))
            return options;

        var parts = options.ModelId.Split(':', 2);
        if (parts.Length == 2)
        {
            // 手动复制所有选项属性，仅将 ModelId 替换为去掉 Provider 前缀的纯模型名
            var cleaned = new ChatOptions
            {
                ModelId = parts[1],
                Instructions = options.Instructions,
                Temperature = options.Temperature,
                MaxOutputTokens = options.MaxOutputTokens,
                TopP = options.TopP,
                TopK = options.TopK,
                FrequencyPenalty = options.FrequencyPenalty,
                PresencePenalty = options.PresencePenalty,
                Seed = options.Seed,
                ResponseFormat = options.ResponseFormat,
                AllowMultipleToolCalls = options.AllowMultipleToolCalls,
                ToolMode = options.ToolMode
            };
            
            if (options.Tools != null && cleaned.Tools != null)
            {
                foreach (var tool in options.Tools)
                    cleaned.Tools.Add(tool);
            }
            
            if (options.AdditionalProperties != null && cleaned.AdditionalProperties != null)
            {
                foreach (var kvp in options.AdditionalProperties)
                    cleaned.AdditionalProperties[kvp.Key] = kvp.Value;
            }

            return cleaned;
        }

        return options;
    }
}