/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： PipelinePrepareResult
*创建时间：2026/10/22
*描述：Step 7「只读准备」读数（v0.3 助手驱动，设计 §5.3）：预览 + 待用户答复的交互项 + 映射缺口
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// Step 7「流水线准备（只读）」的读数：助手算出来的「将要提交的 YAML / 待人工绑定清单 / 部署目标候选」。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不落档案</b>：本类型只承载界面与对话路径的一次读数，随读随用（<c>status=needsInput</c> 的交互项
/// 与 <c>artifacts[]</c> 仍在结论文件里，落盘的是结论文件本身，不是本类型）。
/// </para>
/// <para>
/// <b>交互项为什么要回传</b>：聊天路径（C15′）没有面板的部署目标下拉，只有
/// <see cref="Inputs"/> 里的 <c>deploy-target</c> 候选；对话据此提问、面板据此预选默认值。
/// </para>
/// </remarks>
public sealed class PipelinePrepareResult
{
    /// <summary>按端预览（每端一条；口径与创建同源）。</summary>
    public IReadOnlyList<PipelinePreview> Previews { get; init; } = Array.Empty<PipelinePreview>();

    /// <summary>助手给出的交互项（如 <c>deploy-target</c>）；无则空。</summary>
    public IReadOnlyList<StepReportInput> Inputs { get; init; } = Array.Empty<StepReportInput>();

    /// <summary>映射缺口 / 本轮事件流错误；非空即应阻断创建。</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>一句话结论（操作记录用）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>是否具备创建条件（无错误且有预览）。</summary>
    public bool IsReady => Errors.Count == 0 && Previews.Count > 0;
}