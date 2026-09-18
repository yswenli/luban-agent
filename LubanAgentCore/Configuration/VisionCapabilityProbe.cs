namespace LubanAgentCore.Configuration;

/// <summary>
/// 首次带图调用时的视觉能力探针：观察本轮流式结果，据此回填模型能力记录。
/// 无记录（未知）时才启用；成功记 true，命中「图片不支持」错误记 false，其余错误不写入。
/// </summary>
public sealed class VisionCapabilityProbe
{
    private readonly string _modelId;
    private bool _imageUnsupported;

    private VisionCapabilityProbe(string modelId)
    {
        _modelId = modelId;
    }

    /// <summary>本轮是否需要探测（有图片且能力记录未知）。</summary>
    public bool IsProbing { get; private set; }

    /// <summary>
    /// 创建探针：仅当 <paramref name="hasImage"/> 为 true 且模型视觉能力无记录时启用。
    /// </summary>
    /// <param name="modelId">模型标识（providerName:modelName）。</param>
    /// <param name="hasImage">本轮是否包含图片附件。</param>
    public static VisionCapabilityProbe Create(string modelId, bool hasImage)
        => new(modelId) { IsProbing = hasImage && ModelCapability.TryGetVision(modelId) is null };

    /// <summary>观察一条错误文案；命中图片不支持关键词时标记。</summary>
    public void Observe(string? text)
    {
        if (IsProbing && ModelCapability.LooksLikeImageUnsupported(text))
            _imageUnsupported = true;
    }

    /// <summary>
    /// 结束探测并回填能力记录。
    /// </summary>
    /// <param name="error">本轮终止异常；为 null 表示正常完成。</param>
    /// <param name="completed">流是否正常走到结束（未被提前 break/取消）。</param>
    public void Complete(Exception? error = null, bool completed = true)
    {
        if (!IsProbing)
            return;

        if (error is not null)
            Observe(ModelCapability.CollectRawErrorText(error));

        if (_imageUnsupported)
            ModelCapability.SetVision(_modelId, false);
        else if (completed && error is null)
            ModelCapability.SetVision(_modelId, true);
    }
}