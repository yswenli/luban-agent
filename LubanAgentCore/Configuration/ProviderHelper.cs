/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*Author：yswenli
*命名空间：LubanAgentCore.Configuration
*文件名： ProviderHelper
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：Provider 辅助工具，提供显示名称、端点、模型等查询与刷新能力
*
*****************************************************************************/
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace LubanAgentCore.Configuration;

/// <summary>
/// Provider 辅助工具，提供 Provider 显示名称、端点、模型列表等查询及模型列表刷新能力
/// </summary>
public static class ProviderHelper
{
    private static Dictionary<string, ExtendedProviderInfo> _providerConfigs = new();
    private static bool _initialized = false;

    private static readonly ConcurrentDictionary<string, List<string>> _fetchedModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>精确匹配的支持视觉的模型 ID。</summary>
    private static readonly HashSet<string> _visionModels = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI（Azure 复用同一批 ID，无需重复列出）
        "gpt-4o", "gpt-4o-mini", "gpt-4.1-mini", "gpt-4.1-nano", "o3", "o4-mini",
        // DeepSeek
        "deepseek-flash",
        // Kimi
        "kimi-k3", "kimi-k2.7-code", "kimi-k2.7-code-highspeed", "kimi-k2.6",
        // GLM
        "glm-5.3-flash", "glm-5v-turbo", "glm-4.6v", "glm-4.6v-flash",
        "glm-4.1v-thinking-flash", "glm-4.1v-thinking-flashx", "glm-4v-flash", "glm-ocr",
        // Qwen（确定支持 VL）
        "qwen3-vl-plus", "qwen3-vl-flash", "qwen-vl-max", "qwen-vl-plus",
        "qvq-max", "qvq-plus", "qwen3.5-omni-plus",
        // Doubao
        "doubao-1.5-vision-pro", "doubao-1.5-vision-lite", "doubao-vision-pro", "doubao-vision-lite",
        // Gemini
        "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.0-flash", "gemini-2.0-flash-lite",
        "gemini-1.5-pro", "gemini-1.5-flash",
        // MiniMax
        "MiniMax-M3", "MiniMax-M2.7", "MiniMax-M2.7-highspeed",
        "MiniMax-M2.5", "MiniMax-M2.5-highspeed", "MiniMax-H3",
        // Hunyuan
        "hunyuan-turbos-vision", "hunyuan-t1-vision", "hunyuan-turbos-vision-video", "tencent-hy-vision-1.5-instruct",
        // MiMo
        "mimo-v2.5-pro",
        // xAI（确定支持）
        "grok-2-vision", "grok-4",
        // ERNIE / Qianfan
        "ernie-4.5", "ernie-4.0-vision", "ernie-3.5-vision"
    };

    /// <summary>前缀族（通配符），用于 Claude / Bedrock 等版本化模型 ID。</summary>
    private static readonly string[] _visionPrefixes =
    {
        "claude-opus-4", "claude-sonnet-4", "claude-3-7-sonnet", "claude-3-5-sonnet",
        "anthropic.claude-opus-4", "anthropic.claude-sonnet-4",
        "anthropic.claude-3-7-sonnet", "anthropic.claude-3-5-sonnet"
    };

    private static readonly Dictionary<string, string> _defaultBaseUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = "https://api.openai.com/v1/",
        ["deepseek"] = "https://api.deepseek.com/v1/",
        ["kimi"] = "https://api.moonshot.cn/v1/",
        ["glm"] = "https://open.bigmodel.cn/api/paas/v4/",
        ["qwen"] = "https://dashscope.aliyuncs.com/compatible-mode/v1/",
        ["doubao"] = "https://ark.cn-beijing.volces.com/api/v3/",
        ["ollama"] = "http://localhost:11434/v1/",
        ["ernie"] = "https://qianfan.baidubce.com/v2/",
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
            "gpt-4o", "gpt-4o-mini",
            "o1", "o1-mini", "o1-pro",
            "o3", "o3-mini", "o3-pro", "o4-mini"
        },
        ["azure"] = new List<string> { "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-4", "gpt-35-turbo" },
        ["deepseek"] = new List<string> { "deepseek-flash", "deepseek-v4-pro" },
        ["kimi"] = new List<string> { "kimi-k3", "kimi-k2.7-code", "kimi-k2.7-code-highspeed", "kimi-k2.6" },
        ["glm"] = new List<string>
        {
            "glm-5.3", "glm-5.3-flash", "glm-5.2", "glm-5.1", "glm-5", "glm-5-turbo",
            "glm-4.7", "glm-4.7-flash", "glm-4.6", "glm-4.5-air", "glm-4.5-flash",
            "glm-4-long", "glm-4-flash-250414",
            "glm-5v-turbo", "glm-4.6v", "glm-4.6v-flash",
            "glm-4.1v-thinking-flash", "glm-4.1v-thinking-flashx", "glm-4v-flash", "glm-ocr"
        },
        ["qwen"] = new List<string>
        {
            "qwen3.8-max", "qwen3.8-flash", "qwen3.7-plus", "qwen3.7-flash",
            "qwen3.6-plus", "qwen3.6-flash", "qwen3.5-plus", "qwen3.5-flash",
            "qwen3.5-omni-plus", "qwen3-vl-plus", "qwen3-vl-flash",
            "qwen-vl-max", "qwen-vl-plus", "qvq-max", "qvq-plus"
        },
        ["doubao"] = new List<string>
        {
            "doubao-1.5-vision-pro", "doubao-1.5-vision-lite",
            "doubao-vision-pro", "doubao-vision-lite",
            "Seed-Evolving", "deepseek-v4.1-flash"
        },
        ["claude"] = new List<string>
        {
            "claude-opus-4-20250514", "claude-sonnet-4-20250514",
            "claude-3-7-sonnet-20250219", "claude-3-5-sonnet-20241022",
            "claude-3-5-haiku-20241022", "claude-3-haiku-20240307"
        },
        ["gemini"] = new List<string>
        {
            "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.0-flash",
            "gemini-2.0-flash-lite", "gemini-1.5-pro", "gemini-1.5-flash"
        },
        ["ollama"] = new List<string> { "llama3.1", "llama3.2", "qwen2.5", "deepseek-coder-v2", "codellama" },
        ["ernie"] = new List<string>
        {
            "ernie-4.5", "ernie-4.0-turbo", "ernie-4.0-vision",
            "ernie-3.5-8k", "ernie-3.5-vision", "ernie-speed-pro", "ernie-lite-pro"
        },
        ["minimax"] = new List<string>
        {
            "MiniMax-M3", "MiniMax-M2.7", "MiniMax-M2.7-highspeed",
            "MiniMax-M2.5", "MiniMax-M2.5-highspeed", "MiniMax-H3"
        },
        ["hunyuan"] = new List<string>
        {
            "hunyuan-turbos-vision", "hunyuan-t1-vision",
            "hunyuan-turbos-vision-video", "tencent-hy-vision-1.5-instruct",
            "hunyuan-turbo", "hunyuan-pro", "hunyuan-standard", "hunyuan-lite"
        },
        ["mimo"] = new List<string> { "mimo-v2.5-pro" },
        ["xai"] = new List<string> { "grok-4", "grok-3", "grok-3-mini", "grok-3-fast", "grok-2-vision" },
        ["qianfan"] = new List<string>
        {
            "ernie-4.5", "ernie-4.0-turbo", "ernie-4.0-vision",
            "ernie-3.5-8k", "ernie-3.5-vision", "ernie-speed-pro", "ernie-lite-pro"
        },
        ["tencent-ti"] = new List<string>
        {
            "hunyuan-turbos-vision", "hunyuan-t1-vision",
            "hunyuan-turbo", "hunyuan-pro", "hunyuan-standard", "hunyuan-lite"
        },
        ["huawei-pangu"] = new List<string> { "pangu-7b", "pangu-13b", "pangu-52b" },
        ["bedrock"] = new List<string>
        {
            "anthropic.claude-opus-4-20250515-v1:0", "anthropic.claude-sonnet-4-20250515-v1:0",
            "anthropic.claude-3-5-sonnet-20241022-v1:0", "anthropic.claude-3-7-sonnet-20250219-v1:0",
            "meta.llama3-8b-instruct-v1:0", "meta.llama4-maverick-v1:0"
        },
        ["openrouter"] = new List<string>
        {
            "deepseek/deepseek-v4.1-flash", "deepseek/deepseek-pro-latest",
            "openai/gpt-4.1", "openai/o3",
            "anthropic/claude-opus-4", "anthropic/claude-sonnet-4",
            "google/gemini-2.5-pro", "google/gemini-2.5-flash",
            "meta/llama-4-maverick"
        }
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

    /// <summary>
    /// 从配置中初始化 Provider 扩展信息
    /// </summary>
    /// <param name="configuration">应用配置</param>
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

    /// <summary>
    /// 获取 Provider 的显示名称，优先使用配置中的显示名称，其次使用内置映射，最后返回原始名称
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <returns>Provider 显示名称</returns>
    public static string GetDisplayName(string providerName)
    {
        if (_providerConfigs.TryGetValue(providerName, out var config) && !string.IsNullOrEmpty(config.DisplayName))
            return config.DisplayName;

        return _displayNames.TryGetValue(providerName.ToLowerInvariant(), out var name) ? name : providerName;
    }

    /// <summary>
    /// 获取 Provider 的可用端点列表
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <returns>端点列表；未配置或未初始化时返回空列表</returns>
    public static List<ProviderEndpointInfo> GetEndpoints(string providerName)
    {
        if (!_initialized)
            throw new InvalidOperationException("ProviderHelper not initialized. Call Initialize() first.");

        if (_providerConfigs.TryGetValue(providerName, out var config) && config.Endpoints.Count > 0)
            return config.Endpoints;

        return new List<ProviderEndpointInfo>();
    }

    /// <summary>
    /// 获取 Provider 的模型列表（内置预设模型与刷新获取的模型）
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <returns>模型名称列表</returns>
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

    /// <summary>
    /// 判断模型是否支持视觉输入。
    /// </summary>
    /// <param name="modelId">模型 ID（可含 "provider:" 前缀或 "vendor/model" 形式）。</param>
    public static bool SupportsVision(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        var id = modelId.Trim();
        var colon = id.LastIndexOf(':');
        if (colon >= 0 && colon < id.Length - 1)
            id = id[(colon + 1)..];

        if (MatchesVision(id))
            return true;

        var slash = id.IndexOf('/');
        if (slash > 0 && slash < id.Length - 1)
            return MatchesVision(id[(slash + 1)..]);

        return false;
    }

    private static bool MatchesVision(string id)
    {
        if (_visionModels.Contains(id))
            return true;
        foreach (var p in _visionPrefixes)
        {
            if (id.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 由用户显式声明自定义模型的视觉能力。
    /// </summary>
    /// <param name="modelId">模型 ID。</param>
    /// <param name="supportsVision">是否支持视觉。</param>
    public static void RegisterCustomModel(string modelId, bool supportsVision)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;
        var id = modelId.Trim();
        if (supportsVision)
            _visionModels.Add(id);
        else
            _visionModels.Remove(id);
    }

    /// <summary>支持视觉的模型 ID 快照。</summary>
    public static IReadOnlyCollection<string> VisionModels => _visionModels;

    /// <summary>
    /// 获取 Provider 的全部模型（内置预设、刷新获取及用户自定义模型），并去重
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <param name="customModels">用户自定义模型列表，可省略</param>
    /// <returns>去重后的完整模型名称列表</returns>
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

    /// <summary>
    /// 判断 Provider 是否配置了多个可选端点
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <returns>存在多个端点时返回 true，否则返回 false</returns>
    public static bool HasMultipleEndpoints(string providerName)
    {
        return GetEndpoints(providerName).Count > 1;
    }

    /// <summary>
    /// 从远程端点刷新 Provider 的模型列表，失败时回退到本地模型列表
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <param name="apiKey">API 密钥</param>
    /// <param name="baseUrl">API 基础地址，为空时使用默认地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>刷新后的模型名称列表</returns>
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

    /// <summary>
    /// 从远程 /v1/models 端点拉取模型列表
    /// </summary>
    /// <param name="apiKey">API 密钥</param>
    /// <param name="baseUrl">API 基础地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>模型 ID 列表</returns>
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

    /// <summary>
    /// 规范化 Base URL，确保以斜杠结尾
    /// </summary>
    /// <param name="url">原始 Base URL</param>
    /// <returns>规范化后的 Base URL</returns>
    private static string NormalizeBaseUrl(string url)
    {
        if (!url.EndsWith('/'))
            url += "/";
        return url;
    }
}
