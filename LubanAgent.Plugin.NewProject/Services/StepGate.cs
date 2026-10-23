/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepGate
*创建时间：2026/10/21
*描述：步骤前置闸门（PRD §4.3 / §4.7 / 设计 §6.7）：判据改读「结论文件」，收成纯函数
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 步骤前置闸门：把「某步是否具备前置条件」收成只读<b>结论文件</b>的纯函数，供能力层硬校验与单测共用。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要闸门</b>：PRD 的「编译失败阻断推送」等口径，若只落在向导的步骤顺序上，就是一条界面约束——
/// 常驻「重新推送」入口、步骤条的「回到某步重跑」、聊天入口都能绕开它。P4 真机冒烟即因此翻车：Step 5 从未
/// 执行，Step 6 却推送成功，档案里出现「5 编译 未开始 / 6 推送 已完成」的自相矛盾状态。
/// </para>
/// <para>
/// <b>C7′ 判定源（v0.3 定案）</b>：从「档案终态快照 <see cref="ProjectSpec.StepStates"/> + 逐端结果」改为
/// <b>读对应步骤最近一份结论文件</b>——<c>CanPush</c> 读 <c>results/step5-*.json</c> 的逐端编译结论、
/// <c>CanCreatePipelines</c> 读 <c>results/step6-*.json</c> 的逐端推送结论。判定复用
/// <see cref="StepReportEvaluator"/>（避免「两处口径各自漂移」）；<see cref="ProjectSpec.StepStates"/> 降级为
/// <b>仅界面进度显示</b>，不再作闸门依据。
/// </para>
/// <para>
/// 判据取「最近一份」结论文件（按 <c>mtime</c>），与 §6.7 一致；「本轮起始时刻之后」的时效过滤在能力层下发/回读时完成。
/// </para>
/// </remarks>
public static class StepGate
{
    /// <summary>
    /// 推送（Step 6）的前置：编译（Step 5）是否已全部通过。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <returns>最近一份 Step 5 结论逐端全通过且覆盖全集返回 <c>true</c>。</returns>
    public static bool CanPush(ProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var report = ReadLatestConclusion(spec, 5);
        if (report is null)
            return false;

        var (ok, _, _) = StepReportEvaluator.EvaluateBuild(report, spec.Ends, null);
        return ok;
    }

    /// <summary>
    /// 创建流水线（Step 7）的前置：推送（Step 6）是否已成功。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <returns>最近一份 Step 6 结论逐端推送通过且覆盖全集返回 <c>true</c>。</returns>
    public static bool CanCreatePipelines(ProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var report = ReadLatestConclusion(spec, 6);
        if (report is null)
            return false;

        var (ok, _, _) = StepReportEvaluator.EvaluatePush(report, spec.Ends, null);
        return ok;
    }

    /// <summary>
    /// 作废编译（Step 5）结论：Step 3（建库并克隆）重跑成功后调用——本地代码可能已变，旧结论不再代表当前工作树。
    /// </summary>
    /// <remarks>
    /// C7′：删除（作废）最近一份 <c>results/step5-*.json</c> + 清档案 <see cref="ProjectSpec.BuildResults"/>，
    /// 使 <see cref="CanPush"/> 立刻判否。不再维护 <c>StepStates["Build"]</c> 快照键（降级为仅界面进度）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    public static void InvalidateBuild(ProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        StepResultFile.TryDeleteLatest(spec.LocalPath, 5);
        spec.BuildResults.Clear();
    }

    /// <summary>
    /// 作废流水线（Step 7）结论：Step 3 重跑成功后调用（§6.6）——流水线 YAML 写死代码源地址与分支，
    /// 仓库改名/重建后旧结论是误导。
    /// </summary>
    /// <remarks>
    /// <b>只清结论与档案记录，不删云效上的流水线本体</b>（远端产物一律不自动删除，PRD §4.5）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    public static void InvalidatePipelineConclusion(ProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        StepResultFile.TryDeleteLatest(spec.LocalPath, 7);
        spec.PipelineResults.Clear();
        spec.RemoteArtifacts.RemoveAll(one => one.Kind == RemoteArtifactKind.Pipeline);
    }

    /// <summary>
    /// 读某步最近一份结论文件并解析（不存在 / 解析失败一律返回 <c>null</c>，由调用方判否）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="step">步骤号。</param>
    /// <returns>结论；不可用返回 <c>null</c>。</returns>
    private static StepReport? ReadLatestConclusion(ProjectSpec spec, int step)
    {
        if (string.IsNullOrWhiteSpace(spec.LocalPath))
            return null;

        var latest = StepResultFile.FindLatest(spec.LocalPath, step);
        if (latest is null)
            return null;

        string text;
        try
        {
            text = File.ReadAllText(latest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return StepReportParser.TryParse(text, out var report, out _) ? report : null;
    }
}