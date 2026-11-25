/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： ContextCompactedEvent
*描述：上下文压缩阶段事件（起跑前预检 / 超长兜底），供三端显示压缩标记
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 上下文压缩的触发来源。
/// </summary>
public static class ContextCompactTriggers
{
    /// <summary>起跑前按会话 token 预算判定触发。</summary>
    public const string Preflight = "preflight";

    /// <summary>模型报上下文超长后兜底触发。</summary>
    public const string Overflow = "overflow";
}

/// <summary>
/// 上下文压缩的阶段。
/// </summary>
public static class ContextCompactionPhases
{
    /// <summary>压缩开始（摘要调用可能耗时，先给用户反馈）。</summary>
    public const string Started = "started";

    /// <summary>压缩完成。</summary>
    public const string Done = "done";

    /// <summary>压缩失败（摘要调用失败或无可压缩内容），历史保持原样。</summary>
    public const string Failed = "failed";
}

/// <summary>
/// 上下文压缩事件：压缩开始与压缩完成各发一次，UI 据此显示压缩标记。
/// </summary>
/// <param name="Phase">阶段，见 <see cref="ContextCompactionPhases"/>。</param>
/// <param name="Trigger">触发来源，见 <see cref="ContextCompactTriggers"/>。</param>
/// <param name="ArchivedCount">已归档（不再进入模型上下文）的消息条数。</param>
/// <param name="KeptCount">压缩后保留的活跃消息条数。</param>
/// <param name="EstimatedTokens">压缩后的上下文估算 token（含本轮输入）。</param>
/// <param name="Budget">会话模型的上下文预算；模型窗口未知时为 null。</param>
public sealed record ContextCompactedEvent(
    string Phase,
    string Trigger,
    int ArchivedCount,
    int KeptCount,
    int EstimatedTokens,
    int? Budget) : AgentStreamEvent
{
    /// <summary>压缩开始事件（尚无归档条数）。</summary>
    /// <param name="trigger">触发来源。</param>
    /// <param name="budget">上下文预算。</param>
    /// <returns>事件。</returns>
    public static ContextCompactedEvent Started(string trigger, int? budget)
        => new(ContextCompactionPhases.Started, trigger, 0, 0, 0, budget);

    /// <summary>压缩完成事件。</summary>
    /// <param name="trigger">触发来源。</param>
    /// <param name="result">压缩结果。</param>
    /// <returns>事件。</returns>
    public static ContextCompactedEvent Done(string trigger, ContextCompactionResult result)
        => new(ContextCompactionPhases.Done, trigger, result.ArchivedCount, result.KeptCount, result.TokensAfter, result.Budget);

    /// <summary>压缩失败事件（历史保持原样，避免 UI 停在「正在压缩…」）。</summary>
    /// <param name="trigger">触发来源。</param>
    /// <param name="budget">上下文预算。</param>
    /// <returns>事件。</returns>
    public static ContextCompactedEvent Failed(string trigger, int? budget)
        => new(ContextCompactionPhases.Failed, trigger, 0, 0, 0, budget);

    /// <summary>生成三端一致的中文提示文案。</summary>
    /// <returns>提示文案。</returns>
    public string Describe()
    {
        if (Phase == ContextCompactionPhases.Started)
        {
            return "⏳ 对话上下文接近模型窗口上限，正在压缩历史…";
        }

        if (Phase == ContextCompactionPhases.Failed)
        {
            return "⚠️ 上下文压缩未生效，历史保持原样";
        }

        var usage = Budget is { } budget && budget > 0
            ? $"（约 {EstimatedTokens:N0} / {budget:N0} tokens）"
            : EstimatedTokens > 0
                ? $"（约 {EstimatedTokens:N0} tokens）"
                : string.Empty;

        return $"📦 上下文已压缩：归档 {ArchivedCount} 条，保留 {KeptCount} 条{usage}";
    }
}

/// <summary>
/// 一次上下文压缩的结果。
/// </summary>
/// <param name="ArchivedCount">已归档消息条数。</param>
/// <param name="KeptCount">保留的活跃消息条数。</param>
/// <param name="TokensBefore">压缩前估算 token（含本轮输入）。</param>
/// <param name="TokensAfter">压缩后估算 token（含本轮输入）。</param>
/// <param name="Budget">本次判定使用的上下文预算；未提供时为 null。</param>
public sealed record ContextCompactionResult(
    int ArchivedCount,
    int KeptCount,
    int TokensBefore,
    int TokensAfter,
    int? Budget);