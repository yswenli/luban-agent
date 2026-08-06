using LuBan.AIAgent.Providers;
using Microsoft.Extensions.AI;

namespace LubanAgent.Services;

public class LuBanChatClient : IProviderRouter, IChatClient
{
    private readonly Dictionary<string, IChatClient> _providers;
    private readonly string _defaultProvider;
    private int _disposedInt;

    public LuBanChatClient(
        IEnumerable<KeyValuePair<string, IChatClient>> providers,
        string defaultProvider = "openai")
    {
        _providers = providers?.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value)
            ?? new Dictionary<string, IChatClient>();
        _defaultProvider = defaultProvider.ToLowerInvariant();
    }

    public IChatClient CreateChatClient(string? providerModel = null)
    {
        if (string.IsNullOrEmpty(providerModel))
            return GetProvider(null);
        return GetProvider(providerModel);
    }

    public IReadOnlyList<ProviderInfo> GetAvailableProviders()
    {
        return _providers.Select(p => new ProviderInfo(p.Key, p.Key, Array.Empty<string>())).ToList();
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(options?.ModelId);
        return provider.GetResponseAsync(messages, options, cancellationToken);
    }

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
