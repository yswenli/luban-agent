/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： CompactionOptions
*描述：上下文压缩策略——窗口留白、最近原文保留量与摘要输入比例
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 上下文压缩策略：按模型窗口留白决定最早触发点，压缩后保留最近一段原文。
/// </summary>
/// <remarks>
/// 与框架的按条数压缩（<c>SessionOptions</c>）互补：条数兜底历史组装，
/// 这里按真实 token 长度决定「什么时候压、压完留多少」。
/// </remarks>
public sealed class CompactionOptions
{
    /// <summary>
    /// 触发线距窗口顶端的留白比例（%）。默认 15，即窗口用到 85% 时触发。
    /// </summary>
    public int BufferPercent { get; set; } = 15;

    /// <summary>
    /// 留白下限（token）。默认 16000；小窗口下按比例算出的留白过小，用下限兜住。
    /// </summary>
    public int MinBufferTokens { get; set; } = 16_000;

    /// <summary>
    /// 压缩后保留的最近原文 token 数。默认 15000，超出部分交给摘要。
    /// </summary>
    public int KeepTokens { get; set; } = 15_000;

    /// <summary>
    /// 摘要输入可占预算的比例（%）。默认 80，给系统提示与摘要输出留出余量。
    /// </summary>
    public int SummarizePercent { get; set; } = 80;

    /// <summary>
    /// 把选中档位收敛为实际触发阈值：取「档位」与「窗口减留白」的较小值。
    /// </summary>
    /// <param name="contextWindow">模型上下文窗口。</param>
    /// <param name="selectedBudget">用户选中或默认的预算档位。</param>
    /// <returns>实际触发阈值（token）。</returns>
    public int ResolveThreshold(int contextWindow, int selectedBudget)
    {
        if (contextWindow <= 0) return selectedBudget;

        var percent = Math.Clamp(BufferPercent, 1, 90);
        var buffer = (int)Math.Max(
            (long)contextWindow * percent / 100,
            Math.Max(1, MinBufferTokens));

        // 窗口本身很小时别让留白吃掉整个窗口：至少留一半可用
        buffer = Math.Min(buffer, Math.Max(1, contextWindow / 2));
        var ceiling = Math.Max(1, contextWindow - buffer);
        return Math.Min(selectedBudget, ceiling);
    }
}