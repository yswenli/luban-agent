/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： PipelinePreview
*创建时间：2026/10/22
*描述：Step 7 流水线「只读预览」（PRD §4.3 Step 7 v0.30）：建之前把将提交的新版 YAML 摊给用户看
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// Step 7 流水线只读预览：<b>不调云效</b>，只把「按当前档案与本机配置会提交什么」算出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要预览</b>：模板是与企服版语义对齐后<b>重写</b>的新版 YAML（云效只收 <c>stages:</c> 顶层形态），
/// 其中的步骤参数键名 / 构建集群等仍需真机校正。让用户在建之前就能看到原文，比「建完再发现不对」可省一次
/// 远端资源往返（远端产物一律不自动删除，PRD §4.5）。
/// </para>
/// <para>
/// 预览口径与创建口径<b>同源</b>（同一 <c>PipelineRenderer.Render</c> 调用，只是 <c>includeDeployStage=false</c>），
/// 故预览到什么就提交什么，不存在两套模板。
/// </para>
/// </remarks>
public sealed class PipelinePreview
{
    /// <summary>所属端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>将使用的流水线名（<c>pipelineNameFormat</c> 展开）。</summary>
    public string PipelineName { get; set; } = string.Empty;

    /// <summary>将提交的<b>新版 YAML</b> 全文（含末尾的部署阶段注释块）。</summary>
    public string Yaml { get; set; } = string.Empty;

    /// <summary>该端的待人工绑定清单（已展开占位符）。</summary>
    public IReadOnlyList<string> PendingBindings { get; set; } = Array.Empty<string>();

    /// <summary>该端已创建的流水线 ID（未创建时为空串）。</summary>
    public string PipelineId { get; set; } = string.Empty;

    /// <summary>该端已创建的流水线控制台链接（未创建时为空串）。</summary>
    public string WebUrl { get; set; } = string.Empty;

    /// <summary>该端流水线当前是否已包含部署阶段。</summary>
    public bool DeployStageIncluded { get; set; }

    /// <summary>该端是否已创建过流水线。</summary>
    public bool Created => !string.IsNullOrWhiteSpace(PipelineId);
}