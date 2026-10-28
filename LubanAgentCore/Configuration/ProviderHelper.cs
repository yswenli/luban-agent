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

namespace LubanAgentCore.Configuration;

/// <summary>
/// Provider 辅助工具，提供 Provider 显示名称、端点、模型列表等查询及模型列表刷新能力
/// </summary>
public static class ProviderHelper
{
    private static Dictionary<string, ExtendedProviderInfo> _providerConfigs = new();
    private static bool _initialized = false;

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
    /// <param name="cache">当前用户的可变缓存</param>
    /// <returns>模型名称列表</returns>
    public static List<string> GetModels(string providerName, ProviderCache cache)
    {
        var key = providerName.ToLowerInvariant();
        var models = ProviderDefaults.GetDefaultModels(key).ToList();

        foreach (var model in cache.GetFetched(key))
        {
            if (!models.Contains(model, StringComparer.OrdinalIgnoreCase))
                models.Add(model);
        }

        return models;
    }

    /// <summary>
    /// 获取 Provider 的全部模型（内置预设、刷新获取及用户自定义模型），并去重
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <param name="cache">当前用户的可变缓存</param>
    /// <param name="customModels">用户自定义模型列表，可省略</param>
    /// <returns>去重后的完整模型名称列表</returns>
    public static List<string> GetAllModels(string providerName, ProviderCache cache, List<string>? customModels = null)
    {
        var models = new HashSet<string>(GetModels(providerName, cache));
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
    /// <param name="cache">当前用户的可变缓存</param>
    /// <param name="baseUrl">API 基础地址，为空时使用默认地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>刷新后的模型名称列表</returns>
    public static async Task<List<string>> RefreshModelsAsync(string providerName, string apiKey, ProviderCache cache, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        var key = providerName.ToLowerInvariant();

        if (!string.IsNullOrEmpty(baseUrl))
            baseUrl = NormalizeBaseUrl(baseUrl);
        else if (ProviderDefaults.GetDefaultBaseUrl(key) is { } defaultUrl)
            baseUrl = defaultUrl;
        else
            return GetModels(providerName, cache);

        try
        {
            var models = await FetchModelsFromEndpointAsync(apiKey, baseUrl, cancellationToken).ConfigureAwait(false);
            cache.SetFetched(key, models);
            return GetModels(providerName, cache);
        }
        catch (Exception ex)
        {
            Logger.Warn($"从 /v1/models 刷新 {providerName} 模型列表失败: {ex.Message}");
            return GetModels(providerName, cache);
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
