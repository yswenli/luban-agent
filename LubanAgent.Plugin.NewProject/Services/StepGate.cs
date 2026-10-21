/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepGate
*创建时间：2026/10/21
*描述：步骤前置闸门（PRD §4.3 / §4.7）：某步是否具备前置条件，收成读档案的纯函数。
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 步骤前置闸门：把「某步是否具备前置条件」收成只读档案的纯函数，供能力层硬校验与单测共用。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要闸门</b>：PRD 的「编译失败阻断推送」等口径，若只落在向导的步骤顺序上，就是一条界面约束——
/// 常驻「重新推送」入口、步骤条的「回到某步重跑」、聊天入口都能绕开它。P4 真机冒烟即因此翻车：Step 5 从未
/// 执行，Step 6 却推送成功，档案里出现「5 编译 未开始 / 6 推送 已完成」的自相矛盾状态。
/// </para>
/// <para>
/// 故判据一律取<b>档案里的事实</b>（终态快照 + 逐端结论），不取界面状态、不取内存记录——两者都可被绕开或重启丢失。
/// </para>
/// </remarks>
public static class StepGate
{
    /// <summary>
    /// 推送（Step 6）的前置：编译（Step 5）是否已全部通过。
    /// </summary>
    /// <remarks>
    /// 判据为两处档案事实的合取：Step 5 的终态快照（跨重启保留，见 <see cref="ProjectSpec.StepStates"/>）与
    /// 逐端编译结论（见 <see cref="ProjectSpec.BuildResults"/>）。缺一即视为「未通过」——尤其「从未跑过」时
    /// 快照里根本没有 <c>Build</c> 键，必须判否（这正是冒烟里被放行的情形）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    /// <returns>Step 5 已全部通过返回 <c>true</c>。</returns>
    public static bool CanPush(ProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var buildSucceeded = spec.StepStates.TryGetValue(nameof(WizardStep.Build), out var state)
                             && string.Equals(state, nameof(StepStatus.Succeeded), StringComparison.Ordinal);
        return buildSucceeded
               && spec.BuildResults.Count > 0
               && spec.BuildResults.All(result => result.Ok);
    }
}