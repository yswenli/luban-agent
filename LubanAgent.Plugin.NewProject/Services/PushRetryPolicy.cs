/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： PushRetryPolicy
*创建时间：2026/10/21
*描述：推送失败的退避重试策略（v0.29）：只对云效服务端「限流」这类临时失败重试。
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 推送（Step 6 第 3 段）失败的退避重试策略：判定、退避序列与用户可见文案集中在一处。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：真机冒烟里 Step 6 逐端串行推送时，云效返回
/// <c>remote: 请求过于频繁，请稍后重试!</c>——同一步里后端隔 11 秒重推即成功，管理端紧接着（3 秒后）
/// 推送又被限流，而能力层**一次失败即整步失败**、用户只能手点「重新推送」，且失败文案把限流硬说成
/// 「请确认 PAT 具备该仓库写权限，且目标分支未被保护」，把排查引向了错误方向。
/// </para>
/// <para>
/// <b>只重试限流</b>：非快进（远端已有提交）、401 / 令牌失效、权限不足等失败**绝不自动重试**——
/// 重试解决不了它们，只会拖长用户等待并掩盖真因。判定下沉到
/// <see cref="GitCommandResult.IsRateLimited"/>（读 git 原始报错全文）。
/// </para>
/// <para>
/// <b>退避取值</b>：10s → 20s → 30s，最多 3 次重试（单端最坏多等 60s）。取值依据是真机读数——
/// 失败后隔 11 秒重推已成功，3 秒仍被限流，故首个退避取 10s；再往上加长只是保底。
/// </para>
/// </remarks>
public static class PushRetryPolicy
{
    /// <summary>退避序列（秒）：第 n 次重试前等待 <c>BackoffSeconds[n - 1]</c> 秒。</summary>
    public static IReadOnlyList<int> BackoffSeconds { get; } = [10, 20, 30];

    /// <summary>最大重试次数（不含首次尝试）。</summary>
    public static int MaxRetries => BackoffSeconds.Count;

    /// <summary>
    /// 这次失败是否值得退避重试。
    /// </summary>
    /// <param name="result">连接器返回的推送结果。</param>
    /// <returns>失败且为服务端限流时返回 <c>true</c>。</returns>
    public static bool ShouldRetry(GitCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return !result.Success && result.IsRateLimited;
    }

    /// <summary>
    /// 第 <paramref name="retryIndex"/> 次重试（1 起）之前的等待时长。
    /// </summary>
    /// <param name="retryIndex">重试序号（1..<see cref="MaxRetries"/>）。</param>
    /// <returns>退避时长。</returns>
    public static TimeSpan DelayBefore(int retryIndex)
    {
        if (retryIndex < 1 || retryIndex > MaxRetries)
            throw new ArgumentOutOfRangeException(nameof(retryIndex), retryIndex,
                $"重试序号应在 1..{MaxRetries} 之间。");

        return TimeSpan.FromSeconds(BackoffSeconds[retryIndex - 1]);
    }

    /// <summary>
    /// 重试等待期间的可见文案（写入操作记录，让用户看到「在等什么、还要等多久」）。
    /// </summary>
    /// <param name="repositoryName">仓库名。</param>
    /// <param name="branch">目标分支。</param>
    /// <param name="retryIndex">第几次重试（1 起）。</param>
    /// <param name="summary">git 原始报错摘要（已脱敏）。</param>
    /// <returns>单行文案。</returns>
    public static string WaitingDetail(string repositoryName, string branch, int retryIndex, string summary)
        => $"{repositoryName} · {branch} —— 云效服务端限流（{summary}）："
           + $"{DelayBefore(retryIndex).TotalSeconds:F0}s 后重试（第 {retryIndex}/{MaxRetries} 次）。";

    /// <summary>
    /// 推送失败的最终文案：按「是否限流」分流出**正确归因**的建议。
    /// </summary>
    /// <remarks>
    /// 限流分支不提 PAT / 分支保护（真机证据：同一条 PAT、同一个 develop 分支在 11 秒前的重试里推成功了）；
    /// 非限流分支保留原有排查提示，但措辞由「请确认」改为「若持续失败再检查」，避免把可能的偶发网络抖动
    /// 也一口咬定为权限问题。
    /// </remarks>
    /// <param name="repositoryName">仓库名。</param>
    /// <param name="branch">目标分支。</param>
    /// <param name="result">连接器返回的推送结果。</param>
    /// <param name="retryCount">本轮已自动重试的次数。</param>
    /// <returns>失败原因文案。</returns>
    public static string DescribeFailure(
        string repositoryName,
        string branch,
        GitCommandResult result,
        int retryCount)
    {
        ArgumentNullException.ThrowIfNull(result);

        var summary = result.FailureSummary();
        return result.IsRateLimited
            ? $"推送失败：{repositoryName} · {branch} —— {summary}"
              + $"（云效服务端限流：本轮已自动退避重试 {retryCount} 次仍未成功，"
              + "建议等约 1 分钟后点「重新推送（重跑 Step 6）」；已推送端的产物不受影响）。"
            : $"推送失败：{repositoryName} · {branch} —— {summary}"
              + "（若持续失败，请检查 PAT 是否具备该仓库写权限、目标分支是否被保护）。";
    }
}