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

namespace LubanAgent.Configuration;

public class LuBanChatClient : IProviderRouter, IChatClient
{
    private readonly Dictionary<string, (OpenAIClient Client, OpenAIClientOptions? Options)> _openAIClients;
    private readonly Dictionary<string, IChatClient> _cachedClients;
    private readonly string _defaultProvider;
    private int _disposedInt;

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

    public IReadOnlyList<ProviderInfo> GetAvailableProviders()
    {
        return _openAIClients.Select(p => new ProviderInfo(p.Key, p.Key, Array.Empty<string>())).ToList();
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chatClient = CreateChatClient(options?.ModelId);
        var cleanOptions = RemoveProviderPrefix(options);
        return chatClient.GetResponseAsync(messages, cleanOptions, cancellationToken);
    }

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

    public void Dispose()
    {
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

    private ChatOptions? RemoveProviderPrefix(ChatOptions? options)
    {
        if (options == null || string.IsNullOrEmpty(options.ModelId))
            return options;

        var parts = options.ModelId.Split(':', 2);
        if (parts.Length == 2)
        {
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