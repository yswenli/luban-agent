/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepStateResolver
*创建时间：2026/10/20
*描述：步骤状态归并（PRD §4.6.1 步骤条）：档案里的终态快照 ⊕ 本次运行的操作记录。
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 步骤条状态的唯一计算出口：把「档案里的终态快照」（跨重启保留）与「本次运行的操作记录」（更新者优先）归并。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不能只读操作记录</b>：记录只存在于内存，重启即空；若只读记录，重启后已跑完的步骤会全部退回「未开始」。
/// </para>
/// <para>
/// <b>为什么不能只读档案快照</b>：快照只落终态，正在进行的那一步必须由记录体现（否则显示不出「执行中」）。
/// </para>
/// <para>
/// 归并顺序保证「最后一次发生者胜出」：先铺档案快照，再按发生顺序覆盖为本次记录的状态。重跑同一步时，
/// 时间线上后出现的记录（失败 → 重跑成功，或成功 → 重跑失败）自然成为该步的当前状态。
/// </para>
/// </remarks>
public static class StepStateResolver
{
    /// <summary>
    /// 归并步骤状态。
    /// </summary>
    /// <param name="persisted">档案里的终态快照（键为步骤名、值为状态名）；可为 <c>null</c>。</param>
    /// <param name="records">本次运行的操作记录（按发生顺序）；可为 <c>null</c>。</param>
    /// <returns>每个步骤的当前状态；未出现过的步骤不在结果中。</returns>
    public static IReadOnlyDictionary<WizardStep, StepStatus> Resolve(
        IReadOnlyDictionary<string, string>? persisted,
        IReadOnlyList<OperationRecord>? records)
    {
        var result = new Dictionary<WizardStep, StepStatus>();

        if (persisted is not null)
        {
            foreach (var (key, value) in persisted)
            {
                // 认不出的键 / 值一律忽略：档案由旧版本写入或被人手改坏时，不应让步骤条整体失效
                if (Enum.TryParse<WizardStep>(key, ignoreCase: true, out var step)
                    && Enum.TryParse<StepStatus>(value, ignoreCase: true, out var status))
                {
                    result[step] = status;
                }
            }
        }

        if (records is not null)
        {
            foreach (var record in records)
                result[record.Step] = record.Status;
        }

        return result;
    }
}