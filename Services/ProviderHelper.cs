using LubanAgent.Model;
using Microsoft.Extensions.Configuration;

namespace LubanAgent.Services;

public static class ProviderHelper
{
    private static Dictionary<string, ExtendedProviderInfo> _providerConfigs = new();
    private static bool _initialized = false;

    public static void Initialize(IConfiguration configuration)
    {
        var providers = configuration.GetSection("LuBanAgent:Providers")
            .Get<Dictionary<string, ExtendedProviderInfo>>();
        if (providers != null)
        {
            _providerConfigs = providers;
        }
        _initialized = true;
    }

    public static string GetDisplayName(string providerName)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("ProviderHelper not initialized. Call Initialize() first.");
        }

        if (_providerConfigs.TryGetValue(providerName, out var config) && !string.IsNullOrEmpty(config.DisplayName))
        {
            return config.DisplayName;
        }
        return providerName;
    }

    public static List<ProviderEndpointInfo> GetEndpoints(string providerName)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("ProviderHelper not initialized. Call Initialize() first.");
        }

        if (_providerConfigs.TryGetValue(providerName, out var config) && config.Endpoints.Count > 0)
        {
            return config.Endpoints;
        }
        return new List<ProviderEndpointInfo>();
    }

    public static List<string> GetModels(string providerName)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("ProviderHelper not initialized. Call Initialize() first.");
        }

        if (_providerConfigs.TryGetValue(providerName, out var config) && config.Models.Count > 0)
        {
            return config.Models;
        }
        return new List<string>();
    }

    public static List<string> GetAllModels(string providerName, List<string>? customModels = null)
    {
        var models = new HashSet<string>(GetModels(providerName));
        if (customModels != null)
        {
            foreach (var model in customModels)
            {
                models.Add(model);
            }
        }
        return models.ToList();
    }

    public static bool HasMultipleEndpoints(string providerName)
    {
        return GetEndpoints(providerName).Count > 1;
    }
}
