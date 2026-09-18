namespace LubanAgentCore.Configuration;

/// <summary>模型能力信息。</summary>
/// <param name="Id">模型 ID。</param>
/// <param name="SupportsVision">是否支持视觉输入。</param>
/// <param name="SupportsToolCalling">是否支持工具调用。</param>
public record ModelInfo(string Id, bool SupportsVision, bool SupportsToolCalling = true);

/// <summary>模型能力查询入口。</summary>
public static class ModelCapability
{
    /// <summary>判断模型是否支持视觉。</summary>
    public static bool SupportsVision(string modelId) => ProviderHelper.SupportsVision(modelId);

    /// <summary>获取模型能力信息（未知模型返回默认能力）。</summary>
    public static ModelInfo GetModelInfo(string modelId)
        => new(modelId, ProviderHelper.SupportsVision(modelId));
}