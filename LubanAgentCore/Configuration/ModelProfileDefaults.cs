/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： ModelProfileDefaults
*描述：内置模型能力档案表（用户未登记时生效）
*
*****************************************************************************/

using System.Collections.ObjectModel;

namespace LubanAgentCore.Configuration;

/// <summary>
/// 内置模型能力档案表，键为模型 id（小写，不带供应商前缀）。
/// </summary>
/// <remarks>
/// <para>
/// 能力属于模型本身而不是供应商：同一个模型挂在官方端点、自定义网关或聚合平台上，
/// 上下文窗口与可发思考档位都一样，因此内置表只按模型 id 匹配，任何供应商下同名模型都能命中。
/// </para>
/// <para>
/// 只收录「厂商文档写明、且能映射到标准 <c>reasoning_effort</c> 可发集合」的组合：
/// <c>max</c> / <c>minimal</c> 一律不收（标准链路发不出去），这类模型只登记可发子集；
/// 窗口或档位只核实到一半时，未核实的那一半留 <c>null</c>（未知），不凭印象填。
/// </para>
/// <para>
/// 本表不落盘。用户在设置中心按 <c>provider:model</c> 手工登记过同一模型后以配置为准；
/// 「恢复内置默认」即删除该条用户登记。
/// </para>
/// </remarks>
public static class ModelProfileDefaults
{
    private const int Window1M = 1_048_576;

    private static readonly IReadOnlyDictionary<string, ModelProfile> Profiles =
        new ReadOnlyDictionary<string, ModelProfile>(
            new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase)
            {
                // OpenAI：4.1 / 4o 系列不是推理模型，无档位；o1-mini 明确不支持 reasoning_effort
                ["gpt-4.1"] = Profile(1_047_576, []),
                ["gpt-4.1-mini"] = Profile(1_047_576, []),
                ["gpt-4.1-nano"] = Profile(1_047_576, []),
                ["gpt-4o"] = Profile(128_000, []),
                ["gpt-4o-mini"] = Profile(128_000, []),
                ["o1"] = Profile(200_000, ["Low", "Medium", "High"]),
                ["o3"] = Profile(200_000, ["Low", "Medium", "High"]),
                ["o3-mini"] = Profile(200_000, ["Low", "Medium", "High"]),
                ["o4-mini"] = Profile(200_000, ["Low", "Medium", "High"]),
                ["o1-mini"] = Profile(128_000, []),
                // GPT-5.1：官方 reasoning.effort 支持 none / low / medium / high，窗口 400K
                ["gpt-5.1"] = Profile(400_000, ["None", "Low", "Medium", "High"]),
                // GPT-6 系列：官方 none / low / medium / high / xhigh / max，窗口 1.05M
                ["gpt-6.2-astra"] = Profile(1_050_000, ["None", "Low", "Medium", "High", "ExtraHigh"]),
                ["gpt-6.2-sol"] = Profile(1_050_000, ["None", "Low", "Medium", "High", "ExtraHigh"]),
                ["gpt-6.1-terra"] = Profile(1_050_000, ["None", "Low", "Medium", "High", "ExtraHigh"]),
                ["gpt-6-sol"] = Profile(1_050_000, ["None", "Low", "Medium", "High", "ExtraHigh"]),
                ["gpt-6-luna"] = Profile(1_050_000, ["None", "Low", "Medium", "High", "ExtraHigh"]),

                // Claude：5.x / 4.6+ 官方 low/high/xhigh/max（思考不可关），haiku-4.5 不收 effort
                ["claude-fable-5-2"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-fable-5-1"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-opus-5-5"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-opus-5"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-opus-4-8"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-opus-4-7"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-sonnet-5-5"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-sonnet-5"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-sonnet-4-6"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-haiku-5-5"] = Profile(1_000_000, ["Low", "Medium", "High", "ExtraHigh"]),
                ["claude-haiku-4-5"] = Profile(200_000, []),

                // DeepSeek：官方 low/high/max，max 不可发；关思考走 thinking.type
                ["deepseek-flash"] = Profile(Window1M, ["Low", "High"]),
                ["deepseek-v4-pro"] = Profile(Window1M, ["Low", "High"]),
                // 方舟 DeepSeek-V4.1-Flash（Chat API）：官方 low/high/max + none，默认 high；
                // medium 与 xhigh 均被归一为 high，故不收 Medium / ExtraHigh，max 不可发
                ["deepseek-v4.1-flash"] = Profile(Window1M, ["Low", "High"]),
                ["deepseek-v4.1-flash-1017"] = Profile(Window1M, ["Low", "High"]),
                ["deepseek-v4-1-flash-261017"] = Profile(Window1M, ["Low", "High"]),
                ["deepseek-v4-1-flash-260910"] = Profile(Window1M, ["Low", "High"]),
                ["deepseek-v4-1-flash-economy-261017"] = Profile(Window1M, ["Low", "High"]),

                // Kimi：K3 / K3.1 / K2.8 系列 low/high/max（max 不可发），思考不可关；
                //       k2.6 / k2.7-code 不支持 reasoning_effort
                ["kimi-k3"] = Profile(Window1M, ["Low", "High"]),
                ["kimi-k3.1-pro"] = Profile(Window1M, ["Low", "High"]),
                ["kimi-k3.1-pro-highspeed"] = Profile(Window1M, ["Low", "High"]),
                ["kimi-k2.8-preview"] = Profile(Window1M, ["Low", "High"]),
                ["kimi-k2.6"] = Profile(262_144, []),
                ["kimi-k2.7-code"] = Profile(262_144, []),
                ["kimi-k2.7-code-highspeed"] = Profile(262_144, []),

                // GLM：5.3 思考不可关；5.2 可用 none 停思考，xhigh 映射 max
                ["glm-5.3"] = Profile(Window1M, ["Low", "High"]),
                ["glm-5.3-flash"] = Profile(Window1M, ["Low", "High"]),
                // FlashX / FlashX-Legal 参数与 5.3 一致（1M 上下文，reasoning_effort 同档位）
                ["glm-5.3-flashx"] = Profile(Window1M, ["Low", "High"]),
                ["glm-5.3-flashx-legal"] = Profile(Window1M, ["Low", "High"]),
                ["glm-5.2"] = Profile(Window1M, ["None", "High", "ExtraHigh"]),

                // Qwen：兼容层直收 low/high/xhigh（medium 被映射成 high），窗口 1M
                ["qwen3.8-max"] = Profile(Window1M, ["Low", "Medium", "ExtraHigh"]),
                ["qwen3.8-flash"] = Profile(1_000_000, ["Low", "Medium", "ExtraHigh"]),
                ["qwen4-max"] = Profile(1_000_000, ["Low", "High", "ExtraHigh"]),

                // 豆包：doubao-seed-evolving 支持 low/medium/high（另有 minimal 不可发）
                ["doubao-seed-evolving"] = Profile(Window1M, ["Low", "Medium", "High"]),

                // Gemini：兼容层把 effort 映射到 thinking_budget / thinking_level
                ["gemini-2.5-pro"] = Profile(Window1M, ["Low", "Medium", "High"]),
                ["gemini-2.5-flash"] = Profile(Window1M, ["Low", "Medium", "High"]),
                ["gemini-4-argon"] = Profile(Window1M, ["Low", "Medium", "High"]),
                ["gemini-4-carbon"] = Profile(Window1M, ["Low", "Medium", "High"]),
                ["gemini-4-neon"] = Profile(Window1M, ["Low", "Medium", "High"]),
                ["gemini-4-hydrogen"] = Profile(Window1M, ["Low", "Medium", "High"]),
                ["gemini-3.8-flash"] = Profile(Window1M, ["Low", "Medium", "High"]),
                ["gemini-3.6-flash"] = Profile(Window1M, ["Low", "Medium", "High"]),
                ["gemini-3.5-flash-lite"] = Profile(Window1M, ["Low", "Medium", "High"]),

                // xAI
                ["grok-4"] = Profile(256_000, ["Low", "Medium", "High"]),
                ["grok-3-mini"] = Profile(131_072, ["Low", "High"]),
                ["grok-4.7"] = Profile(500_000, ["Low", "Medium", "High", "ExtraHigh"]),
                // grok-4.6 / 4.5 只核实到接受 effort，未核到具体档位 → 留未知；
                // grok-4.20 在聚合平台不接受 reasoning_effort（仅 multi-agent 变体接受）
                ["grok-4.6"] = Profile(500_000, null),
                ["grok-4.5"] = Profile(500_000, null),
                ["grok-4.20"] = Profile(2_000_000, []),

                // MiniMax：M3.1-Flash 官方 low/medium/high/xhigh/max（none 会被 400 拒绝）
                ["MiniMax-M3.1-Flash"] = Profile(Window1M, ["Low", "Medium", "High", "ExtraHigh"]),
                // 窗口已确认，档位没有逐 id 官方表，留空即「未知」
                ["MiniMax-M3"] = Profile(1_000_000, null),

                // MiMo：三档窗口已确认，官方链路不接受 reasoning_effort
                ["mimo-v2.6-pro"] = Profile(1_050_000, []),
                ["mimo-v2.6-flash"] = Profile(1_050_000, []),
                ["mimo-v2.6-pro-ultraspeed"] = Profile(Window1M, []),

                // 文心：窗口与「接受 effort」已确认，具体档位未核到 → 只登记窗口
                ["ernie-5.5-flash-preview"] = Profile(1_024_000, null),
            });

    /// <summary>内置表全部键（模型 id，用于防漂移测试）。</summary>
    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)Profiles.Keys;

    /// <summary>
    /// 取内置档案（按模型 id 查找，不区分大小写）。
    /// </summary>
    /// <param name="modelId">模型 id；带 <c>provider:</c> 前缀时只取冒号后的部分。</param>
    /// <returns>内置档案副本；没有登记时返回 <c>null</c>。</returns>
    public static ModelProfile? Get(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;

        var key = modelId.Trim();
        var separator = key.IndexOf(':');
        if (separator > 0 && separator < key.Length - 1) key = key[(separator + 1)..];

        return Profiles.TryGetValue(key, out var profile) ? profile.Clone() : null;
    }

    private static ModelProfile Profile(int? contextWindow, string[]? reasoning) => new()
    {
        ContextWindow = contextWindow,
        Reasoning = reasoning is null ? null : [.. reasoning]
    };
}