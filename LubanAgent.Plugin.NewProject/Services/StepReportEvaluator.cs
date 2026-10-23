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

    // ── v0.3 助手驱动：Step 3 / Step 6 / Step 7 判定入口（Step 4/5 入口不动）────────────

    /// <summary>
    /// 归并 Step 3 建库并克隆结论（准备阶段与执行续跑共用；由 <paramref name="requireCloned"/> 区分）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Step 3 是<b>阻断性</b>步骤：<c>status</c> 必须为 <c>done</c> 且 <see cref="StepReport.Ok"/> 为真；
    /// <c>needsInput</c> / <c>failed</c> 一律不通过。
    /// </para>
    /// <para>
    /// <b>阶段区分（方案 A）</b>：准备（只读）结论用 <c>phase=prepare</c> + <c>status=ready|blocked</c>；
    /// 执行轮只认 <c>status=done</c>，读到 <c>phase=prepare</c> / <c>ready</c> / <c>blocked</c> 即判「未执行」。
    /// </para>
    /// <para>
    /// <b>逐端完备性</b>：<c>artifacts[]</c> 中 <c>kind=repository</c> 必须<b>每端一条</b>；缺端即整步失败
    /// （沿用 Step 5「不用其余端兜底」口径）。执行续跑阶段（<paramref name="requireCloned"/>）还要求各端
    /// <c>extra.cloned=true</c> 且 <c>extra.error</c> 为空。
    /// </para>
    /// </remarks>
    /// <param name="report">已解析的结论（准备阶段与执行续跑各一份）。</param>
    /// <param name="ends">本次要求的端（<see cref="ProjectSpec.Ends"/>）。</param>
    /// <param name="assistantErrors">本轮事件流里的真错误（守卫拒绝不计）。</param>
    /// <param name="localIssues">本地复核器（<see cref="StepLocalVerifier"/>）的独立核对结论（准备阶段传 <c>null</c>）。</param>
    /// <param name="requireCloned">是否要求 <c>cloned=true</c>（执行续跑为 <c>true</c>，只读准备为 <c>false</c>）。</param>
    /// <returns>是否通过 + 三态状态 + 一句话结论。</returns>
    public static (bool Ok, string Status, string Message) EvaluateRepositorySetup(
        StepReport? report,
        IReadOnlyList<ProjectEnd> ends,
        IReadOnlyList<string>? assistantErrors,
        IReadOnlyList<string>? localIssues = null,
        bool requireCloned = false)
    {
        var errors = Errors(assistantErrors);
        var issues = Errors(localIssues);

        if (report is null)
            return Failed("未生成结论文件（step3-*.json 不存在或不可读）——请重跑 Step 3。");

        if (report.Step != 3)
            return Failed($"结论文件步骤号不符：应为 3，实为 {report.Step}。");

        // 方案 A：准备（只读）结论与执行结论必须严格区分。
        // 准备结论（phase=prepare / status=ready|blocked）只代表「事实已查清」，绝不能当作「本步已完成」。
        var declared = (report.Status ?? string.Empty).Trim();
        var isPrepareReport =
            string.Equals((report.Phase ?? string.Empty).Trim(), StepReportPhase.Prepare, StringComparison.OrdinalIgnoreCase)
            || string.Equals(declared, StepReportStatus.Ready, StringComparison.OrdinalIgnoreCase)
            || string.Equals(declared, StepReportStatus.Blocked, StringComparison.OrdinalIgnoreCase);

        if (requireCloned && isPrepareReport)
        {
            return Failed("读到的结论是准备（只读）阶段结论（phase=prepare / status=ready|blocked），"
                          + "不代表本步已执行：必须完成建组 / 建库 / 克隆，并写出新的执行结论"
                          + "（step3-<时间戳>.json，status=done）。");
        }

        if (!requireCloned && string.Equals(declared, StepReportStatus.Blocked, StringComparison.OrdinalIgnoreCase))
            return Failed(Msg(report, "Step 3 准备阶段判定为不可执行（status=blocked）。"));

        var (status, gate) = GateStatus(report, "Step 3");
        if (gate is not null)
            return gate.Value;

        var missing = ends.Where(end => !HasRepositoryArtifact(report, end)).ToList();
        if (missing.Count > 0)
            return Failed($"结论缺少端产物：{EndList(missing)}（逐端完备性要求每端一条 kind=repository）。");

        if (requireCloned)
        {
            var notCloned = ends.Where(end => !IsRepositoryCloned(report, end)).ToList();
            if (notCloned.Count > 0)
                return Failed($"以下端未标记 cloned=true：{EndList(notCloned)}（克隆未完成）。");

            var withError = ends.Where(end => !string.IsNullOrWhiteSpace(RepositoryError(report, end))).ToList();
            if (withError.Count > 0)
                return Failed("以下端存在执行错误：" + string.Join("；",
                    withError.Select(end => $"{EndName(end)}：{RepositoryError(report, end)}")));
        }

        if (issues.Count > 0)
            return Failed("本地复核未通过：" + Join(issues));

        if (errors.Count > 0)
            return Failed($"本轮事件流有 {errors.Count} 条错误：{Join(errors)}");

        var suffix = status == StepReportStatus.NeedsInput ? "（需用户补充输入）" : string.Empty;
        return (true, status, Msg(report, $"Step 3 {status}：{ends.Count} 端产物齐备{suffix}。"));
    }

    /// <summary>
    /// 归并 Step 6 推送结论（阻断性：<c>status=done</c> 且逐端 <c>checks[]</c> 通过）。
    /// </summary>
    /// <param name="report">已解析的结论。</param>
    /// <param name="ends">本次要求的端（<see cref="ProjectSpec.Ends"/>）。</param>
    /// <param name="assistantErrors">本轮事件流里的真错误（守卫拒绝不计）。</param>
    /// <param name="localIssues">本地复核器结论（远端分支 / commit id 核对）。</param>
    /// <returns>是否通过 + 三态状态 + 一句话结论。</returns>
    public static (bool Ok, string Status, string Message) EvaluatePush(
        StepReport? report,
        IReadOnlyList<ProjectEnd> ends,
        IReadOnlyList<string>? assistantErrors,
        IReadOnlyList<string>? localIssues = null)
    {
        var errors = Errors(assistantErrors);
        var issues = Errors(localIssues);

        if (report is null)
            return Failed("未生成结论文件（step6-*.json 不存在或不可读）——请重跑 Step 6。");

        if (report.Step != 6)
            return Failed($"结论文件步骤号不符：应为 6，实为 {report.Step}。");

        var (_, gate) = GateStatus(report, "Step 6");
        if (gate is not null)
            return gate.Value;

        var missing = ends.Where(end => !HasEndCheck(report, end)).ToList();
        if (missing.Count > 0)
            return Failed($"结论缺少端结论：{EndList(missing)}（checks[] 必须逐端一条，缺端即失败）。");

        var failedEnds = ends.Where(end => !IsEndCheckOk(report, end)).ToList();
        if (failedEnds.Count > 0)
            return Failed("以下端推送未通过：" + string.Join("；",
                failedEnds.Select(end => $"{EndName(end)}：{EndCheckDetail(report, end)}")));

        if (issues.Count > 0)
            return Failed("本地复核未通过：" + Join(issues));

        if (errors.Count > 0)
            return Failed($"本轮事件流有 {errors.Count} 条错误：{Join(errors)}");

        return (true, StepReportStatus.Done, Msg(report, $"Step 6 通过：{ends.Count} 端推送完成。"));
    }

    /// <summary>
    /// 归并 Step 7 流水线结论（阻断性：<c>status=done</c> 且逐端 <c>kind=pipeline</c> 齐备）。
    /// </summary>
    /// <remarks>
    /// Step 7 的流水线在<b>远端</b>，插件回读时不触网 ⇒ 没有可独立核对的本地证据（§6.1.4）：
    /// 通过判定 = 助手自报，属残余风险（C12/C13）。本入口能做的最强核对是「含部署阶段的端不得留
    /// <c>&lt;未选择&gt;</c> 占位」与「YAML 非空」。
    /// </remarks>
    /// <param name="report">已解析的结论。</param>
    /// <param name="ends">本次要求的端（<see cref="ProjectSpec.Ends"/>）。</param>
    /// <param name="assistantErrors">本轮事件流里的真错误（守卫拒绝不计）。</param>
    /// <returns>是否通过 + 三态状态 + 一句话结论。</returns>
    public static (bool Ok, string Status, string Message) EvaluatePipeline(
        StepReport? report,
        IReadOnlyList<ProjectEnd> ends,
        IReadOnlyList<string>? assistantErrors)
    {
        var errors = Errors(assistantErrors);

        if (report is null)
            return Failed("未生成结论文件（step7-*.json 不存在或不可读）——请重跑 Step 7。");

        if (report.Step != 7)
            return Failed($"结论文件步骤号不符：应为 7，实为 {report.Step}。");

        var (_, gate) = GateStatus(report, "Step 7");
        if (gate is not null)
            return gate.Value;

        var missing = ends.Where(end => !HasPipelineArtifact(report, end)).ToList();
        if (missing.Count > 0)
            return Failed($"结论缺少端流水线产物：{EndList(missing)}（每端一条 kind=pipeline）。");

        var blankYaml = ends.Where(end => string.IsNullOrWhiteSpace(PipelineYaml(report, end))).ToList();
        if (blankYaml.Count > 0)
            return Failed($"以下端未给出流水线 YAML：{EndList(blankYaml)}。");

        var placeholders = ends.Where(end => PipelineNeedsDeployStage(report, end)
                                             && (PipelineYaml(report, end)?.Contains("未选择", StringComparison.Ordinal) ?? false))
            .ToList();
        if (placeholders.Count > 0)
            return Failed($"以下端含部署阶段但未选定部署目标：" + EndList(placeholders)
                + "（不得降级为占位 / 手填 uuid，请在面板或对话中选定主机组后重跑）。");

        if (errors.Count > 0)
            return Failed($"本轮事件流有 {errors.Count} 条错误：{Join(errors)}");

        return (true, StepReportStatus.Done, Msg(report, $"Step 7 通过：{ends.Count} 端流水线产物齐备。"));
    }

    /// <summary>
    /// 把结论文件解析出的状态归一到三态（兼容只有 <see cref="StepReport.Ok"/> 的旧结论）。
    /// </summary>
    /// <param name="report">结论。</param>
    /// <returns><c>done</c> / <c>needsInput</c> / <c>failed</c>。</returns>
    public static string ResolveStatus(StepReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var raw = report.Status?.Trim();
        if (string.Equals(raw, StepReportStatus.NeedsInput, StringComparison.OrdinalIgnoreCase))
            return StepReportStatus.NeedsInput;
        if (string.Equals(raw, StepReportStatus.Done, StringComparison.OrdinalIgnoreCase))
            return StepReportStatus.Done;
        if (string.Equals(raw, StepReportStatus.Failed, StringComparison.OrdinalIgnoreCase))
            return StepReportStatus.Failed;

        // 旧结论（Step 4/5）没有 status：按 ok 兜底。
        return report.Ok ? StepReportStatus.Done : StepReportStatus.Failed;
    }

    /// <summary>是否为该端给出一条 <c>kind=repository</c> 产物。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>存在返回 <c>true</c>。</returns>
    public static bool HasRepositoryArtifact(StepReport report, ProjectEnd end)
        => report.Artifacts.Any(one => IsKind(one.Kind, "repository") && one.End == end);

    /// <summary>该端 <c>kind=repository</c> 产物是否标记 <c>cloned=true</c>。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>已克隆返回 <c>true</c>。</returns>
    public static bool IsRepositoryCloned(StepReport report, ProjectEnd end)
        => RepositoryExtra(report, end)?.Cloned == true;

    /// <summary>该端仓库产物的执行错误（无产物 / 无 error 时为空）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>错误文本。</returns>
    public static string? RepositoryError(StepReport report, ProjectEnd end)
        => RepositoryExtra(report, end)?.Error;

    /// <summary>取该端的仓库产物（找不到为 <c>null</c>）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>产物条目。</returns>
    public static StepReportArtifact? RepositoryArtifact(StepReport report, ProjectEnd end)
        => report.Artifacts.FirstOrDefault(one => IsKind(one.Kind, "repository") && one.End == end);

    /// <summary>取该端的仓库扩展（找不到为 <c>null</c>）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>具名扩展。</returns>
    public static RepositoryArtifactExtra? RepositoryExtra(StepReport report, ProjectEnd end)
        => RepositoryArtifact(report, end)?.Extra as RepositoryArtifactExtra;

    /// <summary>取该端的流水线产物（找不到为 <c>null</c>）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>产物条目。</returns>
    public static StepReportArtifact? PipelineArtifact(StepReport report, ProjectEnd end)
        => report.Artifacts.FirstOrDefault(one => IsKind(one.Kind, "pipeline") && one.End == end);

    /// <summary>取该端的流水线扩展（找不到为 <c>null</c>）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>具名扩展。</returns>
    public static PipelineArtifactExtra? PipelineExtra(StepReport report, ProjectEnd end)
        => PipelineArtifact(report, end)?.Extra as PipelineArtifactExtra;

    /// <summary>该端是否给定 <c>kind=pipeline</c> 产物。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>存在返回 <c>true</c>。</returns>
    public static bool HasPipelineArtifact(StepReport report, ProjectEnd end)
        => PipelineArtifact(report, end) is not null;

    /// <summary>该端流水线 YAML 全文。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>YAML。</returns>
    public static string? PipelineYaml(StepReport report, ProjectEnd end)
        => PipelineArtifact(report, end)?.Yaml;

    /// <summary>该端是否含独立部署阶段（模板口径：仅后端）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>含部署阶段返回 <c>true</c>。</returns>
    public static bool PipelineNeedsDeployStage(StepReport report, ProjectEnd end)
        => PipelineExtra(report, end)?.YamlIncludesDeployStage == true;

    /// <summary>取该端的逐端检查项（<c>checks[]</c> 按端标识对齐）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>检查项。</returns>
    public static StepReportCheck? EndCheck(StepReport report, ProjectEnd end)
        => report.Checks.FirstOrDefault(one =>
            string.Equals(one.Name?.Trim(), end.ToString(), StringComparison.OrdinalIgnoreCase));

    /// <summary>是否为该端给出一条逐端检查结论。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>存在返回 <c>true</c>。</returns>
    public static bool HasEndCheck(StepReport report, ProjectEnd end) => EndCheck(report, end) is not null;

    /// <summary>该端检查项是否通过。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>通过返回 <c>true</c>。</returns>
    public static bool IsEndCheckOk(StepReport report, ProjectEnd end) => EndCheck(report, end)?.Ok == true;

    /// <summary>该端检查项详情（无则空串）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="end">端。</param>
    /// <returns>详情。</returns>
    public static string EndCheckDetail(StepReport report, ProjectEnd end)
        => EndCheck(report, end)?.Detail?.Trim() ?? string.Empty;

    /// <summary>三态闸门：needsInput / 非 done / 非 ok 一律给阻断结论。</summary>
    /// <param name="report">结论。</param>
    /// <param name="label">步骤标签（文案用）。</param>
    /// <returns>状态 + 若阻断则给出结论。</returns>
    private static (string Status, (bool Ok, string Status, string Message)? Gate) GateStatus(StepReport report, string label)
    {
        var status = ResolveStatus(report);

        if (status == StepReportStatus.NeedsInput)
            return (status, (false, status, Msg(report, $"{label} 需要用户补充输入后才能继续。")));

        if (status != StepReportStatus.Done || !report.Ok)
            return (status, (false, StepReportStatus.Failed,
                Msg(report, status == StepReportStatus.Failed ? $"{label} 失败。" : $"{label} 未标记 ok。")));

        return (status, null);
    }

    /// <summary>构造失败结论（统一 status=failed）。</summary>
    /// <param name="message">原因。</param>
    /// <returns>结论元组。</returns>
    private static (bool Ok, string Status, string Message) Failed(string message)
        => (false, StepReportStatus.Failed, message);

    /// <summary>取助手自报的 message（空则用兜底文案）。</summary>
    /// <param name="report">结论。</param>
    /// <param name="fallback">兜底文案。</param>
    /// <returns>文案。</returns>
    private static string Msg(StepReport report, string fallback)
        => string.IsNullOrWhiteSpace(report.Message) ? fallback : report.Message.Trim();

    /// <summary>kind 比对（忽略首尾空白与大小写）。</summary>
    /// <param name="actual">实际值。</param>
    /// <param name="expected">期望值。</param>
    /// <returns>相同返回 <c>true</c>。</returns>
    private static bool IsKind(string? actual, string expected)
        => string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>过滤出非空白项。</summary>
    /// <param name="items">原始列表。</param>
    /// <returns>非空白列表。</returns>
    private static List<string> Errors(IReadOnlyList<string>? items)
        => (items ?? Array.Empty<string>()).Where(one => !string.IsNullOrWhiteSpace(one)).ToList();

    /// <summary>端列表的中文文案（「后端、管理端」）。</summary>
    /// <param name="ends">端集合。</param>
    /// <returns>文案。</returns>
    private static string EndList(IEnumerable<ProjectEnd> ends)
        => string.Join("、", ends.Select(EndName));

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