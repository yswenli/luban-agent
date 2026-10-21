/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepReportEvaluator
*创建时间：2026/10/21
*描述：Step 4 探活结论 / Step 5 逐端编译结论的归并与阻断判定（M4-P4）
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 结论文件的归并与阻断判定（纯函数，供单测锁死口径）。
/// </summary>
/// <remarks>
/// <para>
/// <b>判定口径（四环齐才算通过）</b>：①结论文件存在；②schema 可解析（<see cref="StepReportParser"/>）；
/// ③<see cref="StepReport.Ok"/> 为真；④本轮事件流无错误事件。任一不满足即判失败。
/// </para>
/// <para>
/// <b>不阻断 vs 阻断</b>：Step 4 的失败只记录、<b>不影响</b>流程推进（数据库不通也能先编译、先推送）；
/// Step 5 的失败<b>阻断</b> Step 6（编译都不过，推上去也没有意义）。
/// </para>
/// <para>
/// 缺项一律按失败处理：Step 5 要求的端若没出现在 <c>checks</c> 里，即判「该端未给结论」，不用「其余端通过」兜底——
/// 否则助手漏跑一个端会被静默放过。
/// </para>
/// </remarks>
public static class StepReportEvaluator
{
    /// <summary>
    /// 归并 Step 4 探活结论。
    /// </summary>
    /// <param name="report">已解析的结论。</param>
    /// <param name="assistantErrors">本轮事件流里的错误（工具失败 / 错误事件）。</param>
    /// <returns>是否通过 + 可展示的一句话结论。</returns>
    public static (bool Ok, string Message) EvaluateProbe(StepReport report, IReadOnlyList<string>? assistantErrors)
    {
        var errors = assistantErrors ?? Array.Empty<string>();

        if (report.Step != 4)
            return (false, $"结论文件步骤号不符：应为 4，实为 {report.Step}。");

        var message = string.IsNullOrWhiteSpace(report.Message)
            ? (report.Ok ? "探活通过：能连上且库存在。" : "探活未通过（助手未给出原因）。")
            : report.Message.Trim();

        if (errors.Count > 0)
            message += $"；本轮事件流有 {errors.Count} 条错误：{Join(errors)}";

        return (report.Ok && errors.Count == 0, message);
    }

    /// <summary>
    /// 归并 Step 5 逐端编译结论，并给出阻断判定。
    /// </summary>
    /// <param name="report">已解析的结论。</param>
    /// <param name="ends">本次要求编译的端（按 <see cref="ProjectSpec.Ends"/> ∩ 已克隆仓库）。</param>
    /// <param name="assistantErrors">本轮事件流里的错误（工具失败 / 错误事件）。</param>
    /// <returns>是否通过 + 逐端结果（落档案）+ 一句话结论。</returns>
    public static (bool Ok, List<EndBuildResult> Results, string Message) EvaluateBuild(
        StepReport report,
        IReadOnlyList<ProjectEnd> ends,
        IReadOnlyList<string>? assistantErrors)
    {
        var errors = assistantErrors ?? Array.Empty<string>();
        var results = new List<EndBuildResult>();

        if (report.Step != 5)
            return (false, results, $"结论文件步骤号不符：应为 5，实为 {report.Step}。");

        foreach (var end in ends)
        {
            var check = report.Checks.FirstOrDefault(one =>
                string.Equals(one.Name?.Trim(), end.ToString(), StringComparison.OrdinalIgnoreCase));

            results.Add(new EndBuildResult
            {
                End = end,
                Ok = check?.Ok == true,
                Summary = check is null
                    ? "助手未给出该端的编译结论。"
                    : (string.IsNullOrWhiteSpace(check.Detail)
                        ? (check.Ok ? "编译通过。" : "编译失败（助手未给出错误摘要）。")
                        : check.Detail.Trim()),
                Command = check?.Command,
                CheckedAt = DateTimeOffset.Now
            });
        }

        var failed = results.Where(one => !one.Ok).ToList();
        var allOk = report.Ok
                    && results.Count > 0
                    && failed.Count == 0
                    && errors.Count == 0;

        var message = allOk
            ? $"Step 5 通过：{results.Count} 端全部编译通过。"
            : $"Step 5 未通过：{failed.Count}/{results.Count} 端失败（{string.Join("、", failed.Select(one => EndName(one.End)))}）"
              + $"。{Join(failed.Select(one => $"{EndName(one.End)}：{one.Summary}"))}";

        if (errors.Count > 0)
            message += $"；本轮事件流有 {errors.Count} 条错误：{Join(errors)}";

        return (allOk, results, message);
    }

    /// <summary>端的中文名（与面板 / 操作记录口径一致）。</summary>
    /// <param name="end">端。</param>
    /// <returns>中文名。</returns>
    private static string EndName(ProjectEnd end) => end switch
    {
        ProjectEnd.Service => "后端",
        ProjectEnd.Admin => "管理端",
        ProjectEnd.Mobile => "移动端",
        _ => end.ToString()
    };

    /// <summary>把多条错误压成一句（单条截断 120 字，最多 3 条）。</summary>
    /// <param name="items">错误列表。</param>
    /// <returns>汇总文案。</returns>
    private static string Join(IEnumerable<string> items)
    {
        var list = items
            .Where(one => !string.IsNullOrWhiteSpace(one))
            .Select(one => one.Replace('\r', ' ').Replace('\n', ' ').Trim())
            .Select(one => one.Length <= 120 ? one : one[..120] + "…")
            .Take(3)
            .ToList();

        return list.Count == 0 ? "(无细节)" : string.Join(" | ", list);
    }
}