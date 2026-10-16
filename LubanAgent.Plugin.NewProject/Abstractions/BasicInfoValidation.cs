/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： BasicInfoValidation
*创建时间：2026/10/16
*描述：Step 1 基本信息校验结果（PRD §4.3）；字段级错误由能力层统一给出，面板与聊天入口共用
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// Step 1 基本信息校验结果（PRD §4.3）。
/// </summary>
/// <remarks>
/// <para>
/// 校验规则放在<b>共享能力层</b>而非面板：PRD §4.10.2 要求向导面板与聊天式入口零逻辑分叉，
/// 若校验只存在于面板，聊天路径必然要复刻一遍同一套规则。
/// </para>
/// <para>
/// <see cref="Errors"/> 中的每条文案已自带字段名（如「英文缩写：…」），面板可原样逐行展示。
/// </para>
/// <para>
/// 与 <see cref="Errors"/> 区分：<see cref="Warnings"/> <b>不</b>阻断流程，用于「已在别处处理」的情形—— 
/// 例如目标仓库目录已存在且非空，PRD §4.3 Step 3 要求届时给「复用已克隆 / 清空后重克隆 / 中止」三选一，
/// 因此 Step 1 只提示、不拦截（否则用户会被挡在一个该步并不负责的判断上）。
/// </para>
/// </remarks>
public sealed class BasicInfoValidation
{
    /// <summary>是否通过全部校验。</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>字段级错误文案（已含字段名）。</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>非阻断提示文案（已含字段名，面板用警示色展示，不阻止继续）。</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 唯一性口径①命中的已登记工作区 Id（未命中为 <c>null</c>）。
    /// </summary>
    /// <remarks>
    /// 供面板给出「复用该工作区继续」的显式出口：没有它，用户会卡在一个「文案让他点某个按钮、
    /// 而该按钮只在另一种状态下存在」的死角（P2 冒烟缺陷）。
    /// </remarks>
    public string? ExistingWorkspaceId { get; init; }

    /// <summary>唯一性口径①命中的已登记工作区名称（未命中为 <c>null</c>）。</summary>
    public string? ExistingWorkspaceName { get; init; }

    /// <summary>校验通过。</summary>
    /// <returns>结果。</returns>
    public static BasicInfoValidation Ok() => new();

    /// <summary>校验失败。</summary>
    /// <param name="errors">字段级错误文案。</param>
    /// <returns>结果。</returns>
    public static BasicInfoValidation Fail(params string[] errors) => new()
    {
        Errors = errors?.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray() ?? Array.Empty<string>()
    };

    /// <summary>
    /// 附加提示与已登记工作区信息（不改变 <see cref="IsValid"/>）。
    /// </summary>
    /// <param name="warnings">非阻断提示文案。</param>
    /// <param name="existingWorkspaceId">命中的已登记工作区 Id。</param>
    /// <param name="existingWorkspaceName">命中的已登记工作区名称。</param>
    /// <returns>带附加信息的结果。</returns>
    public BasicInfoValidation With(
        IEnumerable<string>? warnings = null,
        string? existingWorkspaceId = null,
        string? existingWorkspaceName = null) => new()
    {
        Errors = Errors,
        Warnings = warnings?.Where(warning => !string.IsNullOrWhiteSpace(warning)).ToArray() ?? Array.Empty<string>(),
        ExistingWorkspaceId = existingWorkspaceId,
        ExistingWorkspaceName = existingWorkspaceName
    };
}