/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Configuration
*文件名： ModelCapabilityService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/12
*描述：模型能力查询门面（实例级，按用户隔离）。 视觉能力的可变登记状态由 <see cref="ModelCapabilityCache"/> 承载；无状态判定委托 <see cref="ModelCapability"/>。
*
*=================================================
*修改标记
*修改时间：2026/10/12
*修改人： yswenli
*版本号： V1.0.0.0
*描述：模型能力查询门面（实例级，按用户隔离）。 视觉能力的可变登记状态由 <see cref="ModelCapabilityCache"/> 承载；无状态判定委托 <see cref="ModelCapability"/>。
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 模型能力查询门面（实例级，按用户隔离）。
/// 视觉能力的可变登记状态由 <see cref="ModelCapabilityCache"/> 承载；无状态判定委托 <see cref="ModelCapability"/>。
/// </summary>
public sealed class ModelCapabilityService
{
    private readonly ModelCapabilityCache _cache;

    /// <summary>构造。</summary>
    /// <param name="cache">按用户隔离的能力缓存实例。</param>
    public ModelCapabilityService(ModelCapabilityCache cache)
        => _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    /// <summary>判断模型是否支持视觉；无记录时乐观放行（true）。</summary>
    public bool SupportsVision(string modelId)
        => ModelCapability.SupportsVision(modelId, _cache);

    /// <summary>查询模型视觉能力登记记录；无记录返回 null（未知）。</summary>
    public bool? TryGetVision(string modelId) => ModelCapability.TryGetVision(modelId, _cache);

    /// <summary>登记模型视觉能力并持久化。</summary>
    public void SetVision(string modelId, bool supportsVision)
        => ModelCapability.SetVision(modelId, supportsVision, _cache);

    /// <summary>获取模型能力信息（未知模型返回默认能力）。</summary>
    public ModelInfo GetModelInfo(string modelId)
        => ModelCapability.GetModelInfo(modelId, _cache);
}
