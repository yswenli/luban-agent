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
/// <para>
/// 「作废结论」（<see cref="InvalidateBuild"/>）与「判定结论」（<see cref="CanPush"/>）成对放在同一处，
/// 免得两处口径各自漂移。
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

    /// <summary>
    /// 创建流水线（Step 7）的前置：推送（Step 6）是否已成功。
    /// </summary>
    /// <remarks>
    /// 判据取档案里的终态快照（跨重启保留，见 <see cref="ProjectSpec.StepStates"/>）：Step 6 未成功时，
    /// 仓库里没有可构建的代码，流水线建出来只会指向空仓库。与 <see cref="CanPush"/> 同口径——只看档案事实，
    /// 不看界面状态（常驻入口 / 步骤条回退 / 聊天入口都能绕开界面约束）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    /// <returns>Step 6 已成功返回 <c>true</c>。</returns>
    public static bool CanCreatePipelines(ProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return spec.StepStates.TryGetValue(nameof(WizardStep.Push), out var state)
               && string.Equals(state, nameof(StepStatus.Succeeded), StringComparison.Ordinal);
    }

    /// <summary>
    /// 作废档案里的编译（Step 5）结论：Step 3（建库并克隆）重跑成功后调用——本地代码可能已经变了，
    /// 旧结论不再代表当前工作树，必须重新编译才能推送。
    /// </summary>
    /// <remarks>
    /// 同时清「逐端结论」（<see cref="ProjectSpec.BuildResults"/>）与「终态快照」里的 <c>Build</c> 键，
    /// 使 <see cref="CanPush" /> 立刻判否。注意：终态快照还会被保存档案时按<b>内存操作记录</b>回放重写，
    /// 故调用方（Step 3）还须清掉本进程里遗留的 Build 记录，否则刚清掉的键会被「复活」。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    public static void InvalidateBuild(ProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        spec.BuildResults.Clear();
        spec.StepStates.Remove(nameof(WizardStep.Build));
    }
}