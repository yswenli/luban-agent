/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： ProjectSpec
*创建时间：2026/10/16
*描述：新建项目「项目档案」与相关模型（PRD §4.5）
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// 项目所选端（PRD §4.5）：后端、管理端**必选**，移动端可选。
/// </summary>
public enum ProjectEnd
{
    /// <summary>后端（模板仓 <c>TJCES-Service</c>）。</summary>
    Service = 1,

    /// <summary>管理端（模板仓 <c>TJCES-Admin</c>）。</summary>
    Admin = 2,

    /// <summary>移动端（模板仓 <c>TJCES-Mobile</c>）。</summary>
    Mobile = 3
}

/// <summary>
/// 向导步骤（PRD §4.3，第 1 步为表单采集，其余 6 步由共享能力层承载）。
/// </summary>
public enum WizardStep
{
    /// <summary>Step 1：项目基本信息（名称 / 缩写 / 介绍 / 端选择 / 本地目录）。</summary>
    BasicInfo = 1,

    /// <summary>Step 2：初始化项目环境（工作区 / 会话 / 技能 / 云效 / 项目助手）。</summary>
    Scaffold = 2,

    /// <summary>Step 3：按模板建库并克隆。</summary>
    Repositories = 3,

    /// <summary>Step 4：数据库探活（只读，不建库、不写配置）。</summary>
    Database = 4,

    /// <summary>Step 5：编译检查。</summary>
    Build = 5,

    /// <summary>Step 6：推送改动到自有仓库。</summary>
    Push = 6,

    /// <summary>Step 7：创建流水线。</summary>
    Pipeline = 7
}

/// <summary>
/// 单步执行状态。
/// </summary>
public enum StepStatus
{
    /// <summary>未开始。</summary>
    Pending,

    /// <summary>执行中。</summary>
    Running,

    /// <summary>成功。</summary>
    Succeeded,

    /// <summary>失败（可重试，不影响已成功步骤的产物，PRD §4.7）。</summary>
    Failed,

    /// <summary>已跳过（如未勾选该端）。</summary>
    Skipped
}

/// <summary>
/// 远端产物类别（PRD §4.5 / §4.7：远端产物一律<b>不自动删除</b>，只列孤儿清单）。
/// </summary>
public enum RemoteArtifactKind
{
    /// <summary>云效 Codeup 仓库。</summary>
    Repository,

    /// <summary>云效 Codeup 代码组（项目级，运行期自动建组时登记）。</summary>
    Group,

    /// <summary>已推送分支。</summary>
    Branch,

    /// <summary>云效 Flow 流水线。</summary>
    Pipeline
}

/// <summary>
/// 远端产物条目：已在云效上创建、本地删除撤不掉，故必须可观测（PRD §4.5 v0.18）。
/// </summary>
public sealed class RemoteArtifact
{
    /// <summary>产物类别。</summary>
    public RemoteArtifactKind Kind { get; set; }

    /// <summary>所属端；非按端产物（如项目级分组）为 <c>null</c>。</summary>
    public ProjectEnd? End { get; set; }

    /// <summary>产物名称（仓库名 / 分支名 / 流水线名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>云效控制台链接（供用户手动确认删除）。</summary>
    public string WebUrl { get; set; } = string.Empty;

    /// <summary>云效侧标识（仓库 id / 流水线 id）。</summary>
    public string? ExternalId { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// 按端的目标代码仓库信息（PRD §4.5「目标代码仓库（按端）」）。
/// </summary>
public sealed class EndRepository
{
    /// <summary>所属端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>模板仓名（固定：<c>TJCES-Service</c> / <c>TJCES-Admin</c> / <c>TJCES-Mobile</c>）。</summary>
    public string TemplateRepository { get; set; } = string.Empty;

    /// <summary>
    /// 实际使用的模板 pin 分支（运行期解析后写入：配置登记的覆盖值，或按
    /// <c>develop → master → main → 模板默认分支</c> 解析出的分支；PRD §6 #5）。
    /// </summary>
    public string TemplateBranch { get; set; } = string.Empty;

    /// <summary>目标仓库名，默认 <c>{缩写}-Service</c> / <c>-Admin</c> / <c>-Mobile</c>，可在 Step 3 改。</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>可见性：<c>internal</c>＝组织内公开（默认），<c>private</c>＝私有。</summary>
    public string Visibility { get; set; } = "internal";

    /// <summary>云效侧仓库 id（Step 3 建库或同名复用后写入；供远端产物孤儿清单定位）。</summary>
    public string? ExternalId { get; set; }

    /// <summary>云效仓库 Web 地址。</summary>
    public string WebUrl { get; set; } = string.Empty;

    /// <summary>HTTPS 克隆地址（取自「查询单个仓库」的 <c>httpUrlToRepo</c>，PRD v0.14 订正）。</summary>
    public string CloneUrl { get; set; } = string.Empty;

    /// <summary>本地代码目录。</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>默认分支（Step 6 统一推 <c>main</c>，PRD §6 #17）。</summary>
    public string DefaultBranch { get; set; } = "main";

    /// <summary>该端是否已建库并克隆完成。</summary>
    public bool Cloned { get; set; }
}

/// <summary>
/// 数据库探活参数与结论（Step 4，<b>v0.22 口径</b>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>v0.22 起 Step 4 只探活</b>：插件<b>不</b>生成连接串、<b>不</b>改 <c>appsettings*.json</c>、<b>不</b>建库、
/// 不执行任何 SQL；本对象只记「界面填了什么（非敏感项）」与「探活结论」，用于表单回填与重启后复核。
/// </para>
/// <para>
/// <b>口令不落档案</b>：口令只在内存中随本轮提示词交给助手（PRD §4.8 已记录该口径及其残余影响），
/// 一律不写入本档案、日志与操作记录。
/// </para>
/// </remarks>
public sealed class DatabaseOptions
{
    /// <summary>数据库类型标识（当前只用 <c>mysql</c>）。</summary>
    public string Provider { get; set; } = "mysql";

    /// <summary>服务器（主机名或 IP）。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>端口（默认 MySQL 3306）。</summary>
    public int Port { get; set; } = 3306;

    /// <summary>库名（<b>由用户手工创建</b>，插件不建库）。</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>账号。</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>最近一次探活是否通过（判定口径＝能连上且该库存在）。</summary>
    public bool ProbeOk { get; set; }

    /// <summary>最近一次探活结论（失败时为可操作原因，如 <c>1049 Unknown database</c>）。</summary>
    public string ProbeMessage { get; set; } = string.Empty;

    /// <summary>最近一次探活时间。</summary>
    public DateTimeOffset? ProbedAt { get; set; }
}

/// <summary>
/// 单步执行结果。
/// </summary>
public sealed class StepResult
{
    /// <summary>步骤。</summary>
    public WizardStep Step { get; set; }

    /// <summary>状态。</summary>
    public StepStatus Status { get; set; }

    /// <summary>结果说明（失败时为引导文案，如「个人令牌无效」，PRD §4.7）。</summary>
    public string? Message { get; set; }

    /// <summary>本步产生的远端产物。</summary>
    public IReadOnlyList<RemoteArtifact> Artifacts { get; set; } = Array.Empty<RemoteArtifact>();

    /// <summary>成功结果。</summary>
    public static StepResult Ok(WizardStep step, string? message = null, IReadOnlyList<RemoteArtifact>? artifacts = null)
        => new() { Step = step, Status = StepStatus.Succeeded, Message = message, Artifacts = artifacts ?? Array.Empty<RemoteArtifact>() };

    /// <summary>失败结果。</summary>
    public static StepResult Fail(WizardStep step, string message)
        => new() { Step = step, Status = StepStatus.Failed, Message = message };
}

/// <summary>
/// Agent 操作记录条目（PRD §4.6 右侧「Agent 操作记录区」时间线）。
/// </summary>
/// <remarks>
/// 记录中<b>不得</b>出现明文 PAT（PRD §4.8 #6/#7/#11）：涉及凭据的参数一律以 <c>***</c> 呈现。
/// </remarks>
public sealed class OperationRecord
{
    /// <summary>发生时间。</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>所属步骤。</summary>
    public WizardStep Step { get; set; }

    /// <summary>动作标题（如「创建仓库 jnms-Service」）。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>参数 / 结果摘要（已脱敏）。</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>状态。</summary>
    public StepStatus Status { get; set; } = StepStatus.Running;

    /// <summary>耗时。</summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// 克隆冲突处理方式（PRD §4.3 Step 3 重跑幂等②）：目标目录非空时<b>必须</b>由用户选择，不静默覆盖、也不直接失败。
/// </summary>
public enum CloneConflictChoice
{
    /// <summary>复用已克隆的本地目录（跳过克隆，其余步骤继续）。</summary>
    Reuse = 1,

    /// <summary>清空目标目录后重新克隆。</summary>
    Reclone = 2,

    /// <summary>中止本步（本地与远端产物均保持原样，用户可稍后重跑）。</summary>
    Abort = 3
}

/// <summary>
/// 克隆冲突：目标目录已存在且非空，交界面呈现「复用已克隆 / 清空后重克隆 / 中止」三选一。
/// </summary>
public sealed class CloneConflict
{
    /// <summary>所属端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>目标仓库名（与本地目录名一致）。</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>目标本地目录（绝对路径）。</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>目录内顶层条目数（仅用于文案，不做内容判定）。</summary>
    public int EntryCount { get; set; }
}

/// <summary>
/// 逐端流水线结果（Step 7，PRD §4.3 Step 7 v0.30）。
/// </summary>
/// <remarks>
/// 落档案的理由与 <see cref="ProjectSpec.BuildResults"/> 同源：流水线是<b>远端产物</b>，必须跨重启可复核
/// （哪端建了、id / 链接是什么、还有哪些资源待人工绑定），面板才能如实回显并列出孤儿清单。
/// </remarks>
public sealed class EndPipelineResult
{
    /// <summary>所属端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>云效流水线 ID。</summary>
    public string PipelineId { get; set; } = string.Empty;

    /// <summary>流水线名称。</summary>
    public string PipelineName { get; set; } = string.Empty;

    /// <summary>云效控制台链接。</summary>
    public string WebUrl { get; set; } = string.Empty;

    /// <summary>本次是<b>新建</b>（true）还是命中同名<b>复用</b>（false，幂等，PRD §4.3 Step 7）。</summary>
    public bool Created { get; set; }

    /// <summary>该流水线当前是否已包含部署阶段（「一键补齐部署阶段」后为 true）。</summary>
    public bool DeployStageIncluded { get; set; }

    /// <summary>该端的待人工绑定清单（已展开占位符）。</summary>
    public List<string> PendingBindings { get; set; } = new();

    /// <summary>渲染出的新版 YAML（供界面查看与排障；不做脱敏，因不含任何凭据）。</summary>
    public string Yaml { get; set; } = string.Empty;
}

/// <summary>
/// 新建项目「项目档案」（PRD §4.5）：从上到下贯穿七步，并持久化以支持残留检测与孤儿清单展示。
/// </summary>
public sealed class ProjectSpec
{
    // ── Step 1：用户填写 ──────────────────────────────────────────────
    /// <summary>项目中文名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>英文缩写（小写字母开头，可含数字），用于目录 / 仓库名 / 流水线名，需唯一。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>一句话项目介绍（用于初始化项目助手与 README 占位）。</summary>
    public string Introduction { get; set; } = string.Empty;

    /// <summary>所选端（后端、管理端必选；移动端可选）。</summary>
    public List<ProjectEnd> Ends { get; set; } = new();

    /// <summary>本地目录（即工作区根目录）。</summary>
    public string LocalPath { get; set; } = string.Empty;

    // ── Step 2：初始化脚手架产出 ─────────────────────────────────────
    /// <summary>本项目绑定的工作区 id。</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>本项目绑定的会话 id。</summary>
    public string? SessionId { get; set; }

    // ── Step 3：按端目标仓库 ─────────────────────────────────────────
    /// <summary>各端目标仓库信息。</summary>
    public List<EndRepository> Repositories { get; set; } = new();

    // ── Step 4：数据库 ───────────────────────────────────────────────
    /// <summary>数据库探活参数与结论。</summary>
    public DatabaseOptions Database { get; set; } = new();

    // ── Step 5：逐端编译 ─────────────────────────────────────────────
    /// <summary>
    /// 逐端编译结果（最近一次 Step 5 的结论）。
    /// </summary>
    /// <remarks>
    /// 落档案的理由与 <see cref="StepStates"/> 同源：编译结论必须跨重启可复核（哪端过了、用的什么命令、错在哪），
    /// 且 Step 6 的「失败阻断」判定也读这里。全部端通过才允许推进到 Step 6。
    /// </remarks>
    public List<EndBuildResult> BuildResults { get; set; } = new();

    // ── 云效相关 ─────────────────────────────────────────────────────
    /// <summary>云效 PAT 的凭据键名（<b>不含</b>令牌明文，PRD §4.5）。</summary>
    /// <remarks>
    /// 规范键为 <c>connector:yunxiao:token</c>（= <c>AliyunCredentialKeyAliases.DefaultTokenKey</c>）；
    /// 历史档案里的 <c>yunxiao_pat</c> 仍由产品别名表回退读取。
    /// </remarks>
    public string CredentialKey { get; set; } = "connector:yunxiao:token";

    /// <summary>云效企业 / 组织 ID（中心版必填，取自 codex 云效连接配置）。</summary>
    public string? OrganizationId { get; set; }

    /// <summary>参考流水线标识（按端；来自插件配置，Step 2 载入）。</summary>
    public List<string> PipelineReferenceIds { get; set; } = new();

    /// <summary>
    /// 逐端流水线结果（最近一次 Step 7；PRD §4.3 Step 7 v0.30）。
    /// </summary>
    /// <remarks>Step 3 建库重跑会作废本结论（仓库地址可能已变，见 <c>NewProjectService.InvalidatePipelineConclusion</c>）。</remarks>
    public List<EndPipelineResult> PipelineResults { get; set; } = new();

    /// <summary>远端产物清单（v0.18 新增）：向导与插件管理界面均可查看。</summary>
    public List<RemoteArtifact> RemoteArtifacts { get; set; } = new();

    // ── 过程状态 ─────────────────────────────────────────────────────
    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>七步是否全部完成。</summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 当前推进到的步骤：语义为「<b>已到达的最大步</b>」（只前进，见 <see cref="AdvanceTo"/>）。
    /// </summary>
    /// <remarks>
    /// 面板的「回退到某步重看 / 重跑」只移动面板自己的视图游标，不改本字段：否则「回看第 5 步重编译」会把
    /// 已经到达第 7 步的档案拉回第 6 步，回退重跑反而让进度倒退（P4 真机冒烟缺陷）。「已到达」与「未到达」
    /// 也据此区分——只有前者允许直接切过去，后者必须按顺序执行。
    /// </remarks>
    public WizardStep CurrentStep { get; set; } = WizardStep.BasicInfo;

    /// <summary>
    /// 单调推进当前步骤：只前进不后退。
    /// </summary>
    /// <param name="step">目标步骤。</param>
    public void AdvanceTo(WizardStep step)
    {
        if (CurrentStep < step)
            CurrentStep = step;
    }

    /// <summary>
    /// 各步骤的<b>终态快照</b>：键为 <see cref="WizardStep"/> 名称、值为 <see cref="StepStatus"/> 名称。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 必须落档案的原因：<b>操作记录只在内存里</b>（进程重启即空），而步骤条的状态一律取自「记录的真实终态」。
    /// 若只读记录，重启后已经跑完的步骤会全部退回「未开始」（与档案里的
    /// <see cref="EndRepository.Cloned" />、<see cref="RemoteArtifacts" /> 自相矛盾）。
    /// 故共享能力层在每次落库时把本步终态写进这里，面板按「档案快照 ⊕ 本次运行记录（更新者优先）」渲染。
    /// </para>
    /// <para>只落终态（成功 / 失败 / 已跳过）；进行中属易失状态，不落档案。</para>
    /// </remarks>
    public Dictionary<string, string> StepStates { get; set; } = new();
}