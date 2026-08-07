using System.Collections.Concurrent;
using System.Text.Json;
using LubanAgent.Model;
using Microsoft.Extensions.Configuration;

namespace LubanAgent.Services;

public static class ProviderHelper
{
    private static Dictionary<string, ExtendedProviderInfo> _providerConfigs = new();
    private static bool _initialized = false;

    private static readonly ConcurrentDictionary<string, List<string>> _fetchedModels = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> _defaultBaseUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = "https://api.openai.com/v1/",
        ["deepseek"] = "https://api.deepseek.com/v1/",
        ["kimi"] = "https://api.moonshot.cn/v1/",
        ["glm"] = "https://open.bigmodel.cn/api/paas/v4/",
        ["qwen"] = "https://dashscope.aliyuncs.com/compatible-mode/v1/",
        ["doubao"] = "https://ark.cn-beijing.volces.com/api/v3/",
        ["ollama"] = "http://localhost:11434/v1/",
        ["ernie"] = "https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/",
        ["minimax"] = "https://api.minimax.chat/v1/",
        ["hunyuan"] = "https://api.hunyuan.cloud.tencent.com/v1/",
        ["mimo"] = "https://api.xiaomi.com/v1/",
        ["xai"] = "https://api.x.ai/v1/",
        ["qianfan"] = "https://qianfan.baidubce.com/v2/",
        ["tencent-ti"] = "https://api.lkeap.cloud.tencent.com/v1/",
        ["huawei-pangu"] = "https://pangu.huaweicloud.com/v1/",
        ["bedrock"] = "https://bedrock-runtime.us-east-1.amazonaws.com/",
        ["openrouter"] = "https://openrouter.ai/api/v1/"
    };

    private static readonly Dictionary<string, List<string>> _models = new()
    {
        ["openai"] = new List<string>
        {
            "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano",
            "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-4", "gpt-3.5-turbo",
            "o1", "o1-mini", "o3-mini"
        },
        ["azure"] = new List<string> { "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-4", "gpt-35-turbo" },
        ["deepseek"] = new List<string> { "deepseek-chat", "deepseek-coder", "deepseek-reasoner" },
        ["kimi"] = new List<string> { "k3", "k3-256k", "kimi-for-coding", "kimi-for-coding-highspeed" },
        ["glm"] = new List<string> { "glm-4-plus", "glm-4-0520", "glm-4-air", "glm-4-airx", "glm-4-flash", "glm-3-turbo" },
        ["qwen"] = new List<string> { "qwen-turbo", "qwen-plus", "qwen-max", "qwen-max-longcontext" },
        ["doubao"] = new List<string> { "doubao-pro-4k", "doubao-pro-32k", "doubao-pro-128k", "doubao-lite-4k" },
        ["claude"] = new List<string> { "claude-3-5-sonnet-20241022", "claude-3-5-haiku-20241022", "claude-3-opus-20240229", "claude-3-sonnet-20240229", "claude-3-haiku-20240307" },
        ["gemini"] = new List<string> { "gemini-2.0-flash", "gemini-1.5-pro", "gemini-1.5-flash", "gemini-1.5-flash-8b" },
        ["ollama"] = new List<string> { "llama3.1", "llama3.2", "qwen2.5", "deepseek-coder-v2", "codellama" },
        ["ernie"] = new List<string> { "ernie-4.0-turbo-8k", "ernie-4.0-8k", "ernie-3.5-8k", "ernie-speed-128k" },
        ["minimax"] = new List<string> { "abab6.5s-chat", "abab6.5-chat", "abab6-chat", "abab5.5-chat" },
        ["hunyuan"] = new List<string> { "hunyuan-pro", "hunyuan-standard", "hunyuan-lite", "hunyuan-turbo" },
        ["mimo"] = new List<string> { "mimo-v1", "mimo-v1-32k", "mimo-v1-128k" },
        ["xai"] = new List<string> { "grok-2", "grok-2-mini", "grok-beta" },
        ["qianfan"] = new List<string> { "ernie-4.0-8k", "ernie-3.5-8k", "ernie-speed-128k", "ernie-lite-8k" },
        ["tencent-ti"] = new List<string> { "hunyuan-pro", "hunyuan-standard", "hunyuan-lite" },
        ["huawei-pangu"] = new List<string> { "pangu-7b", "pangu-13b", "pangu-52b" },
        ["bedrock"] = new List<string> { "anthropic.claude-3-sonnet-20240229-v1:0", "anthropic.claude-3-haiku-20240307-v1:0", "meta.llama3-8b-instruct-v1:0" },
        ["openrouter"] = new List<string> { "openai/gpt-4o", "anthropic/claude-3.5-sonnet", "google/gemini-2.0-flash-exp:free" }
    };

    private static readonly Dictionary<string, string> _displayNames = new()
    {
        ["openai"] = "OpenAI",
        ["azure"] = "Azure OpenAI",
        ["deepseek"] = "DeepSeek",
        ["kimi"] = "Kimi",
        ["glm"] = "智谱 GLM",
        ["qwen"] = "通义千问",
        ["doubao"] = "豆包",
        ["claude"] = "Claude",
        ["gemini"] = "Google Gemini",
        ["ollama"] = "Ollama (本地)",
        ["ernie"] = "百度文心一言",
        ["minimax"] = "MiniMax",
        ["hunyuan"] = "腾讯混元",
        ["mimo"] = "小米 MiMo",
        ["xai"] = "xAI Grok",
        ["qianfan"] = "百度智能云千帆",
        ["tencent-ti"] = "腾讯云 TI 平台",
        ["huawei-pangu"] = "华为云盘古",
        ["bedrock"] = "AWS Bedrock",
        ["openrouter"] = "OpenRouter"
    };

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
        if (_providerConfigs.TryGetValue(providerName, out var config) && !string.IsNullOrEmpty(config.DisplayName))
            return config.DisplayName;

        return _displayNames.TryGetValue(providerName.ToLowerInvariant(), out var name) ? name : providerName;
    }

    public static List<ProviderEndpointInfo> GetEndpoints(string providerName)
    {
        if (!_initialized)
            throw new InvalidOperationException("ProviderHelper not initialized. Call Initialize() first.");

        if (_providerConfigs.TryGetValue(providerName, out var config) && config.Endpoints.Count > 0)
            return config.Endpoints;

        return new List<ProviderEndpointInfo>();
    }

    public static List<string> GetModels(string providerName)
    {
        var key = providerName.ToLowerInvariant();
        var models = _models.TryGetValue(key, out var predefined) ? predefined.ToList() : new List<string>();

        if (_fetchedModels.TryGetValue(key, out var fetched))
        {
            foreach (var model in fetched)
            {
                if (!models.Contains(model, StringComparer.OrdinalIgnoreCase))
                    models.Add(model);
            }
        }

        return models;
    }

    public static List<string> GetAllModels(string providerName, List<string>? customModels = null)
    {
        var models = new HashSet<string>(GetModels(providerName));
        if (customModels != null)
        {
            foreach (var model in customModels)
                models.Add(model);
        }
        return models.ToList();
    }

    public static bool HasMultipleEndpoints(string providerName)
    {
        return GetEndpoints(providerName).Count > 1;
    }

    public static async Task<List<string>> RefreshModelsAsync(string providerName, string apiKey, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        var key = providerName.ToLowerInvariant();

        if (!string.IsNullOrEmpty(baseUrl))
            baseUrl = NormalizeBaseUrl(baseUrl);
        else if (_defaultBaseUrls.TryGetValue(key, out var defaultUrl))
            baseUrl = defaultUrl;
        else
            return GetModels(providerName);

        try
        {
            var models = await FetchModelsFromEndpointAsync(apiKey, baseUrl, cancellationToken).ConfigureAwait(false);
            _fetchedModels[key] = models;
            return GetModels(providerName);
        }
        catch (Exception ex)
        {
            Logger.Warn($"从 /v1/models 刷新 {providerName} 模型列表失败: {ex.Message}");
            return GetModels(providerName);
        }
    }

    private static async Task<List<string>> FetchModelsFromEndpointAsync(string apiKey, string baseUrl, CancellationToken cancellationToken)
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.BaseAddress = new Uri(baseUrl);

        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.GetAsync("models", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var models = new List<string>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var modelId = id.GetString();
                    if (!string.IsNullOrWhiteSpace(modelId) && !models.Contains(modelId, StringComparer.OrdinalIgnoreCase))
                        models.Add(modelId);
                }
            }
        }

        return models;
    }

    private static string NormalizeBaseUrl(string url)
    {
        if (!url.EndsWith('/'))
            url += "/";
        return url;
    }
}
