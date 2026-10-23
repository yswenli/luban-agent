/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： PipelinePreview
*创建时间：2026/10/22
*描述：Step 7 流水线预览（PRD §4.3 Step 7 v0.30）：建之前把将提交的新版 YAML 摊给用户看，并支持手工微调
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// Step 7 流水线预览：把「本轮只读准备算出的、将要提交什么」摊给用户核对与微调（不直接调云效）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要预览</b>：docs 里的模板是与企服版语义对齐后<b>重写</b>的新版 YAML（云效只收 <c>stages:</c> 顶层形态），
/// 其中的步骤参数键名 / 构建集群等仍需真机校正。让用户在建之前就能看到原文，比「建完再发现不对」可省一次
/// 远端资源往返（远端产物一律不自动删除，PRD §4.5）。
/// </para>
/// <para>
/// 预览口径与创建口径<b>同源</b>：预览就是助手只读准备阶段产出的结论文件
/// <c>artifacts[kind=pipeline]</c>（每端一条，C11′），执行续跑提交的是同一份原文。
/// <b>v0.31</b>：用户手工微调后，这里回显的就是微调后的原文（<see cref="Edited"/> 为 <c>true</c>）。
/// </para>
/// </remarks>
public sealed class PipelinePreview
{
    /// <summary>所属端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>将使用的流水线名（结论文件 <c>artifacts[kind=pipeline].name</c>，按 docs/pipeline-conventions.md 的格式生成）。</summary>
    public string PipelineName { get; set; } = string.Empty;

    /// <summary>将提交的<b>新版 YAML</b> 全文（<b>含部署段</b>，v0.32）。</summary>
    public string Yaml { get; set; } = string.Empty;

    /// <summary>该端的待人工绑定清单（已展开占位符）。</summary>
    public IReadOnlyList<string> PendingBindings { get; set; } = Array.Empty<string>();

    /// <summary>该端已创建的流水线 ID（未创建时为空串）。</summary>
    public string PipelineId { get; set; } = string.Empty;

    /// <summary>该端已创建的流水线控制台链接（未创建时为空串）。</summary>
    public string WebUrl { get; set; } = string.Empty;

    /// <summary>
    /// 本次将提交的 YAML <b>是否含部署阶段</b>（<c>extra.yamlIncludesDeployStage</c>；模板口径：<b>仅后端</b>为 <c>true</c>）。
    /// </summary>
    /// <remarks>
    /// v0.32：界面据此判断「哪些端必须先选到部署目标」——为 <c>true</c> 而没选到目标时阻断创建。
    /// 该判定由助手在只读准备阶段依据 <c>docs/pipeline-conventions.md</c> 给出，面板不复制一份「哪端有部署阶段」的判断。
    /// </remarks>
    public bool YamlIncludesDeployStage { get; set; }

    /// <summary>
    /// 该端的<b>推荐部署目标 uuid</b>（<c>extra.recommendedDeployTargetUuid</c>；未给出时为空串）。
    /// </summary>
    /// <remarks>
    /// 界面用它把「部署目标」下拉默认选中到推荐值（不要默认选列表第一项，否则容易把发布打到别的主机组）。
    /// </remarks>
    public string RecommendedDeployTargetUuid { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Yaml"/> 是否来自用户手工微调（<c>false</c>＝只读准备产出的原文）。
    /// </summary>
    /// <remarks>界面据此显示「已手工微调」标记与「重置为模板」出口；微调只在会话内保留。</remarks>
    public bool Edited { get; set; }

    /// <summary>该端是否已创建过流水线。</summary>
    public bool Created => !string.IsNullOrWhiteSpace(PipelineId);
}