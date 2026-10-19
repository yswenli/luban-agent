/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： RepositoryPrecheck
*创建时间：2026/10/19
*描述：Step 3 预检读数（PRD §4.3「用户确认界面」）：执行前把「将要发生什么」摆给用户核对
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// 单端预检读数：模板库定位、pin 解析、目标仓库名与落地目录。
/// </summary>
/// <remarks>
/// 预检<b>只读</b>，绝不创建任何云效资源；建组与建库都发生在用户确认后的
/// <c>CreateRepositoriesAsync</c> 里。预检只是把「将要使用的参数」提前摊开，
/// 使确认界面的读数与实际执行参数一致。
/// </remarks>
public sealed class EndPrecheck
{
    /// <summary>所属端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>配置登记的模板仓名。</summary>
    public string TemplateRepository { get; set; } = string.Empty;

    /// <summary>模板库中命中项的仓库 ID（<c>templateProjectId</c>）。</summary>
    public long? TemplateProjectId { get; set; }

    /// <summary>模板库中命中项的全路径（如 <c>tjc/TJCES/TJCES-Service</c>）。</summary>
    public string? TemplatePath { get; set; }

    /// <summary>模板仓现有分支（用于人工核对 pin 解析依据）。</summary>
    public List<string> TemplateBranches { get; set; } = new();

    /// <summary>解析出的 pin（分支名）；解析不出时为 <c>null</c>。</summary>
    public string? ResolvedBranch { get; set; }

    /// <summary>pin 来源说明（配置登记 / 模板 develop / 模板默认分支 …）。</summary>
    public string ResolvedBranchSource { get; set; } = string.Empty;

    /// <summary>目标仓库名（可在 Step 3 改）。</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>云效组织内已存在同名仓库（执行时复用，不再创建）。</summary>
    public bool RepositoryExists { get; set; }

    /// <summary>已存在仓库的 Web 地址（复用时的去处）。</summary>
    public string? RepositoryWebUrl { get; set; }

    /// <summary>可见性：<c>internal</c> / <c>private</c>。</summary>
    public string Visibility { get; set; } = "internal";

    /// <summary>本地落地目录（执行时若已存在且非空，需三选一）。</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>本地目录已存在且非空（执行时会出现「复用 / 清空重克隆 / 中止」三选一）。</summary>
    public bool LocalPathOccupied { get; set; }

    /// <summary>该端的预检错误；为 <c>null</c> 表示该端可执行。</summary>
    public string? Error { get; set; }

    /// <summary>该端是否可执行。</summary>
    public bool IsReady => Error is null;
}

/// <summary>
/// Step 3 预检结果：组织 / 目标代码组 / 各端读数，以及阻断级错误与非阻断提示。
/// </summary>
public sealed class RepositoryPrecheck
{
    /// <summary>云效组织 ID。</summary>
    public string? OrganizationId { get; set; }

    /// <summary>项目代码组名（运行期自动建组的名字）。</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>代码组父级 ID（<c>null</c>＝组织根）。</summary>
    public long? GroupParentId { get; set; }

    /// <summary>代码组已存在时的 ID（执行时复用）。</summary>
    public long? GroupId { get; set; }

    /// <summary>代码组在组织内已存在。</summary>
    public bool GroupExists { get; set; }

    /// <summary>代码组已存在时的 Web 地址。</summary>
    public string? GroupWebUrl { get; set; }

    /// <summary>各端读数（仅含 Step 1 所选端）。</summary>
    public List<EndPrecheck> Ends { get; set; } = new();

    /// <summary>阻断级错误（任一存在即不可执行）。</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>非阻断提示（如本地目录已存在）。</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>预检时间（用于界面提示「预检早于当前输入」）。</summary>
    public DateTimeOffset TakenAt { get; set; } = DateTimeOffset.Now;

    /// <summary>是否具备执行条件。</summary>
    public bool IsReady => Errors.Count == 0 && Ends.All(end => end.IsReady);
}