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

    /// <summary>Step 4：配置与建库。</summary>
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

    /// <summary>模板 pin 的分支 / 标签（PRD §6 #5：不跟随模板升级，找不到即阻断）。</summary>
    public string TemplateBranch { get; set; } = string.Empty;

    /// <summary>目标仓库名，默认 <c>{缩写}-Service</c> / <c>-Admin</c> / <c>-Mobile</c>，可在 Step 3 改。</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>可见性：<c>internal</c>＝组织内公开（默认），<c>private</c>＝私有。</summary>
    public string Visibility { get; set; } = "internal";

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
/// 数据库配置（Step 4）。
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>数据库类型标识。</summary>
    public string Provider { get; set; } = "mysql";

    /// <summary>连接串（<b>不含</b>明文口令以外的敏感信息时也应避免落日志）。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>建库脚本路径（可选；为空表示仅写配置）。</summary>
    public string? ScriptPath { get; set; }

    /// <summary>建库是否已执行成功。</summary>
    public bool Created { get; set; }
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
    /// <summary>数据库配置。</summary>
    public DatabaseOptions Database { get; set; } = new();

    // ── 云效相关 ─────────────────────────────────────────────────────
    /// <summary>云效 PAT 的凭据键名（<b>不含</b>令牌明文，PRD §4.5）。</summary>
    public string CredentialKey { get; set; } = "yunxiao_pat";

    /// <summary>云效企业 / 组织 ID（中心版必填，取自 codex 云效连接配置）。</summary>
    public string? OrganizationId { get; set; }

    /// <summary>参考流水线标识（按端；来自插件配置，Step 2 载入）。</summary>
    public List<string> PipelineReferenceIds { get; set; } = new();

    /// <summary>远端产物清单（v0.18 新增）：向导与插件管理界面均可查看。</summary>
    public List<RemoteArtifact> RemoteArtifacts { get; set; } = new();

    // ── 过程状态 ─────────────────────────────────────────────────────
    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>七步是否全部完成。</summary>
    public bool IsCompleted { get; set; }

    /// <summary>当前推进到的步骤。</summary>
    public WizardStep CurrentStep { get; set; } = WizardStep.BasicInfo;
}