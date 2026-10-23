/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： StepReport
*创建时间：2026/10/21
*描述：Step 3/4/5/6/7「助手结论文件」契约（M4-P4 定案 + v0.3 助手驱动扩展）
*****************************************************************************/

using System.Text.Json.Serialization;

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// 结论文件的 <see cref="StepReport.Status"/> 取值（v0.3 助手驱动扩展）。
/// </summary>
/// <remarks>
/// <b>为什么用 <c>string</c> 而不是枚举</b>：枚举遇未知值会在源生成反序列化阶段直接抛异常，
/// 与「文件不存在」混在一起，不利于给出可操作文案；故 <c>status</c> 用字符串，
/// 由 <c>StepReportEvaluator</c> 显式比对这三个常量。
/// </remarks>
public static class StepReportStatus
{
    /// <summary>本步完成（成功）。</summary>
    public const string Done = "done";

    /// <summary>需要用户补充输入（面板渲染 <see cref="StepReport.Inputs"/> 后下发执行续跑）。</summary>
    public const string NeedsInput = "needsInput";

    /// <summary>本步失败（不推进）。</summary>
    public const string Failed = "failed";

    /// <summary>
    /// 准备（只读）阶段：事实已查清、可进入执行（仅 <c>step3-prepare-*.json</c> 使用）。
    /// </summary>
    /// <remarks>
    /// <b>不得与 <see cref="Done"/> 混用</b>：<c>done</c> 只表示「本步的执行动作已完成」。
    /// 准备阶段写 <c>done</c> 会被执行提示词当成「已经做过」而直接收尾（真机 Step 3 翻车根因）。
    /// </remarks>
    public const string Ready = "ready";

    /// <summary>准备（只读）阶段：条件不满足、当前不可执行（仅准备结论使用）。</summary>
    public const string Blocked = "blocked";
}

/// <summary>
/// 结论文件的 <see cref="StepReport.Phase"/> 取值（方案 A：准备与执行分名）。
/// </summary>
public static class StepReportPhase
{
    /// <summary>准备（只读）阶段，文件名 <c>step{N}-prepare-&lt;时间戳&gt;.json</c>。</summary>
    public const string Prepare = "prepare";

    /// <summary>执行阶段，文件名 <c>step{N}-&lt;时间戳&gt;.json</c>。</summary>
    public const string Execute = "execute";
}

/// <summary>
/// 助手结论文件（<c>step{N}-*.json</c>）的 schema（M4-P4 定案 + v0.3 扩展）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么用结论文件而不是解析对话</b>：路线② 下插件驱动助手跑一轮提示词，助手的正文与工具结果
/// 都不适合做机器判定（正文会被截断、工具结果只回前 200 字符）。故约定助手把结论写成结构化 JSON，
/// 插件回读后解析——判定口径 = 文件存在 + schema 可解析 + <see cref="Ok"/>（且 <see cref="Status"/>
/// 为 <c>done</c>）+ 事件流无错误事件（+ Step 3/6/7 的本地复核），齐才算通过。
/// </para>
/// <para>路径约定：执行结论为 <c>{localPath}\.luban-agent\plugin-newproject\results\step{N}-&lt;时间戳&gt;.json</c>；
    /// Step 3 准备（只读）结论为 <c>step3-prepare-&lt;时间戳&gt;.json</c>（<see cref="Phase"/> = <c>prepare</c>）。</para>
/// <para>
/// <b>向后兼容</b>：<see cref="Status"/> / <see cref="Inputs"/> / <see cref="Artifacts"/> 均为可选，
/// Step 4 / Step 5 的旧结论（只有 <see cref="Step"/> / <see cref="Ok"/> / <see cref="Checks"/> / <see cref="Message"/>）
/// 仍可原样解析，既有判定入口不受影响。
/// </para>
/// </remarks>
public sealed class StepReport
{
    /// <summary>步骤号（<c>3</c> / <c>4</c> / <c>5</c> / <c>6</c> / <c>7</c>）。</summary>
    public int Step { get; set; }

    /// <summary>助手自评是否全部通过。</summary>
    public bool Ok { get; set; }

    /// <summary>
    /// 三态状态：<c>done</c> / <c>needsInput</c> / <c>failed</c>（Step 3/6/7 使用）；
    /// 准备（只读）结论用 <c>ready</c> / <c>blocked</c>（见 <see cref="StepReportStatus.Ready"/>）。
    /// </summary>
    /// <remarks>
    /// 留空时按「<see cref="Ok"/> 为真即 <c>done</c>、否则 <c>failed</c>」向后兼容 Step 4/5 的旧结论。
    /// </remarks>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 阶段：<c>prepare</c> / <c>execute</c>（可空；空按执行阶段处理以兼容旧结论）。
    /// </summary>
    public string? Phase { get; set; }

    /// <summary>逐项检查结论（Step 5 为逐端结论）。</summary>
    public List<StepReportCheck> Checks { get; set; } = new();

    /// <summary>需要用户补充的交互项（仅 <c>status=needsInput</c> 时非空，见 §6.3）。</summary>
    public List<StepReportInput> Inputs { get; set; } = new();

    /// <summary>
    /// 产物账目（v0.3）：<b>唯一的面板数据源 + 落账来源</b>（C11′，见 §6.1.1 映射表）。
    /// </summary>
    public List<StepReportArtifact> Artifacts { get; set; } = new();

    /// <summary>一句话结论 / 失败原因。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 结论文件里的单条检查项。
/// </summary>
public sealed class StepReportCheck
{
    /// <summary>
    /// 检查项名称：Step 5 固定取端标识（<c>Service</c> / <c>Admin</c> / <c>Mobile</c>，即
    /// <see cref="ProjectEnd"/> 名称），插件据此把结论对回具体的端；Step 4 可自由命名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>该项是否通过。</summary>
    public bool Ok { get; set; }

    /// <summary>该项详情（失败时的错误摘要，如「3 个错误：CS0246 …」）。</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>该项实际执行的命令（Step 5 记录编译方式，便于用户复核「助手判断」是否合理）。</summary>
    public string? Command { get; set; }
}

/// <summary>
/// Step 5 逐端编译结果（落档案：重启后仍能看到每端编译成没成、用的什么命令）。
/// </summary>
public sealed class EndBuildResult
{
    /// <summary>端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>该端是否编译通过。</summary>
    public bool Ok { get; set; }

    /// <summary>结果摘要（失败时为错误摘要）。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>该端实际执行的命令（助手按工程类型自行判断，如 <c>dotnet build X.sln</c> / <c>pnpm build</c>）。</summary>
    public string? Command { get; set; }

    /// <summary>结论产生时间。</summary>
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// 结论文件 <c>artifacts[]</c> 的单个产物条目（v0.3 定案）。
/// </summary>
/// <remarks>
/// <para>
/// <b>三种 kind</b>：<c>group</c>（代码组，无端归属）/ <c>repository</c>（每端一条）/ <c>pipeline</c>（每端一条）。
/// </para>
/// <para>
/// <see cref="Extra"/> 按 <see cref="Kind"/> 对应一个<b>具名强类型</b>（<see cref="GroupArtifactExtra"/> /
/// <see cref="RepositoryArtifactExtra"/> / <see cref="PipelineArtifactExtra"/>），<b>不得</b>用自由字典（C11′）。
/// </para>
/// </remarks>
public sealed class StepReportArtifact
{
    /// <summary>产物类别：<c>group</c> / <c>repository</c> / <c>pipeline</c>。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>所属端（<c>group</c> 无端归属，为 <c>null</c>）。</summary>
    public ProjectEnd? End { get; set; }

    /// <summary>产物名称（代码组名 / 仓库名 / 流水线名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>云效侧标识（组 id / 仓库 id / 流水线 id）。</summary>
    public string? ExternalId { get; set; }

    /// <summary>云效控制台链接。</summary>
    public string? WebUrl { get; set; }

    /// <summary>HTTPS 克隆地址（<c>repository</c> 专用，取自 <c>httpUrlToRepo</c>，<b>不含</b>凭据）。</summary>
    public string? CloneUrl { get; set; }

    /// <summary>本地目录（<c>repository</c> 专用）。</summary>
    public string? LocalPath { get; set; }

    /// <summary>模板 pin 分支（<c>repository</c> 专用；Step 6 时为推送分支）。</summary>
    public string? TemplateBranch { get; set; }

    /// <summary>提交的 YAML 全文（<c>pipeline</c> 专用，准备阶段产出）。</summary>
    public string? Yaml { get; set; }

    /// <summary>按 <see cref="Kind"/> 对应的具名扩展类型（见三个派生类型）。</summary>
    public ArtifactExtra? Extra { get; set; }
}

/// <summary>
/// <c>artifacts[].extra</c> 的多态基类（v0.3 C11′）。
/// </summary>
/// <remarks>
/// <b>判别属性名 = <c>kind</c></b>：助手在 <c>extra</c> 对象里同样写 <c>"kind"</c>（与产物条目的
/// <see cref="StepReportArtifact.Kind"/> 一致），源生成据此选择具名派生类型。拒绝自由字典，
/// 使面板映射与源生成都保得住（否则运行期 <c>NotSupportedException</c>）。
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GroupArtifactExtra), "group")]
[JsonDerivedType(typeof(RepositoryArtifactExtra), "repository")]
[JsonDerivedType(typeof(PipelineArtifactExtra), "pipeline")]
public abstract class ArtifactExtra
{
}

/// <summary>代码组产物的具名扩展（<c>kind=group</c>）。</summary>
public sealed class GroupArtifactExtra : ArtifactExtra
{
    /// <summary>代码组在组织内是否已存在（已存在＝执行时复用）。</summary>
    public bool Exists { get; set; }

    /// <summary>代码组父级 ID（<c>null</c>＝组织根）。</summary>
    public long? ParentId { get; set; }

    /// <summary>云效组织 ID。</summary>
    public string? OrganizationId { get; set; }
}

/// <summary>仓库产物的具名扩展（<c>kind=repository</c>）。</summary>
/// <remarks>
/// 准备阶段回填「模板定位 / pin 解析 / 同名检查 / 目录占用」；执行续跑阶段回填
/// <see cref="Cloned"/> / <see cref="DefaultBranch"/> / <see cref="CommitId"/>。
/// </remarks>
public sealed class RepositoryArtifactExtra : ArtifactExtra
{
    /// <summary>配置登记的模板仓名。</summary>
    public string? TemplateRepository { get; set; }

    /// <summary>模板库命中项的仓库 ID（<c>templateProjectId</c>）。</summary>
    public long? TemplateProjectId { get; set; }

    /// <summary>模板库命中项的全路径（如 <c>tjc/TJCES/TJCES-Service</c>）。</summary>
    public string? TemplatePath { get; set; }

    /// <summary>模板仓现有分支（用于人工核对 pin 解析依据）。</summary>
    public List<string> TemplateBranches { get; set; } = new();

    /// <summary>pin 来源说明（配置登记 / 模板 develop / 模板默认分支 …）。</summary>
    public string? ResolvedBranchSource { get; set; }

    /// <summary>云效组织内已存在同名仓库（执行时复用，不再创建）。</summary>
    public bool RepositoryExists { get; set; }

    /// <summary>本地目录已存在且非空（执行时会出现「复用 / 清空重克隆 / 中止」三选一）。</summary>
    public bool LocalPathOccupied { get; set; }

    /// <summary>可见性：<c>internal</c> / <c>private</c>。</summary>
    public string? Visibility { get; set; }

    /// <summary>该端的错误；为 <c>null</c> 表示该端可执行。</summary>
    public string? Error { get; set; }

    /// <summary>
    /// 本次占位符替换<b>实际使用的来源令牌</b>（如 <c>["TJCES","Tjces","tjces"]</c>）。
    /// </summary>
    /// <remarks>
    /// <b>为什么由助手给出</b>：占位符规则已迁入 <c>docs/step3-repositories.md</c>，插件不再持有规则表（§4.4 瘦身），
    /// 故插件无法自行推导令牌。助手把"我替换了哪些来源令牌"写进这里，<c>StepLocalVerifier</c> 据此对本地文件
    /// 独立再扫一遍"残余命中 = 0"——令牌集合虽为自报，但"替换是否真的生效"是插件可独立核对的证据（§6.1.4）。
    /// </remarks>
    public List<string> PlaceholderSources { get; set; } = new();

    /// <summary>该端是否已建库并克隆完成（执行续跑阶段回填）。</summary>
    public bool Cloned { get; set; }

    /// <summary>仓库默认分支（执行续跑阶段回填）。</summary>
    public string? DefaultBranch { get; set; }

    /// <summary>推送后的提交 id（Step 6 回填）。</summary>
    public string? CommitId { get; set; }
}

/// <summary>流水线产物的具名扩展（<c>kind=pipeline</c>）。</summary>
public sealed class PipelineArtifactExtra : ArtifactExtra
{
    /// <summary>本次将提交的 YAML 是否含部署阶段（模板口径：仅后端为 <c>true</c>）。</summary>
    public bool YamlIncludesDeployStage { get; set; }

    /// <summary>该端的待人工绑定清单（已展开占位符）。</summary>
    public List<string> PendingBindings { get; set; } = new();

    /// <summary>推荐的部署目标主机组数字 ID（<b>拉机器明细用</b>，不进 YAML）。</summary>
    public long? RecommendedDeployTargetHostGroupId { get; set; }

    /// <summary>推荐的部署目标主机组 uuid（<b>YAML <c>machineGroup</c> 用</b>）。</summary>
    public string? RecommendedDeployTargetUuid { get; set; }
}

/// <summary>
/// 结论文件 <c>inputs[]</c> 的单个交互项（v0.3，仅 <c>status=needsInput</c> 时非空，见 §6.3）。
/// </summary>
public sealed class StepReportInput
{
    /// <summary>交互项标识（如 <c>clone-conflict</c> / <c>deploy-target</c>），答复按它对齐。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>控件形态：<c>choice</c>（单选按钮组）/ <c>select</c>（下拉）。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>提示语（面板标题 / 对话提问）。</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>涉及哪些端（空＝全端 / 项目级）。</summary>
    public List<ProjectEnd> Ends { get; set; } = new();

    /// <summary>候选项。</summary>
    public List<StepReportInputOption> Options { get; set; } = new();

    /// <summary>附加上下文（目录占用 / 默认值等）。</summary>
    public StepReportInputDetails? Details { get; set; }
}

/// <summary>交互项候选项（<c>choice</c> / <c>select</c>）。</summary>
public sealed class StepReportInputOption
{
    /// <summary>
    /// 选项值：<c>clone-conflict</c> 时为 <c>reuse</c> / <c>reclone</c> / <c>abort</c>；
    /// <c>deploy-target</c> 时为<b>数字 hostGroupId</b>（拉机器明细用）。
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>主机组 uuid（仅 <c>deploy-target</c>；YAML <c>machineGroup</c> 用）。</summary>
    public string? Uuid { get; set; }

    /// <summary>展示文案。</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>交互项附加上下文（按 <see cref="StepReportInput.Kind"/> 取用相关字段）。</summary>
public sealed class StepReportInputDetails
{
    /// <summary>目标目录（<c>clone-conflict</c>）。</summary>
    public string? Path { get; set; }

    /// <summary>目录内顶层条目数（<c>clone-conflict</c>，仅用于文案）。</summary>
    public int? EntryCount { get; set; }

    /// <summary>默认选中值（<c>deploy-target</c>）。</summary>
    public string? DefaultValue { get; set; }

    /// <summary>是否必选（<c>deploy-target</c>）。</summary>
    public bool? Required { get; set; }
}