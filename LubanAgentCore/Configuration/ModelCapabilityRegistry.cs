/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： ModelCapabilityRegistry
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/18
*描述：模型多模态能力登记表：从全局配置读取，运行时按实际调用结果回填并持久化
*
*****************************************************************************/
using System.Collections.Concurrent;

namespace LubanAgentCore.Configuration;

/// <summary>
/// 模型多模态能力登记表。
/// 键为 "provider:model"（小写归一化），值表示是否支持图片输入。
/// 无记录表示未知：由 LLM 实际调用结果动态回填（成功记 true，命中图片不支持错误记 false）。
/// </summary>
public static class ModelCapabilityRegistry
{
    private static readonly ConcurrentDictionary<string, bool> _capabilities = new(StringComparer.OrdinalIgnoreCase);
    private static Action<string, bool>? _persist;

    /// <summary>
    /// 由全局配置初始化登记表，并注册持久化回调。
    /// </summary>
    /// <param name="capabilities">已有能力记录，可为空。</param>
    /// <param name="persist">写入回调（更新配置并保存），可为空（仅内存登记）。</param>
    public static void Initialize(IReadOnlyDictionary<string, bool>? capabilities, Action<string, bool>? persist)
    {
        _capabilities.Clear();
        if (capabilities != null)
        {
            foreach (var kv in capabilities)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key))
                    _capabilities[Normalize(kv.Key)] = kv.Value;
            }
        }
        _persist = persist;
    }

    /// <summary>
    /// 查询模型的多模态能力；无记录返回 null（未知）。
    /// </summary>
    /// <param name="modelId">模型标识（格式：providerName:modelName）。</param>
    public static bool? TryGet(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;
        return _capabilities.TryGetValue(Normalize(modelId), out var value) ? value : null;
    }

    /// <summary>
    /// 登记模型的多模态能力，并持久化。
    /// </summary>
    /// <param name="modelId">模型标识（格式：providerName:modelName）。</param>
    /// <param name="supportsVision">是否支持图片输入。</param>
    public static void Set(string modelId, bool supportsVision)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        var key = Normalize(modelId);
        if (_capabilities.TryGetValue(key, out var existing) && existing == supportsVision)
            return;

        _capabilities[key] = supportsVision;
        _persist?.Invoke(key, supportsVision);
    }

    private static string Normalize(string modelId) => modelId.Trim().ToLowerInvariant();
}