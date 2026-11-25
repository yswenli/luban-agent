/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： ContextTiers
*描述：由模型上下文窗口派生可选预算档位（纯计算，不落盘）
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 上下文预算档位的派生规则。
/// </summary>
/// <remarks>
/// 窗口低于 300K 时只有一个档位（等于窗口本身，等效固定）；达到 300K 后按 300K 倍数分档，
/// 末档取实际窗口；默认档取末档（窗口本身），早期档位留给用户主动提前压缩。
/// 规则只依赖窗口一个数，改规则不需要迁移已存数据。
/// </remarks>
public static class ContextTiers
{
    /// <summary>分档步长（token）。</summary>
    public const int Step = 300_000;

    /// <summary>
    /// 按窗口派生档位清单。
    /// </summary>
    /// <param name="contextWindow">模型上下文窗口；null 或非正数视为未知。</param>
    /// <returns>升序档位；窗口未知时为空。</returns>
    public static IReadOnlyList<int> FromWindow(int? contextWindow)
    {
        if (contextWindow is not { } window || window <= 0) return [];

        if (window < Step) return [window];

        var tiers = new List<int>();
        for (var tier = Step; tier <= window; tier += Step)
        {
            tiers.Add(tier);
        }

        if (tiers.Count == 0) return [window];
        if (window > tiers[^1]) tiers.Add(window);
        return tiers;
    }

    /// <summary>
    /// 按窗口取默认档：取末档（窗口本身），窗口未知时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 默认不提前压缩，实际触发点再由 <see cref="CompactionOptions.ResolveThreshold"/> 留出窗口余量；
    /// 用户手选更小的档位才表示「宁可早点压」。
    /// </remarks>
    /// <param name="contextWindow">模型上下文窗口；null 或非正数视为未知。</param>
    /// <returns>默认档 token 数；窗口未知时返回 <c>null</c>。</returns>
    public static int? DefaultTier(int? contextWindow)
    {
        var tiers = FromWindow(contextWindow);
        return tiers.Count == 0 ? null : tiers[^1];
    }

    /// <summary>
    /// 把已存档位收敛到当前窗口的合法集合：窗口未知或档位非法时返回 <c>null</c>。
    /// </summary>
    /// <param name="contextWindow">模型上下文窗口。</param>
    /// <param name="tier">已存档位。</param>
    /// <returns>合法档位；不合法返回 <c>null</c>。</returns>
    public static int? Normalize(int? contextWindow, int? tier)
    {
        if (tier is not { } value || value <= 0) return null;
        return FromWindow(contextWindow).Contains(value) ? value : null;
    }
}