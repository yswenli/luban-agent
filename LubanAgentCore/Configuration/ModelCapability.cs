using System.Text;
using LuBan.AIAgent.Errors;

namespace LubanAgentCore.Configuration;

/// <summary>模型能力信息。</summary>
/// <param name="Id">模型 ID。</param>
/// <param name="SupportsVision">是否支持视觉输入。</param>
/// <param name="SupportsToolCalling">是否支持工具调用。</param>
public record ModelInfo(string Id, bool SupportsVision, bool SupportsToolCalling = true);

/// <summary>模型能力查询入口（多模态可变状态由 <see cref="ModelCapabilityCache"/> 按用户承载）。</summary>
public static class ModelCapability
{
    private static readonly string[] ImageUnsupportedKeywords =
    [
        "image", "image_url", "vision", "multimodal", "multi-modal",
        "图片", "视觉", "多模态"
    ];

    /// <summary>
    /// 判断模型是否支持视觉。无记录时乐观放行（true），由 LLM 实际调用结果回填。
    /// </summary>
    /// <param name="modelId">模型标识（providerName:modelName）。</param>
    /// <param name="cache">按用户隔离的能力缓存实例。</param>
    public static bool SupportsVision(string modelId, ModelCapabilityCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return cache.TryGet(modelId) ?? true;
    }

    /// <summary>查询模型视觉能力登记记录；无记录返回 null（未知）。</summary>
    /// <param name="modelId">模型标识（providerName:modelName）。</param>
    /// <param name="cache">按用户隔离的能力缓存实例。</param>
    public static bool? TryGetVision(string modelId, ModelCapabilityCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return cache.TryGet(modelId);
    }

    /// <summary>登记模型视觉能力并持久化。</summary>
    /// <param name="modelId">模型标识（providerName:modelName）。</param>
    /// <param name="supportsVision">是否支持图片输入。</param>
    /// <param name="cache">按用户隔离的能力缓存实例。</param>
    public static void SetVision(string modelId, bool supportsVision, ModelCapabilityCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        cache.Set(modelId, supportsVision);
    }

    /// <summary>获取模型能力信息（未知模型返回默认能力）。</summary>
    /// <param name="modelId">模型标识（providerName:modelName）。</param>
    /// <param name="cache">按用户隔离的能力缓存实例。</param>
    public static ModelInfo GetModelInfo(string modelId, ModelCapabilityCache cache)
        => new(modelId, SupportsVision(modelId, cache));

    /// <summary>
    /// 判断错误文本是否表示「模型不支持图片输入」。
    /// 仅匹配图片/视觉相关关键词，避免把工具调用等其他能力错误误记为图片不支持。
    /// </summary>
    public static bool LooksLikeImageUnsupported(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // 框架对「能力不支持」使用统一文案（含「工具调用/多模态」），无法区分图片；
        // 含「工具调用」时判定为工具能力错误，不计入图片能力记录
        if (text.Contains("工具调用", StringComparison.OrdinalIgnoreCase))
            return false;

        return ImageUnsupportedKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 收集异常的原始错误文本。对 <see cref="AgentApiException"/> 跳过其已分类的外层文案
    /// （该文案对「能力不支持」统一含「多模态」，会误判），只看内层原始异常。
    /// </summary>
    public static string CollectRawErrorText(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        Exception? current = ex is AgentApiException api ? api.InnerException : ex;
        var sb = new StringBuilder();
        for (var depth = 0; current != null && depth < 5; depth++, current = current.InnerException)
            sb.AppendLine(current.Message);
        return sb.ToString();
    }
}