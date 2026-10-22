/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： NewProjectService
*创建时间：2026/10/16
*描述：共享能力层实现（PRD §4.10.2）。向导面板与聊天入口共用同一实例。
*****************************************************************************/

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LubanAgentCore.Connectors.AliyunDevOps;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Infrastructure;
using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Lifecycle;
using LuBan.AIAgent.Plugins.Security;
using LuBan.AIAgent.Sessions;
using LubanAgentCore.Configuration;
using LubanAgentCore.Hosting;
using LubanAgentCore.Infrastructure;
using LubanAgentCore.Models;
using LubanAgentCore.Services;
using LubanAgentCore.Utils;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Configuration;
using LubanAgent.Plugin.NewProject.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 「新建项目」共享能力层实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>生命周期</b>：本实例由插件（<c>NewProjectPlugin</c>）持有，存活于插件 ALC 之内；
/// 向导面板直接持有它，聊天路径（P6）经同一实例暴露，保证「同一个实例、零逻辑分叉」。
/// </para>
/// <para>
/// <b>依赖解析（PRD §3.9 DI 硬约束）</b>：宿主容器里的工作区 / 会话 / 连接器服务
/// 一律<b>按需解析</b>，<b>不</b>长期持有（Scoped 服务不得被单例提升）。
/// </para>
/// <para>
/// <b>当前进度（M4-P3）</b>：Step 1 校验 + Step 2（工作区 / 会话 / 技能 / MCP / 档案快照）+
/// Step 3（按模板建库 / 同名复用 + 克隆 + 占位符替换）+ Step 6（目标分支可配置：`pushBranch` 优先、v0.28 起默认 develop + 提交 + 推送）已落地；
/// Step 4 / 5 已落地为「助手亲自执行」；Step 7（v0.30：内嵌新版 YAML 模板渲染 + 创建 + 一键补齐部署阶段）见
/// <see cref="CreatePipelinesAsync"/> / <see cref="AppendDeployStagesAsync"/>。
/// </para>
/// </remarks>
public sealed class NewProjectService : INewProjectService
{
    /// <summary>英文缩写命名规范（PRD §4.3：只能小写字母开头，后面可跟小写字母或数字）。</summary>
    private static readonly Regex CodePattern = new("^[a-z][a-z0-9]*$", RegexOptions.Compiled);

    /// <summary>云效仓库名规范（字母 / 数字 / 下划线 / 中划线 / 点，且以字母或数字开头）。</summary>
    private static readonly Regex RepositoryNamePattern = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    /// <summary>
    /// 模板 pin 的实时解析顺序（PRD §6 #5）：配置 <c>templates[].branch</c> 留空时按此择优。
    /// </summary>
    private static readonly string[] PreferredPins = { "develop", "master", "main" };

    /// <summary>
    /// 模板分支就绪等待上限（PRD §4.3 Step 3 竞态防护）：建库 API 返回<b>不等于</b>模板分支已同步完成，
    /// 服务端的 <c>syncAllBranches</c> 是异步的，必须等 pin 分支真实出现再 <c>git clone -b</c>，否则必然失败。
    /// </summary>
    private static readonly TimeSpan BranchReadyTimeout = TimeSpan.FromSeconds(120);

    /// <summary>模板分支就绪轮询间隔（避免高频打云效接口）。</summary>
    private static readonly TimeSpan BranchReadyPollInterval = TimeSpan.FromSeconds(3);

    private readonly List<OperationRecord> _operations = new();
    private readonly NewProjectStateStore _store = new();
    private readonly object _gate = new();

    private IPluginHost? _host;
    private NewProjectSettings _settings = new();

    /// <inheritdoc />
    public ProjectSpec? Current { get; private set; }

    /// <inheritdoc />
    public WizardStep CurrentStep => Current?.CurrentStep ?? WizardStep.BasicInfo;

    /// <inheritdoc />
    public IReadOnlyList<OperationRecord> Operations
    {
        get
        {
            lock (_gate)
            {
                return _operations.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public string? OrganizationHint
    {
        get
        {
            // 运行时权威来源：＜宿主容器单例＞YunxiaoApiOptions.OrganizationId（codex 云效连接设置保存时即写入该实例，
            // 见 YunxiaoSettingsService.SaveAsync，无需重启）；未命中再退化读凭据存储（重启后由 App 启动时回填）。
            var options = Resolve<YunxiaoApiOptions>();
            if (!string.IsNullOrWhiteSpace(options?.OrganizationId))
                return options!.OrganizationId!.Trim();

            try
            {
                var stored = Resolve<ICredentialStore>()?.GetAsync(AliyunCredentialKeyAliases.DefaultOrganizationIdKey).GetAwaiter().GetResult();
                return string.IsNullOrWhiteSpace(stored) ? null : stored!.Trim();
            }
            catch (Exception ex)
            {
                Logger.Warn("[newproject] 读取云效组织 ID 失败。", ex);
                return null;
            }
        }
    }

    /// <inheritdoc />
    public Func<CloneConflict, Task<CloneConflictChoice>>? CloneConflictResolver { get; set; }

    /// <summary>
    /// 绑定宿主（插件初始化时调用）：载入包内配置与上一次未完成的档案。
    /// </summary>
    /// <param name="host">宿主能力接口。</param>
    internal void Attach(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _settings = NewProjectSettings.LoadDefault();

        // 残留检测：上次未完成的项目档案仍可读取（供面板提示与孤儿清单展示，不做断点续跑）
        var restored = _store.Load();
        if (restored is not null && !restored.IsCompleted)
        {
            Current = restored;
            host.Log($"[newproject] 检测到未完成的项目档案：{restored.Code}（当前步骤 {(int)restored.CurrentStep}）");
        }
        else
        {
            Current = restored;
        }

        RaiseChanged();
    }

    /// <summary>
    /// 解除宿主绑定（插件关闭时调用）。
    /// </summary>
    internal void Detach()
    {
        _host = null;
        lock (_gate)
        {
            _operations.Clear();
        }

        RaiseChanged();
    }

    /// <inheritdoc />
    public async Task<BasicInfoValidation> ValidateBasicInfoAsync(
        string name,
        string code,
        string introduction,
        IEnumerable<ProjectEnd> ends,
        string localPath,
        bool allowExistingWorkspace = false)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        string? existingWorkspaceId = null;
        string? existingWorkspaceName = null;

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
            errors.Add("项目名称：必填。");
        else if (trimmedName.Length > 40)
            errors.Add("项目名称：请控制在 40 字以内。");

        var trimmedCode = code?.Trim() ?? string.Empty;
        if (trimmedCode.Length == 0)
            errors.Add("英文缩写：必填。");
        else if (!CodePattern.IsMatch(trimmedCode))
            errors.Add("英文缩写：只能小写字母开头，后面可跟小写字母或数字（不能有空格、中文或符号）。");
        else if (trimmedCode.Length > 20)
            errors.Add("英文缩写：请控制在 20 个字符以内。");

        if (string.IsNullOrWhiteSpace(introduction))
            errors.Add("项目介绍：必填（一句话说明项目用途）。");

        var selected = (ends ?? Enumerable.Empty<ProjectEnd>()).Distinct().ToList();
        if (!selected.Contains(ProjectEnd.Service))
            errors.Add("端选择：后端为必选。");
        if (!selected.Contains(ProjectEnd.Admin))
            errors.Add("端选择：管理端为必选。");

        var trimmedPath = localPath?.Trim() ?? string.Empty;
        if (trimmedPath.Length == 0)
        {
            errors.Add("本地目录：必填（作为 Step 2 的工作区根目录）。");
        }
        else if (!Path.IsPathRooted(trimmedPath))
        {
            errors.Add("本地目录：请填写绝对路径。");
        }
        else
        {
            var (writable, reason) = ProbeWritable(trimmedPath);
            if (!writable)
                errors.Add($"本地目录：{reason}");

            // 唯一性口径 ①：该目录已被宿主登记为工作区（同路径必然派生同一 WorkspaceId）
            var existing = await FindWorkspaceByPathAsync(trimmedPath);
            if (existing is not null)
            {
                // 命中的工作区信息无论是否拦截都带回去：面板据此给出「复用该工作区继续」出口
                existingWorkspaceId = existing.WorkspaceId;
                existingWorkspaceName = existing.Name;

                if (!allowExistingWorkspace)
                {
                    // 区分两种情形：本项目自身的工作区（重跑 / 复用）vs 其他项目占用的目录
                    var isOwnArchive = string.Equals(Current?.WorkspaceId, existing.WorkspaceId, StringComparison.Ordinal)
                        || string.Equals(trimmedName, existing.Name, StringComparison.Ordinal);
                    errors.Add(isOwnArchive
                        ? $"本地目录：本项目已在该目录初始化过（工作区「{existing.Name}」）。"
                          + "要继续请点「复用该工作区继续」（复用工作区并新建执行会话）；要另建项目请换目录。"
                        : $"本地目录：该目录已被登记为工作区「{existing.Name}」。"
                          + "要继续请点「复用该工作区继续」；要另建项目请换目录。");
                }
            }

            // 唯一性口径 ②：目标仓库目录已存在且非空——**只提示不拦截**：
            // PRD §4.3 Step 3 要求「克隆前判断目标目录，非空时给复用已克隆 / 清空后重克隆 / 中止 三选一」，
            // 该判断属于 Step 3；在 Step 1 硬拦截会把用户挡在本步并不负责的决策上（P2 冒烟缺陷）。
            if (CodePattern.IsMatch(trimmedCode))
            {
                foreach (var end in selected)
                {
                    var repoName = _settings.BuildRepositoryName(trimmedCode, end);
                    var repoDir = Path.Combine(trimmedPath, repoName);
                    try
                    {
                        if (Directory.Exists(repoDir) && Directory.EnumerateFileSystemEntries(repoDir).Any())
                            warnings.Add($"本地目录：{repoName} 已存在且非空；Step 3 克隆前会让你选择"
                                         + "「复用已克隆 / 清空后重克隆 / 中止」（PRD §4.3 Step 3）。");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[newproject] 检查目标目录失败：{repoDir}", ex);
                    }
                }
            }
        }

        var result = errors.Count == 0 ? BasicInfoValidation.Ok() : BasicInfoValidation.Fail(errors.ToArray());
        return result.With(warnings, existingWorkspaceId, existingWorkspaceName);
    }

    /// <inheritdoc />
    public ProjectSpec StartNewProject(
        string name,
        string code,
        string introduction,
        IEnumerable<ProjectEnd> ends,
        string localPath)
    {
        ArgumentNullException.ThrowIfNull(ends);

        var selected = ends.Distinct().ToList();

        var spec = new ProjectSpec
        {
            Name = name?.Trim() ?? string.Empty,
            Code = code?.Trim() ?? string.Empty,
            Introduction = introduction?.Trim() ?? string.Empty,
            Ends = selected,
            LocalPath = localPath?.Trim() ?? string.Empty,
            CurrentStep = WizardStep.BasicInfo
        };

        // 模板对照 → 各端目标仓库（仓库名默认 {缩写}-Service/-Admin/-Mobile，Step 3 可改）
        foreach (var end in selected)
        {
            var template = _settings.FindTemplate(end);
            spec.Repositories.Add(new EndRepository
            {
                End = end,
                TemplateRepository = template?.Repository ?? string.Empty,
                TemplateBranch = template?.Branch ?? string.Empty,
                RepositoryName = _settings.BuildRepositoryName(spec.Code, end),
                Visibility = _settings.DefaultVisibility,
                DefaultBranch = _settings.PushBranch,
                LocalPath = string.IsNullOrWhiteSpace(spec.LocalPath)
                    ? string.Empty
                    : Path.Combine(spec.LocalPath, _settings.BuildRepositoryName(spec.Code, end))
            });
        }

        // 参考流水线清单（Step 2 载入，Step 7 使用）
        foreach (var end in selected)
        {
            var reference = _settings.PipelineReferences.FirstOrDefault(item => item.End == end);
            if (!string.IsNullOrWhiteSpace(reference?.PipelineId))
                spec.PipelineReferenceIds.Add(reference!.PipelineId);
        }

        lock (_gate)
        {
            _operations.Clear();
        }

        Current = spec;
        SaveSpec(spec);

        return spec;
    }

    /// <inheritdoc />
    public void Reset()
    {
        Current = null;
        lock (_gate)
        {
            _operations.Clear();
        }

        _store.Clear();
        RaiseChanged();
    }

    /// <inheritdoc />
    public async Task<StepResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Scaffold;

        var spec = Current;
        if (spec is null)
        {
            Record(Step, "初始化项目环境", "尚未建立项目档案，请先完成第 1 步。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        // 面板事件回调不在框架的权限包裹范围内，此处显式进入本插件的权限环境（PRD §3.7）：
        // 否则后续连接器 / 进程 / 凭据出口会按「宿主自身调用」放行，越界检查形同虚设。
        using var permissionScope = EnterPermissionScope();

        // 依赖按需解析：不长期持有 scoped 服务（PRD §3.9 DI 硬约束）
        var workspaceManager = ResolveWorkspaceManager();
        var sessionManager = ResolveSessionManager();
        if (workspaceManager is null || sessionManager is null)
        {
            Record(Step, "初始化项目环境", "无法解析宿主工作区 / 会话服务（插件宿主环境不完整）。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "无法解析宿主工作区 / 会话服务，Step 2 无法执行。");
        }

        var total = Stopwatch.StartNew();
        var createdWorkspace = false;
        WorkspaceInfo? workspace = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ── 1. 工作区：探测 → 复用 or 新建（陷阱①：同路径重复创建会抛异常）──────────
            var watch = BeginOperation(Step, "初始化工作区", $"目录 {spec.LocalPath}");
            var probeId = WorkspaceIdGenerator.Compute(spec.LocalPath);
            var existing = string.IsNullOrEmpty(probeId) ? null : await workspaceManager.GetWorkspaceAsync(probeId);

            if (existing is null)
            {
                workspace = await workspaceManager.CreateWorkspaceAsync(
                    spec.LocalPath,
                    name: string.IsNullOrWhiteSpace(spec.Name) ? null : spec.Name);
                createdWorkspace = true;
                EndOperation(Step, "初始化工作区",
                    $"已创建并登记工作区「{workspace.Name}」：{workspace.RootPath}", StepStatus.Succeeded, watch.Elapsed);
            }
            else
            {
                workspace = existing;
                EndOperation(Step, "初始化工作区",
                    $"复用已有工作区「{workspace.Name}」：{workspace.RootPath}（重跑幂等）", StepStatus.Succeeded, watch.Elapsed);
            }

            // ── 2. 授权（必须显式调用以落库 IsAuthorized=true，见 PRD Step 2 P0 纠错）───────
            watch = BeginOperation(Step, "工作区授权", "落库 IsAuthorized=true");
            if (!await workspaceManager.EnsureAuthorizedAsync(workspace))
            {
                EndOperation(Step, "工作区授权", "授权被拒绝或授权提示不可用。", StepStatus.Failed, watch.Elapsed);
                throw new InvalidOperationException("工作区授权失败（目录不可用或授权被拒绝）。");
            }

            EndOperation(Step, "工作区授权", "已授权并落库。", StepStatus.Succeeded, watch.Elapsed);

            // ── 3. 置为当前工作区 ────────────────────────────────────────────────────
            watch = BeginOperation(Step, "切换当前工作区", workspace.RootPath);
            await workspaceManager.SetCurrentAsync(workspace.WorkspaceId);
            EndOperation(Step, "切换当前工作区", $"当前工作区：{workspace.Name}", StepStatus.Succeeded, watch.Elapsed);

            // ── 4. 会话：必须「新建」而非复用（陷阱③：复用分支会恢复旧会话）────────────────
            watch = BeginOperation(Step, "准备执行会话", "向导操作的载体与操作记录数据源");
            var sessionTitle = string.IsNullOrWhiteSpace(spec.Name) ? "新建项目" : $"新建项目：{spec.Name}";
            string sessionId;
            if (createdWorkspace)
            {
                // 首次：SetCurrentAsync 已为全新工作区自动建好「新会话」，直接采用并改名
                var auto = sessionManager.CurrentSession;
                if (auto is not null && !string.IsNullOrWhiteSpace(auto.SessionId))
                {
                    sessionId = auto.SessionId;
                    await sessionManager.UpdateSessionTitleAsync(sessionId, sessionTitle);
                }
                else
                {
                    sessionId = (await sessionManager.CreateSessionAsync(sessionTitle, workspace.WorkspaceId)).SessionId;
                }
            }
            else
            {
                // 重跑：显式新建会话，不使用 SwitchWorkspaceAsync 恢复的旧会话
                sessionId = (await sessionManager.CreateSessionAsync(sessionTitle, workspace.WorkspaceId)).SessionId;
            }

            spec.WorkspaceId = workspace.WorkspaceId;
            spec.SessionId = sessionId;
            EndOperation(Step, "准备执行会话",
                $"会话「{sessionTitle}」已就绪（{sessionId}）", StepStatus.Succeeded, watch.Elapsed);

            // ── 5. 档案快照写入本工作区（PRD §4.3 Step 2 第 1 项）─────────────────────────
            watch = BeginOperation(Step, "写入项目档案快照", SnapshotRelativePath);
            var snapshotPath = WriteSnapshot(workspace.RootPath, spec);
            EndOperation(Step, "写入项目档案快照", $"已写入 {snapshotPath}", StepStatus.Succeeded, watch.Elapsed);

            // ── 6. 技能：把 newproject-* 装配到本工作区（§3.11 三件套）──────────────────
            var manifest = ResolveManifest();
            var skills = manifest?.Skills ?? new List<SkillDefinition>();
            var mcps = manifest?.EnabledMcpServers.ToList() ?? new List<PluginMcpServerDeclaration>();
            if (manifest is null || skills.Count == 0)
                throw new InvalidOperationException("插件清单不可读或未声明技能，newproject-* 技能未能装配。");

            watch = BeginOperation(Step, "装配项目技能", $"{skills.Count} 个 newproject-* 技能");
            var mounted = 0;
            foreach (var skill in skills)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _host!.RegisterSkill(skill);
                mounted++;
            }

            EndOperation(Step, "装配项目技能",
                $"{mounted} 个技能已写入 {Path.Combine(workspace.RootPath, ".luban-agent", "skills")}",
                StepStatus.Succeeded, watch.Elapsed);

            // ── 7. 云效 MCP（yunxiao）：会话构建时由宿主按启用中插件清单注入 ──────────────
            watch = BeginOperation(Step, "接通云效 MCP", "yunxiao（进程内客户端）");
            if (mcps.Count == 0)
            {
                EndOperation(Step, "接通云效 MCP", "清单未声明 mcpServers；本步跳过。", StepStatus.Skipped, watch.Elapsed);
            }
            else
            {
                foreach (var mcp in mcps)
                    _host!.RegisterMcpServer(mcp.Name);

                EndOperation(Step, "接通云效 MCP",
                    $"已声明 {string.Join("/", mcps.Select(mcp => mcp.Name))}：该会话首次对话时注入（§3.11）",
                    StepStatus.Succeeded, watch.Elapsed);
            }

            // ── 8. 项目级 Agent：首次对话时由 EnsureRuntimeAsync 惰性创建（PRD Step 2 第 5 项）──
            Record(Step, "项目级 Agent",
                "延迟到该会话首次对话时由 EnsureRuntimeAsync 惰性创建（本步无需触网）。", StepStatus.Succeeded, TimeSpan.Zero);

            // ── 9. 落库并推进到 Step 3 ─────────────────────────────────────────────
            spec.AdvanceTo(WizardStep.Repositories);
            SaveSpec(spec);

            total.Stop();
            var message = $"初始化完成：工作区「{workspace.Name}」· 会话 {sessionId} · "
                          + $"{mounted} 个技能 · MCP {mcps.Count} 项 · 耗时 {total.Elapsed.TotalSeconds:F1}s";
            Record(Step, "Step 2 完成", message, StepStatus.Succeeded, total.Elapsed);
            _host?.Log($"[newproject] {message}");

            return StepResult.Ok(Step, message);
        }
        catch (OperationCanceledException)
        {
            FailRunningOperation(Step, "用户取消。", total.Elapsed);
            return StepResult.Fail(Step, "Step 2 初始化已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 2 初始化失败", ex);
            FailRunningOperation(Step, ex.Message, total.Elapsed);

            // 仅回滚本步：新建的工作区逻辑注销（不删 LocalPath 目录，陷阱②）
            if (createdWorkspace && workspace is not null)
            {
                try
                {
                    var deleted = await workspaceManager.DeleteWorkspaceAsync(workspace.WorkspaceId);
                    Record(Step, "回滚本步",
                        deleted
                            ? $"已注销本步新建的工作区「{workspace.Name}」（本地目录 {spec.LocalPath} 未被删除）。"
                            : $"工作区「{workspace.Name}」注销未生效，请到工作区列表手动清理。",
                        deleted ? StepStatus.Succeeded : StepStatus.Failed, TimeSpan.Zero);
                }
                catch (Exception rollbackEx)
                {
                    _host?.LogError("[newproject] 回滚工作区失败", rollbackEx);
                    Record(Step, "回滚本步", $"注销工作区失败：{rollbackEx.Message}", StepStatus.Failed, TimeSpan.Zero);
                }

                spec.WorkspaceId = null;
                spec.SessionId = null;
            }

            spec.CurrentStep = WizardStep.BasicInfo;
            SaveSpec(spec);

            return StepResult.Fail(Step,
                $"Step 2 初始化失败：{ex.Message}（已回滚本步；修正后可直接重跑本步）");
        }
    }

    /// <inheritdoc />
    public Task<BasicInfoValidation> ValidateRepositoryPlanAsync(
        IEnumerable<EndRepository> plan,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var entries = (plan ?? Enumerable.Empty<EndRepository>()).ToList();

        if (entries.Count == 0)
            errors.Add("建库计划：为空（Step 1 至少要选择后端与管理端）。");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var label = EndLabel(entry.End);
            var name = entry.RepositoryName?.Trim() ?? string.Empty;

            if (name.Length == 0)
                errors.Add($"{label}仓库名：必填。");
            else if (!RepositoryNamePattern.IsMatch(name))
                errors.Add($"{label}仓库名：只能包含字母、数字、下划线、中划线与点，且需以字母或数字开头"
                           + $"（建议沿用默认 {_settings.BuildRepositoryName(Current?.Code ?? string.Empty, entry.End)}）。");
            else if (name.Length > 100)
                errors.Add($"{label}仓库名：请控制在 100 个字符以内。");
            else if (!seen.Add(name))
                errors.Add($"仓库名：{name} 在各端重复（同批建库的仓库名必须互不相同）。");

            if (!IsSupportedVisibility(entry.Visibility))
                errors.Add($"{label}可见性：取值非法（只能是「组织内公开」或「私有」）。");

            if (string.IsNullOrWhiteSpace(entry.TemplateRepository))
                errors.Add($"{label}模板仓：未配置（插件配置 templates[] 缺少该端对照）。");

            // pin 不再必填：留空＝Step 3 预检 / 执行时按 develop → master → main → 模板默认分支 实时解析
            // （配置登记了值则必须真实存在，校验发生在预检里，因为需要联网查分支）。
        }

        // 与 Step 1 的端选择对齐（档案里的端一个都不能漏）
        var selected = Current?.Ends ?? new List<ProjectEnd>();
        foreach (var end in selected.Where(end => entries.All(entry => entry.End != end)))
            errors.Add($"{EndLabel(end)}：建库计划缺失（Step 1 已勾选该端）。");

        return Task.FromResult(errors.Count == 0
            ? BasicInfoValidation.Ok()
            : BasicInfoValidation.Fail(errors.ToArray()));
    }

    /// <inheritdoc />
    public void ApplyRepositoryPlan(IEnumerable<EndRepository> plan)
    {
        var spec = Current;
        if (spec is null || plan is null)
            return;

        foreach (var incoming in plan)
        {
            var name = incoming.RepositoryName?.Trim() ?? string.Empty;
            var visibility = NormalizeVisibility(incoming.Visibility);

            var target = spec.Repositories.FirstOrDefault(repository => repository.End == incoming.End);
            if (target is null)
            {
                spec.Repositories.Add(new EndRepository
                {
                    End = incoming.End,
                    TemplateRepository = incoming.TemplateRepository,
                    TemplateBranch = incoming.TemplateBranch,
                    RepositoryName = name,
                    Visibility = visibility,
                    DefaultBranch = _settings.PushBranch,
                    LocalPath = ResolveRepositoryLocalPath(spec, incoming.End, name)
                });

                continue;
            }

            // 改名后本地目录随之改变：已克隆的端视为需要重新克隆（旧目录留在磁盘，由 Step 3 的三选一处置）
            if (!string.Equals(target.RepositoryName, name, StringComparison.Ordinal))
                target.Cloned = false;

            target.RepositoryName = name;
            target.Visibility = visibility;
            target.LocalPath = ResolveRepositoryLocalPath(spec, target.End, name);
        }

        SaveSpec(spec);
    }

    /// <inheritdoc />
    public async Task<RepositoryPrecheck> PrecheckRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Repositories;

        var result = new RepositoryPrecheck();
        var spec = Current;
        if (spec is null)
        {
            result.Errors.Add("尚未建立项目档案：请先完成 Step 1。");
            return result;
        }

        var plan = PlanRepositories(spec);
        result.OrganizationId = OrganizationHint;
        result.GroupName = BuildGroupName(spec);
        result.GroupParentId = _settings.TryGetGroupParentId();

        if (plan.Count == 0)
            result.Errors.Add("建库计划为空：Step 1 至少要选择后端与管理端。");

        var codeup = Resolve<CodeupClient>();
        if (codeup is null)
            result.Errors.Add("云效连接器不可用（宿主未注册 CodeupClient），无法预检 Step 3。");

        if (string.IsNullOrWhiteSpace(result.OrganizationId))
            result.Errors.Add("未配置云效组织 ID：请到 codex「设置 → 云效连接」填写后重新预检（PRD §4.9）。");

        if (string.IsNullOrWhiteSpace(await TryGetPatAsync(cancellationToken)))
            result.Errors.Add("未配置云效个人令牌（PAT）：请到 codex「设置 → 云效连接」填写后重新预检（PRD §4.9）。");

        if (string.IsNullOrWhiteSpace(result.GroupName))
            result.Errors.Add("项目代码组名为空：请检查插件配置 groupNameFormat（默认 {CODE} 需能由项目缩写展开出非空名字）。");

        // 前置不满足即不再联网（避免拿必失败的请求去打云效）
        if (result.Errors.Count > 0 || codeup is null)
        {
            Record(Step, "Step 3 预检", string.Join("；", result.Errors), StepStatus.Failed, TimeSpan.Zero);
            return result;
        }

        var organizationId = result.OrganizationId!;
        using var permissionScope = EnterPermissionScope();
        var watch = Stopwatch.StartNew();

        // ── ① 目标代码组（只查不建）──────────────────────────────────────────────
        try
        {
            var group = await FindGroupByNameAsync(codeup, result.GroupName, result.GroupParentId, organizationId, cancellationToken);
            result.GroupExists = group is not null;
            result.GroupId = group?.Id;
            result.GroupWebUrl = group?.WebUrl;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"查询目标代码组失败：{result.GroupName} —— {ex.Message}");
        }

        // ── ② 逐端：模板库定位 + pin 解析 + 同名仓库检查 + 落地目录──────────────────
        foreach (var repository in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var end = new EndPrecheck
            {
                End = repository.End,
                TemplateRepository = repository.TemplateRepository,
                RepositoryName = repository.RepositoryName,
                Visibility = NormalizeVisibility(repository.Visibility),
                LocalPath = ResolveRepositoryLocalPath(spec, repository.End, repository.RepositoryName)
            };
            result.Ends.Add(end);

            try
            {
                var located = await LocateTemplateAsync(codeup, repository, organizationId, cancellationToken);
                if (located.Template is null)
                {
                    end.Error = located.Error ?? $"定位不到模板仓「{repository.TemplateRepository}」。";
                    continue;
                }

                end.TemplateProjectId = located.Template.ProjectId;
                end.TemplatePath = located.Template.Path;
                end.TemplateRepository = string.IsNullOrWhiteSpace(located.Template.Name)
                    ? repository.TemplateRepository
                    : located.Template.Name!;

                var pin = await ResolvePinAsync(codeup, located.Template.ProjectId, repository.End, organizationId, cancellationToken);
                end.TemplateBranches = pin.Branches;
                end.ResolvedBranch = pin.Branch;
                end.ResolvedBranchSource = pin.Source;
                if (string.IsNullOrWhiteSpace(pin.Branch))
                {
                    end.Error = $"模板 pin 解析失败：{pin.Error}";
                    continue;
                }

                var existing = await FindRepositoryByNameAsync(codeup, repository.RepositoryName, organizationId, cancellationToken);
                end.RepositoryExists = existing is not null;
                end.RepositoryWebUrl = string.IsNullOrWhiteSpace(existing?.HttpUrlToRepo)
                    ? null
                    : BuildWebUrl(existing!.HttpUrlToRepo!);

                if (end.LocalPath.Length > 0 && Directory.Exists(end.LocalPath)
                    && Directory.EnumerateFileSystemEntries(end.LocalPath).Any())
                {
                    end.LocalPathOccupied = true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                end.Error = ex.Message;
            }
        }

        foreach (var end in result.Ends.Where(item => item.LocalPathOccupied))
        {
            result.Warnings.Add($"{EndLabel(end.End)}本地目录已存在且非空（{end.LocalPath}）：执行时会请你"
                                + "「复用已克隆 / 清空后重新克隆 / 中止」三选一。");
        }

        watch.Stop();
        var summary = result.IsReady
            ? $"Step 3 预检通过：代码组 {result.GroupName}（{(result.GroupExists ? "已存在，将复用" : "不存在，将新建")}）· "
              + $"{result.Ends.Count} 端 · 耗时 {watch.Elapsed.TotalSeconds:F1}s"
            : "Step 3 预检未通过：" + string.Join("；", result.Errors.Concat(result.Ends
                .Where(end => !end.IsReady)
                .Select(end => $"{EndLabel(end.End)} {end.Error}")));

        Record(Step, "Step 3 预检", summary, result.IsReady ? StepStatus.Succeeded : StepStatus.Failed, watch.Elapsed);

        return result;
    }

    /// <inheritdoc />
    public async Task<StepResult> CreateRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Repositories;

        var spec = Current;
        if (spec is null)
        {
            Record(Step, "按模板建库并克隆", "尚未建立项目档案，请先完成第 1 步。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        using var permissionScope = EnterPermissionScope();

        // ── 前置校验：连接器 / 组织 ID / PAT / 建库计划 + pin（任一缺失即给可操作引导）───────
        var codeup = Resolve<CodeupClient>();
        if (codeup is null)
        {
            Record(Step, "按模板建库并克隆", "云效连接器不可用（宿主未注册 CodeupClient）。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "云效连接器不可用，无法执行 Step 3。");
        }

        var organizationId = OrganizationHint;
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            const string Tip = "未配置云效组织 ID：请到 codex「设置 → 云效连接」填写组织 ID 后重跑本步（PRD §4.9）。";
            Record(Step, "按模板建库并克隆", Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        if (string.IsNullOrWhiteSpace(await TryGetPatAsync(cancellationToken)))
        {
            const string Tip = "未配置云效个人令牌（PAT）：请到 codex「设置 → 云效连接」填写后重跑本步（PRD §4.9）。";
            Record(Step, "按模板建库并克隆", Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var planValidation = await ValidateRepositoryPlanAsync(spec.Repositories, cancellationToken);
        if (!planValidation.IsValid)
        {
            var tip = "建库计划校验未通过：" + string.Join("；", planValidation.Errors);
            Record(Step, "按模板建库并克隆", tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, tip);
        }

        spec.OrganizationId = organizationId;
        SaveSpec(spec);

        var plan = PlanRepositories(spec);
        var total = Stopwatch.StartNew();
        var artifacts = new List<RemoteArtifact>();

        try
        {
            // ── 0. 目标代码组：先查后建（PRD §4.3 Step 3；组名＝项目缩写经配置格式展开）────────
            var groupName = BuildGroupName(spec);
            var groupWatch = BeginOperation(Step, "准备项目代码组", $"{groupName} · 组织 {organizationId}");

            YunxiaoNamespace group;
            try
            {
                group = await EnsureGroupAsync(codeup, groupName, organizationId, cancellationToken);
            }
            catch (Exception ex)
            {
                var reason = $"准备项目代码组失败：{groupName} —— {ex.Message}";
                EndOperation(Step, "准备项目代码组", reason, StepStatus.Failed, groupWatch.Elapsed);
                SaveSpec(spec);
                return StepResult.Fail(Step, reason);
            }

            var groupArtifact = AddGroupArtifact(spec, group);
            if (groupArtifact is not null)
                artifacts.Add(groupArtifact);

            EndOperation(Step, "准备项目代码组",
                $"代码组 {group.PathWithNamespace ?? group.Name ?? groupName}（id {group.Id}）· {group.WebUrl}",
                StepStatus.Succeeded, groupWatch.Elapsed);
            SaveSpec(spec);

            foreach (var repository in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var label = EndLabel(repository.End);

                // 本端即将被重新处理：先把上一轮的「已就绪」标记置回未就绪，
                // 免得本轮失败后档案仍留着旧绿灯（RC4：不给假绿，Step 6 也据此拒推）。
                if (repository.Cloned)
                {
                    repository.Cloned = false;
                    SaveSpec(spec);
                }

                // ── 1. 模板库定位 + pin 解析（PRD §6 #5：不跟随升级；定位 / 解析不到即阻断）────
                var templateWatch = BeginOperation(Step, $"定位模板仓与 pin（{label}）", repository.TemplateRepository);

                var located = await LocateTemplateAsync(codeup, repository, organizationId, cancellationToken);
                if (located.Template is null)
                {
                    var reason = located.Error ?? $"定位不到模板仓「{repository.TemplateRepository}」。";
                    EndOperation(Step, $"定位模板仓与 pin（{label}）", reason, StepStatus.Failed, templateWatch.Elapsed);
                    SaveSpec(spec);
                    return StepResult.Fail(Step, reason);
                }

                var template = located.Template;
                var pin = await ResolvePinAsync(codeup, template.ProjectId, repository.End, organizationId, cancellationToken);
                if (string.IsNullOrWhiteSpace(pin.Branch))
                {
                    var reason = $"{label}模板 pin 解析失败：{pin.Error}";
                    EndOperation(Step, $"定位模板仓与 pin（{label}）", reason, StepStatus.Failed, templateWatch.Elapsed);
                    SaveSpec(spec);
                    return StepResult.Fail(Step, reason);
                }

                // 模板库命中项即为准（配置里的名字可能已被改名）：把实际使用的参数写回档案
                repository.TemplateRepository = string.IsNullOrWhiteSpace(template.Name)
                    ? repository.TemplateRepository
                    : template.Name!;
                repository.TemplateBranch = pin.Branch!;

                EndOperation(Step, $"定位模板仓与 pin（{label}）",
                    $"模板库命中 {repository.TemplateRepository}（id {template.ProjectId} · {template.Path}）"
                    + $"· pin {repository.TemplateBranch}（{pin.Source}）",
                    StepStatus.Succeeded, templateWatch.Elapsed);

                // ── 2. 目标仓库：同名复用（幂等①）or 以模板创建（落在项目代码组下）─────────────
                var repoWatch = BeginOperation(Step, $"准备仓库（{label}）", repository.RepositoryName);
                var existing = await FindRepositoryByNameAsync(codeup, repository.RepositoryName, organizationId, cancellationToken);
                YunxiaoRepository? remote;
                var createdNow = false;   // 本步是否新建了远端仓库（仅用于「等待分支就绪」的操作文案）

                if (existing is not null)
                {
                    remote = existing;
                    EndOperation(Step, $"准备仓库（{label}）",
                        $"云效已存在同名仓库，跳过创建直接复用（重跑幂等）：{repository.RepositoryName}（id {existing.Id}）",
                        StepStatus.Succeeded, repoWatch.Elapsed);
                }
                else
                {
                    var createWatch = BeginOperation(Step, $"创建仓库（{label}）",
                        $"{repository.TemplateRepository} → {repository.RepositoryName} · {VisibilityLabel(repository.Visibility)}"
                        + $" · 代码组 {group.PathWithNamespace ?? group.Name ?? groupName}");

                    remote = await codeup.CreateRepositoryAsync(
                        new YunxiaoCreateRepositoryRequest
                        {
                            Name = repository.RepositoryName,
                            Path = repository.RepositoryName,
                            Visibility = repository.Visibility,
                            NamespaceId = group.Id,
                            Description = $"{spec.Name} · {label}（由「新建项目」插件按模板 {repository.TemplateRepository} 创建）",
                            TemplateProject = new YunxiaoTemplateProject
                            {
                                TemplateProjectId = template.ProjectId,
                                TemplateType = 1,
                                SyncAllBranches = true
                            }
                        },
                        organizationId,
                        cancellationToken);

                    if (remote?.Id is null)
                    {
                        var reason = $"{label}建库失败：云效未返回新仓库 id（请检查代码组 "
                                     + $"{group.PathWithNamespace ?? group.Name ?? groupName} 的写权限、可见性与仓库名是否被占用）。";
                        EndOperation(Step, $"创建仓库（{label}）", reason, StepStatus.Failed, createWatch.Elapsed);
                        SaveSpec(spec);
                        return StepResult.Fail(Step, reason);
                    }

                    EndOperation(Step, $"创建仓库（{label}）",
                        $"已创建 {repository.RepositoryName}（id {remote!.Id}）· 代码组 {group.PathWithNamespace ?? group.Name ?? groupName}",
                        StepStatus.Succeeded, createWatch.Elapsed);
                    createdNow = true;
                    EndOperation(Step, $"准备仓库（{label}）", "创建完成。", StepStatus.Succeeded, repoWatch.Elapsed);
                }

                // ── 3. 补查 HTTPS 克隆地址（创建 / 列表返回均不含，PRD v0.14 订正）──────────
                var urlWatch = BeginOperation(Step, $"查询仓库地址（{label}）", "云效创建返回不含克隆地址，必须补查");
                var remoteRepoId = remote!.Id!.Value;
                var repositoryId = remoteRepoId.ToString(CultureInfo.InvariantCulture);
                var detail = await codeup.GetRepositoryAsync(repositoryId, organizationId, cancellationToken);
                var cloneUrl = detail?.HttpUrlToRepo;
                if (string.IsNullOrWhiteSpace(cloneUrl))
                {
                    var reason = $"云效未返回仓库 HTTPS 克隆地址（{label} {repository.RepositoryName}）。";
                    EndOperation(Step, $"查询仓库地址（{label}）", reason, StepStatus.Failed, urlWatch.Elapsed);
                    SaveSpec(spec);
                    return StepResult.Fail(Step, reason);
                }

                repository.ExternalId = repositoryId;
                repository.CloneUrl = cloneUrl!;
                repository.WebUrl = BuildWebUrl(cloneUrl!);
                repository.DefaultBranch = string.IsNullOrWhiteSpace(detail?.DefaultBranch)
                    ? _settings.PushBranch
                    : detail!.DefaultBranch!;
                repository.LocalPath = ResolveRepositoryLocalPath(spec, repository.End, repository.RepositoryName);
                EndOperation(Step, $"查询仓库地址（{label}）", cloneUrl!, StepStatus.Succeeded, urlWatch.Elapsed);

                // ── 4. 本地目录非空 → 三选一（幂等②，PRD §4.3 Step 3）──────────────────────
                var reuseLocal = false;
                if (Directory.Exists(repository.LocalPath) && Directory.EnumerateFileSystemEntries(repository.LocalPath).Any())
                {
                    var conflictWatch = BeginOperation(Step, $"处理已存在的本地目录（{label}）", repository.LocalPath);
                    var choice = await AskCloneConflictAsync(repository, cancellationToken);
                    if (choice is null)
                    {
                        var reason = $"目标目录已存在且非空：{repository.LocalPath}。"
                                     + "请在向导面板中选择「复用已克隆 / 清空后重克隆 / 中止」后重跑本步。";
                        EndOperation(Step, $"处理已存在的本地目录（{label}）", reason, StepStatus.Failed, conflictWatch.Elapsed);
                        SaveSpec(spec);
                        return StepResult.Fail(Step, reason);
                    }

                    if (choice == CloneConflictChoice.Abort)
                    {
                        // 远端仓库已创建，属孤儿产物：登记后中止，绝不自动删除（PRD §4.7）
                        AddArtifact(spec, repository, RemoteArtifactKind.Repository);
                        SaveSpec(spec);

                        var reason = $"用户选择「中止」：{label} 远端仓库 {repository.RepositoryName} 保留（孤儿清单），本地目录未改动。";
                        EndOperation(Step, $"处理已存在的本地目录（{label}）", reason, StepStatus.Failed, conflictWatch.Elapsed);
                        return StepResult.Fail(Step, reason);
                    }

                    if (choice == CloneConflictChoice.Reclone)
                    {
                        // 删除必须走 DirectoryCleaner：git 会把 pack 文件标成只读，裸 Directory.Delete 必失败，
                        // 且会在抛错前把目录删成「只剩只读文件」的残缺态，让三选一两条路同时失效（RC5）
                        try
                        {
                            DirectoryCleaner.DeleteForce(repository.LocalPath);
                        }
                        catch (IOException ex)
                        {
                            var reason = $"清空本地目录失败，本步中止：{ex.Message}";
                            EndOperation(Step, $"处理已存在的本地目录（{label}）", reason, StepStatus.Failed, conflictWatch.Elapsed);
                            SaveSpec(spec);
                            return StepResult.Fail(Step, reason);
                        }

                        EndOperation(Step, $"处理已存在的本地目录（{label}）",
                            $"已清空 {repository.LocalPath}（用户选择「清空后重克隆」）", StepStatus.Succeeded, conflictWatch.Elapsed);
                    }
                    else
                    {
                        // 复用前健康校验（RC4）：坏克隆（HEAD = ref: refs/heads/.invalid）不得被当成「已克隆」盖章通过
                        var verifyWatch = BeginOperation(Step, $"校验本地目录（{label}）",
                            $"{repository.LocalPath} · 要求 HEAD 可解析且存在分支 {repository.TemplateBranch}");

                        // 本机 git 出口门禁（PRD §3.7）已内置于 RunGitAsync，无需在此重复声明
                        var runner = Resolve<ProcessRunner>();
                        var verifyError = runner is null
                            ? "宿主未注册 ProcessRunner，无法校验本地目录的 Git 状态。"
                            : await VerifyWorktreeAsync(runner, repository.LocalPath, repository.TemplateBranch, cancellationToken);

                        if (verifyError is not null)
                        {
                            var reason = $"不能「复用已克隆」：{verifyError}。"
                                         + "该目录不是可用的 Git 工作树（常见于上一次克隆中途失败留下的残骸——"
                                         + "git 失败时会写入 .git/HEAD = ref: refs/heads/.invalid）；"
                                         + "请在向导面板重跑本步并选择「清空后重克隆」，或手动删除该目录后重跑。";
                            EndOperation(Step, $"校验本地目录（{label}）", reason, StepStatus.Failed, verifyWatch.Elapsed);
                            SaveSpec(spec);
                            return StepResult.Fail(Step, reason);
                        }

                        EndOperation(Step, $"校验本地目录（{label}）",
                            "校验通过：HEAD 可解析且 pin 分支存在。", StepStatus.Succeeded, verifyWatch.Elapsed);

                        reuseLocal = true;
                        EndOperation(Step, $"处理已存在的本地目录（{label}）",
                            $"复用已克隆目录 {repository.LocalPath}（用户选择「复用已克隆」，本端跳过克隆）",
                            StepStatus.Succeeded, conflictWatch.Elapsed);
                    }
                }

                // ── 5. 克隆（按模板 pin 版本，PRD §6 #17：git clone 必须带 -b）────────────
                if (reuseLocal)
                {
                    Record(Step, $"克隆仓库（{label}）", "复用已克隆目录，跳过克隆。", StepStatus.Skipped, TimeSpan.Zero);
                }
                else
                {
                    // ── 5a. 先等 pin 分支真实就绪（RC1）：建库 API 返回 ≠ 模板分支已同步 ────────
                    var readyWatch = BeginOperation(Step, $"等待模板分支就绪（{label}）",
                        $"pin {repository.TemplateBranch} · 模板同步在服务端异步进行"
                        + (createdNow ? "（本步新建仓库）" : "（复用既有仓库）"));

                    var ready = await WaitForBranchReadyAsync(
                        codeup, remoteRepoId, repository.TemplateBranch, organizationId, cancellationToken);
                    if (!ready)
                    {
                        var reason = $"{label}模板分支未就绪：等待 {BranchReadyTimeout.TotalSeconds:F0}s 后，"
                                     + $"{repository.RepositoryName}（id {remoteRepoId}）仍无分支 {repository.TemplateBranch}。"
                                     + "远端仓库已保留，可稍后重跑本步（同名复用，不会重复建库）。";
                        EndOperation(Step, $"等待模板分支就绪（{label}）", reason, StepStatus.Failed, readyWatch.Elapsed);
                        SaveSpec(spec);
                        return StepResult.Fail(Step, reason);
                    }

                    EndOperation(Step, $"等待模板分支就绪（{label}）",
                        $"pin {repository.TemplateBranch} 已就绪（{readyWatch.Elapsed.TotalSeconds:F1}s）",
                        StepStatus.Succeeded, readyWatch.Elapsed);

                    // ── 5b. 克隆（失败时 git 会留下残骸，按「是否本步新建」决定是否清理，RC3）──────
                    var cloneWatch = BeginOperation(Step, $"克隆仓库（{label}）", $"git clone -b {repository.TemplateBranch} {cloneUrl}");

                    var parent = Path.GetDirectoryName(repository.LocalPath);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    // 只有本步新建的目录才允许在失败时清理；原本就存在的目录交由三选一处置，不越权删除
                    var dirPreExisted = Directory.Exists(repository.LocalPath);

                    var clone = await codeup.CloneToLocalAsync(
                        cloneUrl!, repository.LocalPath, repository.TemplateBranch, cancellationToken);
                    if (!clone.Success)
                    {
                        var cleaned = TryCleanPartialClone(repository.LocalPath, dirPreExisted);
                        var reason = $"克隆失败：{label} {cloneUrl}（-b {repository.TemplateBranch}）—— {clone.FailureSummary()}。"
                                     + (cleaned ? "已清理本次克隆留下的残留目录。" : string.Empty)
                                     + "远端仓库已保留，可稍后重跑本步（同名复用，不会重复建库）。";
                        EndOperation(Step, $"克隆仓库（{label}）", reason, StepStatus.Failed, cloneWatch.Elapsed);
                        SaveSpec(spec);
                        return StepResult.Fail(Step, reason);
                    }

                    EndOperation(Step, $"克隆仓库（{label}）", $"已克隆到 {repository.LocalPath}", StepStatus.Succeeded, cloneWatch.Elapsed);
                }

                // ── 6. 占位符替换：命名空间 / 项目名 / 缩写（PRD §4.3 Step 3）───────────────
                var replaceWatch = BeginOperation(Step, $"替换占位符（{label}）", "命名空间 / 项目名 / 缩写（配置驱动，区分大小写）");
                var replaced = PlaceholderReplacer.Apply(repository.LocalPath, _settings.BuildReplacements(spec.Code, spec.Name));
                EndOperation(Step, $"替换占位符（{label}）",
                    $"改写 {replaced.FilesChanged} 个文件 · 重命名 {replaced.EntriesRenamed} 项 · 跳过 {replaced.FilesSkipped} 项",
                    StepStatus.Succeeded, replaceWatch.Elapsed);

                // ── 7. 落库：仓库信息 + 远端产物（每条即时持久化，中途失败也不丢账）───────────
                // Cloned 语义＝「本地目录已就绪」：真克隆成功，或「复用已克隆」经健康校验通过
                // （两条路径的失败分支均已提前 return，故走到这里必然可用）。
                repository.Cloned = true;
                var artifact = AddArtifact(spec, repository, RemoteArtifactKind.Repository);
                if (artifact is not null)
                    artifacts.Add(artifact);

                SaveSpec(spec);
            }

            // 本步重跑成功＝本地代码可能已随模板 / pin / 占位符替换而变：作废上一轮编译结论。
            // 不作废就会出现「改完代码不重编也能推」——闸门按档案事实判定，会误放行。
            InvalidateBuildConclusion(spec);

            // 同理作废上一轮流水线结论：流水线 YAML 里写死了代码源地址与分支，仓库改名 / 重建后旧结论是误导。
            // 只清结论，不删云效上的流水线本体（远端产物一律不自动删除，PRD §4.5）。
            InvalidatePipelineConclusion(spec);

            spec.AdvanceTo(WizardStep.Database);
            SaveSpec(spec);

            total.Stop();
            var message = $"Step 3 完成：{plan.Count} 端仓库就绪（建库 / 复用 + 克隆 + 占位符替换）· 耗时 {total.Elapsed.TotalSeconds:F1}s";
            Record(Step, "Step 3 完成", message, StepStatus.Succeeded, total.Elapsed);
            _host?.Log($"[newproject] {message}");

            return StepResult.Ok(Step, message, artifacts);
        }
        catch (OperationCanceledException)
        {
            FailRunningOperation(Step, "用户取消。", total.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, "Step 3 建库并克隆已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 3 建库并克隆失败", ex);
            FailRunningOperation(Step, ex.Message, total.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step,
                $"Step 3 失败：{ex.Message}（远端产物一律保留，可在云效控制台确认；修正后可直接重跑本步）");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>执行形态（路线 ②）</b>：插件把参数交给项目会话的助手，由助手用脚本工具自己连库探活，并把结论写进约定文件；插件回读结论、给出可展示的判定。</para>
    /// <para><b>v0.22 口径</b>：只探活——不写任何项目文件 / <c>appsettings</c>、不建库、不执行任何 SQL；判定只看「能连上且库存在」，结果<b>不阻断</b>，本步结束即推进到 Step 5。</para>
    /// <para><b>口令</b>：只在内存（内联进本轮提示词），不落档案、不写日志、操作记录一律 <c>***</c>。</para>
    /// </remarks>
    public async Task<StepResult> SetupDatabaseAsync(DatabaseProbeRequest request, CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Database;
        const string Action = "数据库探活（只读）";

        var spec = Current;
        if (spec is null)
        {
            Record(Step, Action, "尚未建立项目档案，请先完成 Step 1。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        if (request is null)
        {
            const string Empty = "探活参数为空。";
            Record(Step, Action, Empty, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Empty);
        }

        if (!request.TryValidate(out var invalid))
        {
            Record(Step, Action, invalid, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, invalid);
        }

        if (string.IsNullOrWhiteSpace(spec.LocalPath))
        {
            const string Tip = "档案缺少本地目录，无法落结论文件：请重跑 Step 1 / Step 2。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var watch = BeginOperation(Step, Action, $"目标 {request.Describe()}");

        try
        {
            var resultFile = StepResultFile.BuildPath(spec.LocalPath, 4);
            StepResultFile.EnsureDirectory(resultFile);

            var outcome = await CreateBridge()
                .RunAsync(spec, StepPrompts.BuildDatabaseProbePrompt(spec, request, resultFile), resultFile, cancellationToken)
                .ConfigureAwait(false);

            // 非敏感参数回填档案（口令不落档案）
            spec.Database.Provider = "mysql";
            spec.Database.Host = request.Host.Trim();
            spec.Database.Port = request.Port;
            spec.Database.Database = request.Database.Trim();
            spec.Database.User = request.User.Trim();
            if (outcome.SessionCreated && !string.IsNullOrWhiteSpace(outcome.SessionId))
                spec.SessionId = outcome.SessionId;

            var (probed, message) = InterpretProbe(outcome);
            spec.Database.ProbeOk = probed;
            spec.Database.ProbeMessage = message;
            spec.Database.ProbedAt = DateTimeOffset.Now;

            // 探活结果不阻断（v0.22 定案）：无论通过与否都推进到 Step 5
            spec.AdvanceTo(WizardStep.Build);

            var lines = DescribeTurn(outcome, resultFile);
            lines.Add($"结论：{message}");
            EndOperation(Step, Action, string.Join("\n", lines),
                probed ? StepStatus.Succeeded : StepStatus.Failed, watch.Elapsed);

            // 落库放在 EndOperation 之后：终态快照只回放终态记录，顺序反了本步终态就进不了 StepStates
            // （重启后步骤条退回「未开始」）。与 Step 5 / Step 6 同型。
            SaveSpec(spec);

            return probed ? StepResult.Ok(Step, message) : StepResult.Fail(Step, message);
        }
        catch (OperationCanceledException)
        {
            EndOperation(Step, Action, "已取消（档案未推进，可直接重跑本步）。", StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, "第 4 步数据库探活已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 4 数据库探活失败", ex);
            EndOperation(Step, Action, ex.Message, StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, $"第 4 步失败：{ex.Message}");
        }
    }

    /// <inheritdoc />
    public DatabaseProbeRequest SuggestDatabaseProbe()
    {
        var spec = Current;
        if (spec is null)
            return new DatabaseProbeRequest { Port = 3306 };

        // ① 档案里已填过的非敏感参数优先（重启后仍能回填，口令除外）
        if (!string.IsNullOrWhiteSpace(spec.Database.Host))
        {
            return new DatabaseProbeRequest
            {
                Host = spec.Database.Host,
                Port = spec.Database.Port > 0 ? spec.Database.Port : 3306,
                Database = spec.Database.Database,
                User = spec.Database.User
            };
        }

        // ② 否则从已克隆的后端仓库 WebHost/appsettings.json 的连接串推导（只读、绝不回填口令）
        var backend = PlanRepositories(spec)
            .FirstOrDefault(repository => repository.End == ProjectEnd.Service && repository.Cloned);

        if (backend is null || string.IsNullOrWhiteSpace(backend.LocalPath))
            return new DatabaseProbeRequest { Port = 3306 };

        return DatabaseProbeDefaults.FromBackendRepository(backend.LocalPath);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>执行形态（路线 ②）</b>：编译命令<b>由助手按工程类型自行判断</b>（后端 <c>dotnet build</c>、前端按本组织既有姿势 <c>fnm exec --using=16 -- npm.cmd run build:dev</c> 等），插件只提供白名单与结论契约。</para>
    /// <para><b>边界（v0.28 放宽）</b>：只编译、<b>不改业务</b>——允许修依赖树与环境（装依赖 / <c>fnm</c> 切 Node /
    /// <c>npm rebuild X</c> 补跑依赖自身的构建脚本 / 必要时重建 <c>node_modules</c>），禁止改业务源码与工程配置。</para>
    /// <para><b>失败阻断</b>：只要有一端未通过（含「助手漏给某端结论」），档案就不推进到 Step 6；修好后重跑本步即可。</para>
    /// </remarks>
    public async Task<StepResult> BuildAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Build;
        const string Action = "逐端编译检查";

        var spec = Current;
        if (spec is null)
        {
            Record(Step, Action, "尚未建立项目档案，请先完成 Step 1。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        var targets = PlanRepositories(spec)
            .Where(repository => repository.Cloned && !string.IsNullOrWhiteSpace(repository.LocalPath))
            .ToList();
        if (targets.Count == 0)
        {
            const string Tip = "没有任何已克隆的端可编译：请先完成 Step 3（按模板建库并克隆）。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        if (string.IsNullOrWhiteSpace(spec.LocalPath))
        {
            const string Tip = "档案缺少本地目录，无法落结论文件：请重跑 Step 1 / Step 2。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var ends = targets.Select(repository => repository.End).Distinct().ToList();
        var watch = BeginOperation(Step, Action, string.Join("、", ends.Select(EndLabel)) + "端");

        try
        {
            var resultFile = StepResultFile.BuildPath(spec.LocalPath, 5);
            StepResultFile.EnsureDirectory(resultFile);

            var outcome = await CreateBridge()
                .RunAsync(spec, StepPrompts.BuildCompilePrompt(spec, targets, resultFile), resultFile, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.SessionCreated && !string.IsNullOrWhiteSpace(outcome.SessionId))
                spec.SessionId = outcome.SessionId;

            var (passed, results, message) = InterpretBuild(outcome, ends);
            if (results.Count > 0)
                spec.BuildResults = results;

            // 失败阻断（D4）：未全部通过就不推进到 Step 6
            if (passed)
                spec.AdvanceTo(WizardStep.Push);

            var lines = DescribeTurn(outcome, resultFile);
            lines.Add($"结论：{message}");
            EndOperation(Step, Action, string.Join("\n", lines),
                passed ? StepStatus.Succeeded : StepStatus.Failed, watch.Elapsed);

            // 落库必须在 EndOperation **之后**：终态快照（StepStates）由 SaveSpec → SnapshotStepStates 按内存
            // 操作记录回放生成，且只回放终态记录；顺序反了（先保存后记终态）时本步记录仍是 Running，会被跳过，
            // 于是 StepStates 里根本没有 Build 键 → 闸门 CanPush 恒判否，编译通过了也推不动。
            // 与 Step 6（EndOperation → SaveSpec）同型。
            SaveSpec(spec);

            return passed ? StepResult.Ok(Step, message) : StepResult.Fail(Step, message);
        }
        catch (OperationCanceledException)
        {
            EndOperation(Step, Action, "已取消（档案未推进：本步失败会阻断 Step 6）。", StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, "第 5 步编译检查已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 5 编译检查失败", ex);
            EndOperation(Step, Action, ex.Message, StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, $"第 5 步失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 创建「插件 → 宿主会话助手」的桥接层（路线 ②）。
    /// </summary>
    /// <returns>桥接层实例（每次现建，用完即弃）。</returns>
    private HostAssistantBridge CreateBridge() => new(ResolveByType, ResolveSessionManager, _host);

    /// <summary>
    /// 把一轮提示词的事件流读数压成操作记录里的可读行（<b>不含口令</b>）。
    /// </summary>
    /// <param name="outcome">执行读数。</param>
    /// <param name="resultFile">结论文件路径。</param>
    /// <returns>多行文本。</returns>
    private static List<string> DescribeTurn(HostAssistantBridge.TurnOutcome outcome, string resultFile)
    {
        var lines = new List<string>
        {
            $"会话：{outcome.SessionId ?? "(无)"}（新建会话={outcome.SessionCreated} · 唤起运行时={outcome.RuntimeBootstrapped}"
                + $" · 下发时被聊天占用={outcome.WasBusy}）",
            $"耗时 {outcome.Elapsed.TotalSeconds:F1}s · 工具调用 "
                + (outcome.ToolCalls.Count == 0 ? "（无）" : string.Join(", ", outcome.ToolCalls))
                + $" · 正文 {outcome.TextLength} 字",
            $"工具审批：放行 {outcome.Approvals.Count} 次 / 拒绝 {outcome.Denials.Count} 次"
        };

        if (outcome.Denials.Count > 0)
            lines.Add("　被拒明细：" + string.Join("；", outcome.Denials.Take(3)));

        if (outcome.Errors.Count > 0)
            lines.Add("　错误事件：" + string.Join("；", outcome.Errors.Take(3)));

        lines.Add($"结论文件：{resultFile}（{(outcome.ResultFileContent is null ? "未产出" : "已回读")}）");

        if (!string.IsNullOrWhiteSpace(outcome.Failure))
            lines.Add("　未跑起来：" + outcome.Failure);

        return lines;
    }

    /// <summary>
    /// Step 4 判定：四环齐才算通过（结论文件存在 + 可解析 + <c>ok</c> + 事件流无错误）。
    /// </summary>
    /// <param name="outcome">执行读数。</param>
    /// <returns>是否通过 + 可展示结论。</returns>
    private static (bool Ok, string Message) InterpretProbe(HostAssistantBridge.TurnOutcome outcome)
    {
        if (!outcome.Ran)
            return (false, $"本步未能跑起来：{outcome.Failure}");

        if (outcome.ResultFileContent is null)
            return (false, $"助手未产出结论文件（{outcome.ResultFile}），无法判定探活结果。");

        if (!StepReportParser.TryParse(outcome.ResultFileContent, out var report, out var parseError))
            return (false, parseError);

        return StepReportEvaluator.EvaluateProbe(report!, outcome.Errors);
    }

    /// <summary>
    /// Step 5 判定：逐端归并 + 失败阻断判定。
    /// </summary>
    /// <param name="outcome">执行读数。</param>
    /// <param name="ends">本次要求编译的端。</param>
    /// <returns>是否通过 + 逐端结果 + 可展示结论。</returns>
    private static (bool Ok, List<EndBuildResult> Results, string Message) InterpretBuild(
        HostAssistantBridge.TurnOutcome outcome,
        IReadOnlyList<ProjectEnd> ends)
    {
        if (!outcome.Ran)
            return (false, new List<EndBuildResult>(), $"本步未能跑起来：{outcome.Failure}");

        if (outcome.ResultFileContent is null)
            return (false, new List<EndBuildResult>(), $"助手未产出结论文件（{outcome.ResultFile}），无法判定编译结果。");

        if (!StepReportParser.TryParse(outcome.ResultFileContent, out var report, out var parseError))
            return (false, new List<EndBuildResult>(), parseError);

        return StepReportEvaluator.EvaluateBuild(report!, ends, outcome.Errors);
    }

    /// <inheritdoc />
    public async Task<StepResult> PushAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Push;

        var spec = Current;
        if (spec is null)
        {
            Record(Step, "推送改动到自有仓库", "尚未建立项目档案，请先完成第 1 步。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        // 编译失败阻断 Step 6（PRD §4.3 Step 5 / §4.7）：向导的步骤顺序只是界面约束——常驻「重新推送」、
        // 步骤条回退、聊天入口都能绕开，故此处按档案事实做硬校验（判据见 StepGate.CanPush）。
        // 位置在前置检查之前：顺序约束比参数齐备更根本，报错先给最该修的那一条。
        // 拒绝不记操作记录——一次「未执行的拒绝」不是本步的终态，记了会被 SnapshotStepStates 回放写进档案，
        // 把早已成功的 Push 状态改写成失败（提示由调用方按 StepResult.Message 呈现）。
        if (!StepGate.CanPush(spec))
        {
            const string Tip = "编译（Step 5）尚未全部通过，已拒绝推送：请到步骤条的「编译」步逐端编译，"
                               + "全部通过后本步才可执行（编译结论见 Step 5 的逐端状态行与右侧操作记录）。";
            return StepResult.Fail(Step, Tip);
        }

        using var permissionScope = EnterPermissionScope();

        var codeup = Resolve<CodeupClient>();
        var runner = Resolve<ProcessRunner>();
        if (codeup is null || runner is null)
        {
            const string Tip = "宿主能力不完整：云效连接器或进程执行器（用于本机 git）不可用，无法执行 Step 6。";
            Record(Step, "推送改动到自有仓库", Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        if (string.IsNullOrWhiteSpace(await TryGetPatAsync(cancellationToken)))
        {
            const string Tip = "未配置云效个人令牌（PAT）：请到 codex「设置 → 云效连接」填写后重跑本步（PRD §4.9）。";
            Record(Step, "推送改动到自有仓库", Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var pushable = PlanRepositories(spec)
            .Where(repository => repository.Cloned && !string.IsNullOrWhiteSpace(repository.LocalPath))
            .ToList();
        if (pushable.Count == 0)
        {
            const string Tip = "没有任何已克隆的仓库可推送：请先完成 Step 3（按模板建库并克隆）。";
            Record(Step, "推送改动到自有仓库", Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var total = Stopwatch.StartNew();
        var artifacts = new List<RemoteArtifact>();

        try
        {
            foreach (var repository in pushable)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var label = EndLabel(repository.End);
                var branch = ResolvePushBranch(repository);

                // 本机 git 出口门禁（PRD §3.7）已内置于 RunGitAsync；连接器内的 push 由其自身门禁覆盖

                if (!Directory.Exists(repository.LocalPath) || !Directory.Exists(Path.Combine(repository.LocalPath, ".git")))
                {
                    var reason = $"{label}本地目录不是 git 工作区：{repository.LocalPath}（请重跑 Step 3 完成克隆）。";
                    Record(Step, $"准备推送（{label}）", reason, StepStatus.Failed, TimeSpan.Zero);
                    SaveSpec(spec);
                    return StepResult.Fail(Step, reason);
                }

                // ── 1. 切到目标分支（PRD §6 #17：pushBranch 优先、v0.28 起默认 develop）────────────
                var branchWatch = BeginOperation(Step, $"整理分支（{label}）", $"目标分支 {branch}");
                var hasBranch = await RunGitAsync(runner, repository.LocalPath,
                    new[] { "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}" }, cancellationToken);

                var checkoutArgs = hasBranch.ExitCode == 0
                    ? new[] { "checkout", branch }
                    : new[] { "checkout", "-b", branch };
                var checkout = await RunGitAsync(runner, repository.LocalPath, checkoutArgs, cancellationToken);
                if (checkout.ExitCode != 0)
                {
                    var reason = $"{label}切换分支失败：{Summarize(checkout)}";
                    EndOperation(Step, $"整理分支（{label}）", reason, StepStatus.Failed, branchWatch.Elapsed);
                    SaveSpec(spec);
                    return StepResult.Fail(Step, reason);
                }

                EndOperation(Step, $"整理分支（{label}）",
                    hasBranch.ExitCode == 0
                        ? $"已在 {branch} 分支。"
                        : $"已创建并切换到 {branch}（自模板 pin {repository.TemplateBranch}）。",
                    StepStatus.Succeeded, branchWatch.Elapsed);

                // ── 2. 暂存 + 提交（PRD §6 #17：推送前至少一次提交）────────────────────────
                var commitWatch = BeginOperation(Step, $"提交改动（{label}）", "git add -A → 有改动则 commit");
                var add = await RunGitAsync(runner, repository.LocalPath, new[] { "add", "-A" }, cancellationToken);
                if (add.ExitCode != 0)
                {
                    var reason = $"{label}暂存改动失败：{Summarize(add)}";
                    EndOperation(Step, $"提交改动（{label}）", reason, StepStatus.Failed, commitWatch.Elapsed);
                    SaveSpec(spec);
                    return StepResult.Fail(Step, reason);
                }

                var diff = await RunGitAsync(runner, repository.LocalPath, new[] { "diff", "--cached", "--quiet" }, cancellationToken);
                if (diff.ExitCode == 0)
                {
                    EndOperation(Step, $"提交改动（{label}）", "工作区无改动，跳过提交。", StepStatus.Skipped, commitWatch.Elapsed);
                }
                else
                {
                    var (authorName, authorEmail) = await ResolveCommitAuthorAsync(runner, repository.LocalPath, cancellationToken);
                    var commitMessage = $"chore: 初始化 {spec.Name}（{label}）——由「新建项目」向导按模板 {repository.TemplateRepository} 生成";
                    var commit = await RunGitAsync(runner, repository.LocalPath, new[]
                    {
                        "-c", $"user.name={authorName}",
                        "-c", $"user.email={authorEmail}",
                        "commit", "-m", commitMessage
                    }, cancellationToken);

                    if (commit.ExitCode != 0)
                    {
                        var reason = $"{label}提交失败：{Summarize(commit)}";
                        EndOperation(Step, $"提交改动（{label}）", reason, StepStatus.Failed, commitWatch.Elapsed);
                        SaveSpec(spec);
                        return StepResult.Fail(Step, reason);
                    }

                    EndOperation(Step, $"提交改动（{label}）",
                        $"已提交（{authorName} <{authorEmail}>）：{commitMessage}", StepStatus.Succeeded, commitWatch.Elapsed);
                }

                // ── 3. 推送（连接器负责带鉴权的 URL 与自身出口门禁；v0.29：云效限流退避重试）──
                var pushWatch = BeginOperation(Step, $"推送到自有仓库（{label}）", $"{repository.RepositoryName} · {branch}");
                var pushed = await codeup.PushAsync(repository.CloneUrl, repository.LocalPath, branch, force: false, cancellationToken);

                // 只对「服务端限流」退避重试（10s → 20s → 30s，最多 3 次）；非快进 / 鉴权 / 权限类失败一律不重试。
                // 等待期间原地改写同一条「进行中」记录的说明，用户能看见「在等什么、还要等多久」。
                var retried = 0;
                while (retried < PushRetryPolicy.MaxRetries && PushRetryPolicy.ShouldRetry(pushed))
                {
                    retried++;
                    UpdateRunningOperation(Step, $"推送到自有仓库（{label}）",
                        PushRetryPolicy.WaitingDetail(repository.RepositoryName, branch, retried, pushed.FailureSummary()));

                    await Task.Delay(PushRetryPolicy.DelayBefore(retried), cancellationToken);

                    pushed = await codeup.PushAsync(repository.CloneUrl, repository.LocalPath, branch, force: false, cancellationToken);
                }

                if (!pushed.Success)
                {
                    var reason = PushRetryPolicy.DescribeFailure(repository.RepositoryName, branch, pushed, retried);
                    EndOperation(Step, $"推送到自有仓库（{label}）", reason, StepStatus.Failed, pushWatch.Elapsed);
                    SaveSpec(spec);
                    return StepResult.Fail(Step, reason);
                }

                EndOperation(Step, $"推送到自有仓库（{label}）",
                    retried == 0
                        ? $"已推送 {branch} → {repository.WebUrl}"
                        : $"已推送 {branch} → {repository.WebUrl}（云效限流后，第 {retried} 次重试成功）",
                    StepStatus.Succeeded, pushWatch.Elapsed);

                var artifact = AddArtifact(spec, repository, RemoteArtifactKind.Branch, branch);
                if (artifact is not null)
                    artifacts.Add(artifact);

                SaveSpec(spec);
            }

            spec.AdvanceTo(WizardStep.Pipeline);
            SaveSpec(spec);

            total.Stop();
            var message = $"Step 6 完成：{pushable.Count} 端改动已推送 · 耗时 {total.Elapsed.TotalSeconds:F1}s";
            Record(Step, "Step 6 完成", message, StepStatus.Succeeded, total.Elapsed);
            _host?.Log($"[newproject] {message}");

            return StepResult.Ok(Step, message, artifacts);
        }
        catch (OperationCanceledException)
        {
            FailRunningOperation(Step, "用户取消。", total.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, "Step 6 推送已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 6 推送失败", ex);
            FailRunningOperation(Step, ex.Message, total.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, $"Step 6 失败：{ex.Message}（已推送的端不受影响；修正后可直接重跑本步）");
        }
    }

    /// <inheritdoc />
    public Task<StepResult> CreatePipelinesAsync(CancellationToken cancellationToken = default)
        => RunPipelineStepAsync(includeDeployStage: false, cancellationToken);

    /// <inheritdoc />
    public Task<StepResult> AppendDeployStagesAsync(CancellationToken cancellationToken = default)
        => RunPipelineStepAsync(includeDeployStage: true, cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<PipelinePreview> PreviewPipelines()
    {
        var spec = Current;
        if (spec is null)
            return Array.Empty<PipelinePreview>();

        // 与创建口径同源：同一渲染器、同一过滤条件（已建库且已克隆的所选端），只是不调云效、不落档案。
        return PlanRepositories(spec)
            .Where(repository => repository.Cloned && !string.IsNullOrWhiteSpace(repository.CloneUrl))
            .Select(repository =>
            {
                var rendered = PipelineRenderer.Render(spec, repository, _settings, includeDeployStage: false);
                var existing = spec.PipelineResults.FirstOrDefault(item => item.End == repository.End);

                return new PipelinePreview
                {
                    End = repository.End,
                    PipelineName = rendered.PipelineName,
                    Yaml = rendered.Yaml,
                    PendingBindings = rendered.PendingBindings,
                    PipelineId = existing?.PipelineId ?? string.Empty,
                    WebUrl = existing?.WebUrl ?? string.Empty,
                    DeployStageIncluded = existing?.DeployStageIncluded ?? false
                };
            })
            .ToList();
    }

    /// <summary>
    /// Step 7 执行体（PRD §4.3 Step 7 <b>v0.30 口径</b>）：内嵌模板渲染新版 YAML → 同名幂等检索 → 创建 / 更新。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么是「渲染后创建」而不是「克隆参考流水线」</b>：云效 OpenAPI 既没有「复制流水线」接口，
    /// 也只收顶层 <c>stages:</c> 的新版 YAML（喂 tb 形态 <c>flow</c> 原文必报 <c>1209300</c>）；组织内 70 条流水线
    /// 全是 tb 形态，取不到可提交范本。故改为「按企服版语义翻译成新版 YAML 模板 → 本地渲染 → 提交创建」，
    /// 变量与缓存由模板直接生成（见 <see cref="PipelineTemplateLibrary"/>）。
    /// </para>
    /// <para>
    /// <b>两条入口共用本执行体</b>：<c>includeDeployStage=false</c> 为首次创建（<b>只含构建阶段</b>，
    /// 部署阶段以注释块附末尾，PRD §6 #14）；<c>true</c> 为「一键补齐部署阶段」（对该端已有流水线做
    /// <c>UpdatePipeline</c>，把部署阶段并入 YAML）。
    /// </para>
    /// <para>
    /// <b>幂等</b>：创建前按流水线名精确检索，命中即复用（不再重复建），与 Step 3 建库同口径。
    /// </para>
    /// <para><b>建完不自动运行</b>（PRD §6 #14）：本步不发 <c>CreatePipelineRun</c>。</para>
    /// </remarks>
    /// <param name="includeDeployStage">是否包含部署阶段。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>步骤结果（含逐端流水线地址）。</returns>
    private async Task<StepResult> RunPipelineStepAsync(bool includeDeployStage, CancellationToken cancellationToken)
    {
        const WizardStep Step = WizardStep.Pipeline;
        var action = includeDeployStage ? "补齐部署阶段" : "创建云效流水线";

        var spec = Current;
        if (spec is null)
        {
            Record(Step, action, "尚未建立项目档案，请先完成第 1 步。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        // 闸门：与「编译失败阻断推送」同口径，只认档案事实（界面顺序 / 常驻入口 / 聊天入口都能绕开）。
        // 首次创建要求 Step 6 已成功；补齐要求该端流水线已存在（尚无流水线时无从补齐，按端另行提示）。
        if (!includeDeployStage && !StepGate.CanCreatePipelines(spec))
        {
            const string Tip = "推送（Step 6）尚未全部成功，已拒绝创建流水线：仓库里还没有可构建的代码，"
                               + "请先完成 Step 6（推送）再执行本步（结论见 Step 6 的逐端状态行与右侧操作记录）。";
            return StepResult.Fail(Step, Tip);
        }

        using var permissionScope = EnterPermissionScope();

        var flow = Resolve<FlowClient>();
        if (flow is null)
        {
            const string Tip = "宿主能力不完整：云效流水线客户端（FlowClient）不可用，无法执行 Step 7。";
            Record(Step, action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        if (string.IsNullOrWhiteSpace(await TryGetPatAsync(cancellationToken)))
        {
            const string Tip = "未配置云效个人令牌（PAT）：请到 codex「设置 → 云效连接」填写后重跑本步（PRD §4.9）。";
            Record(Step, action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var targets = PlanRepositories(spec)
            .Where(repository => repository.Cloned && !string.IsNullOrWhiteSpace(repository.CloneUrl))
            .ToList();
        if (targets.Count == 0)
        {
            const string Tip = "没有任何已建库并克隆的仓库可建流水线：请先完成 Step 3（按模板建库并克隆）。";
            Record(Step, action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var total = Stopwatch.StartNew();
        var artifacts = new List<RemoteArtifact>();
        var summary = new List<string>();

        try
        {
            foreach (var repository in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var label = EndLabel(repository.End);
                var previous = spec.PipelineResults.FirstOrDefault(item => item.End == repository.End);

                // 补齐：只对「已有流水线」且「该端确实有部署阶段模板」的端生效。
                // 前端两端无独立部署阶段（企服版参考流水线取证：OSS 上传即发布），按端跳过并如实说明。
                if (includeDeployStage)
                {
                    if (previous is null || string.IsNullOrWhiteSpace(previous.PipelineId))
                    {
                        summary.Add($"{label}尚未创建流水线，跳过补齐");
                        continue;
                    }

                    if (PipelineTemplateLibrary.DeployStageYaml(repository.End) is null)
                    {
                        summary.Add($"{label}无独立部署阶段（构建产物上传 OSS 即发布），跳过补齐");
                        continue;
                    }

                    // 补齐前置（真机口径）：VMDockerDeploy.machineGroup 必须是**主机组 uuid**（tb 的数字 id 会被拒）。
                    // 未绑定就直接给可读失败、不发请求——否则必然是 400「该机器组uuid[]不存在」，白跑一次。
                    if (string.IsNullOrWhiteSpace(_settings.FindPipelineDefault(repository.End)?.DeployMachineGroup))
                    {
                        const string Tip = "补齐部署阶段前需先绑定部署主机组：请在插件配置 newproject.settings.json 的 "
                                           + "pipelineDefaults[Service].deployMachineGroup 填入**主机组 uuid**"
                                           + "（云效控制台主机组详情可见；企服版 tb 的数字 id 226948 不被新版 YAML 接受，"
                                           + "报「该机器组uuid[226948]不存在」），再重跑「一键补齐部署阶段」。"
                                           + "该端其余人工项（Harbor 凭据 HARBOR_USER / HARBOR_PASSWORD、代码源服务连接）见「待人工绑定清单」。";
                        Record(Step, $"{action}（{label}）", Tip, StepStatus.Failed, TimeSpan.Zero);
                        SaveSpec(spec);
                        return StepResult.Fail(Step, Tip);
                    }
                }

                var rendered = PipelineRenderer.Render(spec, repository, _settings, includeDeployStage);
                var watch = BeginOperation(Step, $"{action}（{label}）", $"流水线名 {rendered.PipelineName}");

                // ── 1. 幂等：按名精确检索，命中即复用（PRD §4.3 Step 7）──────────────────
                var listed = await flow.ListPipelinesAsync(
                    pipelineName: rendered.PipelineName,
                    page: 1,
                    perPage: 10,
                    cancellationToken: cancellationToken);

                var hit = (listed ?? new List<YunxiaoPipeline>())
                    .FirstOrDefault(item => string.Equals(item.DisplayName, rendered.PipelineName, StringComparison.OrdinalIgnoreCase));

                var pipelineId = hit?.DisplayId;
                var created = false;

                // ── 2. 创建 / 更新 ───────────────────────────────────────────────────
                if (includeDeployStage)
                {
                    // 补齐：对该端已有流水线做整体覆盖更新（UpdatePipeline 会一并覆盖名称，故回传现名）
                    var updated = await flow.UpdatePipelineAsync(
                        previous!.PipelineId,
                        new YunxiaoUpdatePipelineRequest
                        {
                            PipelineName = previous.PipelineName,
                            Content = rendered.Yaml
                        },
                        cancellationToken: cancellationToken);

                    if (!updated)
                    {
                        EndOperation(Step, $"{action}（{label}）", "云效未接受更新请求", StepStatus.Failed, watch.Elapsed);
                        SaveSpec(spec);
                        return StepResult.Fail(Step, $"{action}失败（{label}）：云效未接受更新请求，请稍后重试。");
                    }

                    pipelineId = previous.PipelineId;
                }
                else if (string.IsNullOrWhiteSpace(pipelineId))
                {
                    pipelineId = await flow.CreatePipelineAsync(
                        new YunxiaoCreatePipelineRequest
                        {
                            PipelineName = rendered.PipelineName,
                            Content = rendered.Yaml
                        },
                        cancellationToken: cancellationToken);

                    if (string.IsNullOrWhiteSpace(pipelineId))
                    {
                        EndOperation(Step, $"{action}（{label}）", "云效未返回流水线 id", StepStatus.Failed, watch.Elapsed);
                        SaveSpec(spec);
                        return StepResult.Fail(Step, $"{action}失败（{label}）：云效未返回流水线 id，请稍后重试。");
                    }

                    created = true;
                }

                var webUrl = BuildPipelineWebUrl(pipelineId);

                // ── 3. 落档案（重跑幂等：同端覆盖，不重复入册）────────────────────────
                var result = previous ?? new EndPipelineResult { End = repository.End };
                result.PipelineId = pipelineId!;
                result.PipelineName = created || previous is null ? rendered.PipelineName : previous.PipelineName;
                result.WebUrl = webUrl;
                result.Created = created;
                result.DeployStageIncluded = includeDeployStage ? true : previous?.DeployStageIncluded ?? false;
                result.PendingBindings = rendered.PendingBindings.ToList();
                result.Yaml = rendered.Yaml;

                if (previous is null)
                    spec.PipelineResults.Add(result);

                var artifact = AddArtifact(spec, repository, RemoteArtifactKind.Pipeline, name: rendered.PipelineName, externalId: pipelineId, webUrl: webUrl);
                if (artifact is not null)
                    artifacts.Add(artifact);

                var outcome = created
                    ? $"已创建（{rendered.PendingBindings.Count} 项待人工绑定）"
                    : includeDeployStage ? "已并入部署阶段" : "命中同名流水线，已复用";
                EndOperation(Step, $"{action}（{label}）", $"{outcome} · {webUrl}", StepStatus.Succeeded, watch.Elapsed);
                summary.Add($"{label}{outcome}");

                SaveSpec(spec);
            }

            spec.AdvanceTo(WizardStep.Pipeline);
            SaveSpec(spec);

            total.Stop();
            var message = $"Step 7 完成：{summary.Count} 端流水线已就绪（{string.Join(" · ", summary)}）· 耗时 {total.Elapsed.TotalSeconds:F1}s";
            Record(Step, "Step 7 完成", message, StepStatus.Succeeded, total.Elapsed);
            _host?.Log($"[newproject] {message}");

            return StepResult.Ok(Step, message, artifacts);
        }
        catch (OperationCanceledException)
        {
            FailRunningOperation(Step, "用户取消。", total.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, "Step 7 已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 7 创建流水线失败", ex);
            FailRunningOperation(Step, ex.Message, total.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, $"Step 7 失败：{ex.Message}（已创建的端不受影响；修正后可直接重跑本步）");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 清理顺序「先删技能 / MCP、再注销工作区」不可颠倒，理由见
    /// <see cref="INewProjectService.AbortAndCleanupAsync"/> 的备注。
    /// </remarks>
    public async Task AbortAndCleanupAsync(CancellationToken cancellationToken = default)
    {
        // 档案可能在内存里（本进程刚建 / 刚重开面板已加载），故内存优先、盘上兜底
        var spec = Current ?? _store.Load();

        using var permissionScope = EnterPermissionScope();

        var manifest = ResolveManifest();
        var removedSkills = 0;
        var removedMcps = 0;

        // ── 1. 显式删除本插件写入的技能与 MCP 文件（PRD §3.5 卸载语义 / §4.7）──────────
        //     必须排在注销工作区之前：宿主删技能时优先按本进程记录的技能根目录定位，记录为空
        //     （重启后再清理）才退化到「当前工作区」——Step 2 刚把项目工作区置为当前，此刻定位最准；
        //     先注销工作区会切走当前工作区，反而让退化路径指向错误目录。
        if (manifest is not null)
        {
            foreach (var skill in manifest.Skills)
            {
                _host?.UnregisterSkill(skill.Id);
                removedSkills++;
            }

            foreach (var mcp in manifest.EnabledMcpServers)
            {
                _host?.UnregisterMcpServer(mcp.Name);
                removedMcps++;
            }
        }

        // ── 2. 注销本项目工作区（逻辑删；**不删** LocalPath 目录，PRD §4.7 / Step 2 陷阱②）──
        var unregistered = false;
        var workspaceId = spec?.WorkspaceId;
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            var workspaceManager = ResolveWorkspaceManager();
            if (workspaceManager is not null)
            {
                try
                {
                    unregistered = await workspaceManager.DeleteWorkspaceAsync(workspaceId);
                }
                catch (Exception ex)
                {
                    _host?.LogError("[newproject] 「中止并清理」注销工作区失败", ex);
                }
            }
        }

        // ── 3. 远端产物一律不自动删除，只列孤儿清单（PRD §4.7 / §6 #15）────────────────
        var orphans = spec?.RemoteArtifacts.Count ?? 0;

        // ── 4. 清空档案与操作记录（等价于「标记未完成」：不留误导性进度）────────────────
        Reset();

        var message = $"已中止并清理：注销工作区 {(unregistered ? 1 : 0)} 个 · 删除技能 {removedSkills} 项 / MCP {removedMcps} 项；"
                      + $"本地目录未被删除；远端产物孤儿 {orphans} 条未删除（需到云效控制台手动确认）。";
        Record(WizardStep.BasicInfo, "中止并清理", message, StepStatus.Succeeded, TimeSpan.Zero);
        _host?.Log($"[newproject] {message}");
    }

    /// <summary>
    /// 记一条操作记录（右侧时间线数据源，PRD §4.6）。
    /// </summary>
    /// <param name="step">步骤。</param>
    /// <param name="action">动作标题。</param>
    /// <param name="detail">参数 / 结果摘要（调用方负责脱敏）。</param>
    /// <param name="status">状态。</param>
    /// <param name="duration">耗时。</param>
    internal void Record(WizardStep step, string action, string detail, StepStatus status, TimeSpan duration)
    {
        lock (_gate)
        {
            _operations.Add(new OperationRecord
            {
                Step = step,
                Action = action,
                Detail = detail,
                Status = status,
                Duration = duration
            });
        }

        RaiseChanged();
    }

    /// <summary>
    /// 档案快照在工作区内的相对路径（PRD §4.3 Step 2 第 1 项）。
    /// </summary>
    internal static string SnapshotRelativePath => Path.Combine(".luban-agent", "plugin-newproject", "spec.json");

    /// <summary>
    /// 进入本插件的权限环境（面板事件回调等由宿主 UI 线程直接触发的代码不在框架包裹范围内）。
    /// </summary>
    /// <returns>退出令牌。</returns>
    private IDisposable EnterPermissionScope()
    {
        var host = _host ?? throw new InvalidOperationException("插件尚未绑定宿主，无法进入权限环境。");
        return PluginScopeContext.Enter(new PluginPermissionScope(host.PluginId, host.Permissions));
    }

    /// <summary>
    /// 按需解析当前用户的工作区管理器（宿主 scoped 服务的唯一合法通路，PRD §3.9）。
    /// </summary>
    /// <remarks>
    /// <c>IWorkspaceManager</c> / <c>ISessionManager</c> 在宿主里是 <b>scoped 转发</b>（按用户桶隔离），
    /// 从根容器直接解析在 <c>ValidateScopes=true</c> 下会抛；故经根单例 <c>UserContextRegistry</c>
    /// 取该用户的桶（与 codex 宿主 <c>PluginHostContext</c> 同一路径），用完即弃、不长期持有。
    /// </remarks>
    /// <returns>工作区管理器；宿主环境不完整时为 <c>null</c>。</returns>
    private IWorkspaceManager? ResolveWorkspaceManager() => ResolveUserBucket()?.WorkspaceManager;

    /// <summary>按需解析当前用户的会话管理器。</summary>
    /// <returns>会话管理器；宿主环境不完整时为 <c>null</c>。</returns>
    private ISessionManager? ResolveSessionManager() => ResolveUserBucket()?.SessionManager;

    /// <summary>取当前用户的宿主状态桶。</summary>
    /// <returns>用户桶；不可用时为 <c>null</c>。</returns>
    private UserBucket? ResolveUserBucket()
    {
        try
        {
            var registry = _host?.Services.GetService(typeof(UserContextRegistry)) as UserContextRegistry;
            return registry?.GetOrCreate(LocalUserDefaults.DefaultUserId);
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 解析宿主用户上下文失败（工作区 / 会话服务不可用）。", ex);
            return null;
        }
    }

    /// <summary>
    /// 读取本插件清单（技能 / MCP 声明的权威来源，避免在插件代码里复刻一份 <c>plugin.json</c>）。
    /// </summary>
    /// <returns>清单；不可读时为 <c>null</c>。</returns>
    private PluginManifest? ResolveManifest()
    {
        try
        {
            var pluginId = _host?.PluginId;
            if (string.IsNullOrWhiteSpace(pluginId))
                return null;

            var manager = _host!.Services.GetService(typeof(PluginManager)) as PluginManager;
            return manager?.GetPlugin(pluginId)?.Manifest;
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 读取插件清单失败。", ex);
            return null;
        }
    }

    /// <summary>
    /// 探测目录可写性（只读语义：探针文件写完即删，不留残留）。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <returns>是否可写 + 失败原因。</returns>
    private static (bool Writable, string Reason) ProbeWritable(string path)
    {
        try
        {
            if (File.Exists(path))
                return (false, "该路径是一个文件，请选择目录。");

            var target = path;
            if (!Directory.Exists(target))
            {
                // 目录尚不存在（Step 2 会创建）：退到最近的已存在祖先探测写权限
                while (!Directory.Exists(target))
                {
                    var parent = Path.GetDirectoryName(target);
                    if (string.IsNullOrEmpty(parent) || string.Equals(parent, target, StringComparison.OrdinalIgnoreCase))
                        break;

                    target = parent;
                }

                if (!Directory.Exists(target))
                    return (false, "路径不存在且无法定位上级目录。");
            }

            var probe = Path.Combine(target, $".newproject-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"不可写（{ex.Message}）。");
        }
    }

    /// <summary>
    /// 唯一性口径 ①：查该目录是否已被宿主登记为工作区（同路径必然派生同一 <c>WorkspaceId</c>）。
    /// </summary>
    /// <param name="localPath">本地目录。</param>
    /// <returns>已登记的工作区；未登记或宿主服务不可用时为 <c>null</c>。</returns>
    private async Task<WorkspaceInfo?> FindWorkspaceByPathAsync(string localPath)
    {
        var manager = ResolveWorkspaceManager();
        if (manager is null)
            return null;

        try
        {
            var id = WorkspaceIdGenerator.Compute(localPath);
            return string.IsNullOrEmpty(id) ? null : await manager.GetWorkspaceAsync(id);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[newproject] 检查工作区唯一性失败：{localPath}", ex);
            return null;
        }
    }

    /// <summary>
    /// 把 <see cref="ProjectSpec"/> 快照写入项目工作区（JSON 一律走源生成上下文，见 ALC 硬约束）。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="spec">项目档案。</param>
    /// <returns>快照文件绝对路径。</returns>
    private static string WriteSnapshot(string workspaceRoot, ProjectSpec spec)
    {
        var target = Path.Combine(workspaceRoot, SnapshotRelativePath);
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(target, JsonSerializer.Serialize(spec, PluginJsonContext.Default.ProjectSpec));
        return target;
    }

    /// <summary>
    /// 记一条「进行中」操作（PRD §4.3 Step 2：每条初始化动作实时呈现「进行中 → 成功/失败」）。
    /// </summary>
    /// <param name="step">步骤。</param>
    /// <param name="action">动作标题。</param>
    /// <param name="detail">参数摘要。</param>
    /// <returns>该动作的计时器（终态时取 <c>Elapsed</c> 作为耗时）。</returns>
    private Stopwatch BeginOperation(WizardStep step, string action, string detail)
    {
        Record(step, action, detail, StepStatus.Running, TimeSpan.Zero);
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// 把本步最近一条「进行中」记录改写为失败（异常路径：动作标题未知，不做同名匹配）。
    /// </summary>
    /// <param name="step">步骤。</param>
    /// <param name="detail">失败原因。</param>
    /// <param name="duration">耗时。</param>
    private void FailRunningOperation(WizardStep step, string detail, TimeSpan duration)
    {
        var updated = false;
        lock (_gate)
        {
            for (var i = _operations.Count - 1; i >= 0; i--)
            {
                if (_operations[i].Step != step || _operations[i].Status != StepStatus.Running)
                    continue;

                _operations[i].Detail = detail;
                _operations[i].Status = StepStatus.Failed;
                _operations[i].Duration = duration;
                updated = true;
                break;
            }
        }

        if (updated)
            RaiseChanged();
        else
            Record(step, "Step 2 失败", detail, StepStatus.Failed, duration);
    }

    /// <summary>
    /// 把最近一条同名「进行中」记录原地改写为终态（避免时间线出现成对重复条目）。
    /// </summary>
    /// <param name="step">步骤。</param>
    /// <param name="action">动作标题。</param>
    /// <param name="detail">结果摘要。</param>
    /// <param name="status">终态。</param>
    /// <param name="duration">耗时。</param>
    private void EndOperation(WizardStep step, string action, string detail, StepStatus status, TimeSpan duration = default)
    {
        var updated = TryMutateRunningOperation(step, action, operation =>
        {
            operation.Detail = detail;
            operation.Status = status;
            operation.Duration = duration;
        });

        if (updated)
            RaiseChanged();
        else
            Record(step, action, detail, status, duration);
    }

    /// <summary>
    /// 原地改写最近一条同名「进行中」记录的说明，**不改状态**（用于「还需等待 N 秒」这类进度文案）。
    /// </summary>
    /// <remarks>
    /// 退避重试期间调用（v0.29 Step 6 推送）：一次推送仍是一条动作记录，只把说明改成当前退避进度，
    /// 避免时间线里出现「推送到自有仓库 ×4」这种把重试摊成多条记录的噪声。
    /// </remarks>
    /// <param name="step">步骤。</param>
    /// <param name="action">动作标题。</param>
    /// <param name="detail">新的说明。</param>
    private void UpdateRunningOperation(WizardStep step, string action, string detail)
    {
        if (TryMutateRunningOperation(step, action, operation => operation.Detail = detail))
            RaiseChanged();
    }

    /// <summary>
    /// 找最近一条「同一步 + 同名 + 进行中」的记录并就地改写；找不到返回假。
    /// </summary>
    /// <param name="step">步骤。</param>
    /// <param name="action">动作标题（按序数比较，与 <see cref="EndOperation"/> 同口径）。</param>
    /// <param name="mutate">改写动作。</param>
    /// <returns>命中并改写返回 <c>true</c>。</returns>
    private bool TryMutateRunningOperation(WizardStep step, string action, Action<OperationRecord> mutate)
    {
        lock (_gate)
        {
            for (var i = _operations.Count - 1; i >= 0; i--)
            {
                var operation = _operations[i];
                if (operation.Step != step || operation.Status != StepStatus.Running)
                    continue;

                if (!string.Equals(operation.Action, action, StringComparison.Ordinal))
                    continue;

                mutate(operation);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 保存档案并通知面板刷新（Step 3 / 6 的中途落库都走这里，保证「失败也不丢账」）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    private void SaveSpec(ProjectSpec spec)
    {
        SnapshotStepStates(spec);
        _store.Save(spec);
        RaiseChanged();
    }

    /// <summary>
    /// 把本次运行的步骤终态写回档案（唯一落库出口 <see cref="SaveSpec" /> 的一部分）。
    /// </summary>
    /// <remarks>
    /// 操作记录只在内存（重启即空），步骤条要跨重启保住状态就只能落档案；此处按发生顺序回放本次记录，
    /// 每一步「最后一次出现的终态」即该步快照（进行中不落，属易失状态）。失败步骤若要「重跑后转成功」，
    /// 由后一条成功记录覆盖，与时间线语义一致。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    private void SnapshotStepStates(ProjectSpec spec)
    {
        OperationRecord[] snapshot;
        lock (_gate)
        {
            snapshot = _operations.ToArray();
        }

        foreach (var record in snapshot)
        {
            if (record.Status is not (StepStatus.Succeeded or StepStatus.Failed or StepStatus.Skipped))
                continue;

            spec.StepStates[record.Step.ToString()] = record.Status.ToString();
        }
    }

    /// <summary>
    /// 作废上一轮的编译（Step 5）结论：Step 3 重跑成功后调用（本地代码可能已变）。
    /// </summary>
    /// <remarks>
    /// 档案侧交给 <see cref="StepGate.InvalidateBuild" />（清逐端结论 + 终态快照的 <c>Build</c> 键），
    /// 但快照还会在下一次 <see cref="SaveSpec" /> 的 <see cref="SnapshotStepStates" /> 里按<b>内存操作记录</b>
    /// 回放重写——本进程里遗留的 Build 记录会把刚清掉的键「复活」。故一并清掉那些记录：结论既已作废，
    /// 留在时间线上同样是误导。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    private void InvalidateBuildConclusion(ProjectSpec spec)
    {
        StepGate.InvalidateBuild(spec);

        lock (_gate)
        {
            _operations.RemoveAll(record => record.Step == WizardStep.Build);
        }
    }

    /// <summary>
    /// 作废档案里的流水线（Step 7）结论：Step 3（建库并克隆）重跑成功后调用。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="InvalidateBuildConclusion"/> 同源理由：流水线 YAML 里写死了代码源地址与分支，
    /// 仓库一旦重建 / 改名，旧结论就是误导（面板会继续回显一条指向旧仓库的流水线链接）。
    /// 本方法<b>只清结论</b>，不动云效上的流水线本体——远端产物一律不自动删除（PRD §4.5）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    private void InvalidatePipelineConclusion(ProjectSpec spec)
    {
        spec.PipelineResults.Clear();
        spec.StepStates.Remove(nameof(WizardStep.Pipeline));
        spec.RemoteArtifacts.RemoveAll(artifact => artifact.Kind == RemoteArtifactKind.Pipeline);

        lock (_gate)
        {
            _operations.RemoveAll(record => record.Step == WizardStep.Pipeline);
        }
    }

    /// <summary>
    /// 从宿主根容器按需解析服务（解析失败只告警不抛，交由调用方给出可操作引导文案）。
    /// </summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <returns>服务实例；未注册或解析失败时为 <c>null</c>。</returns>
    private T? Resolve<T>()
        where T : class
        => ResolveByType(typeof(T)) as T;

    /// <summary>
    /// 按 <see cref="Type"/> 从宿主根容器按需解析服务（供桥接层等只持有 <c>Func&lt;Type, object?&gt;</c> 的场景复用同一条通路）。
    /// </summary>
    /// <param name="serviceType">服务类型。</param>
    /// <returns>服务实例；未注册或解析失败时为 <c>null</c>。</returns>
    private object? ResolveByType(Type serviceType)
    {
        try
        {
            return _host?.Services.GetService(serviceType);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[newproject] 解析宿主服务 {serviceType.Name} 失败。", ex);
            return null;
        }
    }

    /// <summary>
    /// 本次要执行建库 / 推送的端（档案里的 <see cref="ProjectSpec.Repositories"/> ∩ Step 1 所选端）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <returns>按端仓库清单。</returns>
    private static List<EndRepository> PlanRepositories(ProjectSpec spec)
        => spec.Repositories
            .Where(repository => spec.Ends.Count == 0 || spec.Ends.Contains(repository.End))
            .ToList();

    /// <summary>
    /// 目标项目代码组名：由插件配置 <c>groupNameFormat</c> 按项目缩写 / 名称展开
    /// （默认 <c>{CODE}</c>，如 <c>jnms</c> → <c>JNMS</c>）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <returns>组名（展开结果为空时为空串）。</returns>
    private string BuildGroupName(ProjectSpec spec)
        => (_settings.BuildGroupName(spec.Code, spec.Name) ?? string.Empty).Trim();

    /// <summary>
    /// 在组织内按名精确查找目标代码组（<b>只读</b>）。
    /// </summary>
    /// <remarks>
    /// 云效 <c>namespaces.search</c> 是「组名包含」语义，故命中后还要做一次精确名比较；
    /// 未登记父级（组织根）时可能存在嵌套同名组，此时取层级最浅的一个，避免误复用深层同名组。
    /// </remarks>
    /// <param name="codeup">云效客户端。</param>
    /// <param name="groupName">组名（精确匹配，忽略大小写）。</param>
    /// <param name="parentId">父级组 ID（<c>null</c>＝组织根）。</param>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中的代码组；未命中为 <c>null</c>。</returns>
    private static async Task<YunxiaoNamespace?> FindGroupByNameAsync(
        CodeupClient codeup,
        string groupName,
        long? parentId,
        string organizationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return null;

        var list = await codeup.ListNamespacesAsync(
            search: groupName,
            parentId: parentId,
            page: 1,
            perPage: 100,
            organizationId: organizationId,
            cancellationToken: cancellationToken);

        var candidates = (list ?? new List<YunxiaoNamespace>())
            .Where(item => string.IsNullOrWhiteSpace(item.Kind)
                           || string.Equals(item.Kind, "group", StringComparison.OrdinalIgnoreCase))
            .Where(item => string.Equals(item.Name, groupName, StringComparison.OrdinalIgnoreCase));

        if (parentId.HasValue)
            candidates = candidates.Where(item => item.ParentId == parentId);

        return candidates
            .OrderBy(item => (item.PathWithNamespace ?? item.Path ?? string.Empty).Count(ch => ch == '/'))
            .FirstOrDefault();
    }

    /// <summary>
    /// 确保目标代码组存在（先查后建，PRD §4.3 Step 3）：命中即复用，未命中才创建。
    /// </summary>
    /// <param name="codeup">云效客户端。</param>
    /// <param name="groupName">组名。</param>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可用的代码组（新建或复用）。</returns>
    /// <exception cref="InvalidOperationException">组名为空，或云效未返回新建组 id。</exception>
    private async Task<YunxiaoNamespace> EnsureGroupAsync(
        CodeupClient codeup,
        string groupName,
        string organizationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new InvalidOperationException("项目代码组名为空：请检查插件配置 groupNameFormat。");

        var parentId = _settings.TryGetGroupParentId();
        var existing = await FindGroupByNameAsync(codeup, groupName, parentId, organizationId, cancellationToken);
        if (existing is not null)
            return existing;

        var created = await codeup.CreateNamespaceAsync(
            new YunxiaoCreateNamespaceRequest
            {
                Name = groupName,
                Path = groupName,
                ParentId = parentId,
                Visibility = NormalizeVisibility(_settings.DefaultVisibility),
                Description = $"由「新建项目」插件自动创建的项目代码组（{groupName}）"
            },
            organizationId,
            cancellationToken);

        if (created is null || created.Id <= 0)
            throw new InvalidOperationException($"云效未返回新建代码组 id（{groupName}）。");

        return created;
    }

    /// <summary>
    /// 在云效<b>模板库</b>（<c>templateType=1</c>）中定位某端的模板仓（PRD §6 #5）。
    /// </summary>
    /// <remarks>
    /// 优先用配置登记的 <c>templates[].projectId</c>（整数）精确匹配；未登记则按模板仓名匹配。
    /// 走模板库列表而不是按名搜普通仓库：只有登记为模板的仓库才能用作 <c>templateProjectId</c>。
    /// </remarks>
    /// <param name="codeup">云效客户端。</param>
    /// <param name="repository">按端仓库信息。</param>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中的模板仓；失败时给出可读原因（不抛异常）。</returns>
    private async Task<(YunxiaoTemplateRepository? Template, string? Error)> LocateTemplateAsync(
        CodeupClient codeup,
        EndRepository repository,
        string organizationId,
        CancellationToken cancellationToken)
    {
        var binding = _settings.FindTemplate(repository.End);
        var wanted = (repository.TemplateRepository ?? string.Empty).Trim();
        if (wanted.Length == 0)
            wanted = (binding?.Repository ?? string.Empty).Trim();

        var configuredId = long.TryParse(binding?.ProjectId?.Trim(), out var parsedId) && parsedId > 0
            ? parsedId
            : (long?)null;

        List<YunxiaoTemplateRepository> templates;
        try
        {
            templates = await codeup.ListTemplateRepositoriesAsync(
                templateType: 1,
                page: 1,
                perPage: 100,
                organizationId: organizationId,
                cancellationToken: cancellationToken)
                ?? new List<YunxiaoTemplateRepository>();
        }
        catch (Exception ex)
        {
            return (null, $"读取云效模板库失败：{ex.Message}");
        }

        if (templates.Count == 0)
            return (null, "云效自定义模板库为空（templateType=1）：请先在云效把模板仓登记为模板（PRD §6 #5）。");

        if (configuredId.HasValue)
        {
            var byId = templates.FirstOrDefault(item => item.ProjectId == configuredId.Value);
            return byId is not null
                ? (byId, null)
                : (null, $"配置登记的模板仓 ID {configuredId} 不在云效模板库中。"
                         + $"模板库现有：{string.Join(" / ", templates.Select(TemplateLabel))}");
        }

        var byName = templates.FirstOrDefault(item =>
            string.Equals(item.Name, wanted, StringComparison.OrdinalIgnoreCase));
        return byName is not null
            ? (byName, null)
            : (null, $"模板库中没有名为「{wanted}」的模板仓。"
                     + $"模板库现有：{string.Join(" / ", templates.Select(TemplateLabel))}");
    }

    /// <summary>
    /// 解析某端的模板 pin 分支（PRD §6 #5）。
    /// </summary>
    /// <remarks>
    /// 配置 <c>templates[].branch</c> 登记了值则以登记值为准（不存在即报错，不静默回退）；
    /// 留空则运行期实时取模板仓分支，按 <c>develop → master → main → 模板默认分支</c> 顺序择一。
    /// </remarks>
    /// <param name="codeup">云效客户端。</param>
    /// <param name="templateId">模板仓 ID。</param>
    /// <param name="end">所属端。</param>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>pin 分支、来源说明、模板仓分支清单、失败原因。</returns>
    private async Task<(string? Branch, string Source, List<string> Branches, string? Error)> ResolvePinAsync(
        CodeupClient codeup,
        long templateId,
        ProjectEnd end,
        string organizationId,
        CancellationToken cancellationToken)
    {
        var empty = new List<string>();

        List<YunxiaoBranch> branches;
        try
        {
            branches = await codeup.ListBranchesAsync(
                templateId,
                page: 1,
                perPage: 100,
                organizationId: organizationId,
                cancellationToken: cancellationToken)
                ?? new List<YunxiaoBranch>();
        }
        catch (Exception ex)
        {
            return (null, string.Empty, empty, $"读取模板仓分支失败：{ex.Message}");
        }

        var names = branches
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        if (names.Count == 0)
            return (null, string.Empty, names, "模板仓没有任何分支（模板全量同步可能尚未完成）。");

        var configured = _settings.FindTemplate(end)?.Branch?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return names.Contains(configured, StringComparer.Ordinal)
                ? (configured, "配置登记", names, null)
                : (null, string.Empty, names,
                    $"配置登记的 pin「{configured}」在模板仓中不存在（模板仓现有：{string.Join(" / ", names)}）。");
        }

        foreach (var candidate in PreferredPins)
        {
            if (names.Contains(candidate, StringComparer.Ordinal))
                return (candidate, $"按开发约定取模板 {candidate}", names, null);
        }

        var fallback = branches.FirstOrDefault(item => item.DefaultBranch)?.Name;
        if (!string.IsNullOrWhiteSpace(fallback))
            return (fallback, $"取模板默认分支 {fallback}", names, null);

        return (null, string.Empty, names,
            "模板仓既没有 develop / master / main，也未标记默认分支："
            + $"请在插件配置 templates[].branch 显式登记 pin（模板仓现有：{string.Join(" / ", names)}）。");
    }

    /// <summary>模板库条目的展示名（名称缺失时退回路径 / id）。</summary>
    /// <param name="template">模板库条目。</param>
    /// <returns>展示名。</returns>
    private static string TemplateLabel(YunxiaoTemplateRepository template)
        => string.IsNullOrWhiteSpace(template.Name)
            ? (string.IsNullOrWhiteSpace(template.Path) ? $"#{template.ProjectId}" : template.Path!)
            : template.Name!;

    /// <summary>
    /// 登记项目级远端产物「代码组」（重跑幂等：同名只更新地址，不重复入册）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="group">云效代码组。</param>
    /// <returns>新登记的产物；已存在时为 <c>null</c>。</returns>
    private static RemoteArtifact? AddGroupArtifact(ProjectSpec spec, YunxiaoNamespace group)
    {
        var name = string.IsNullOrWhiteSpace(group.PathWithNamespace)
            ? (string.IsNullOrWhiteSpace(group.Name) ? group.Id.ToString(CultureInfo.InvariantCulture) : group.Name!)
            : group.PathWithNamespace!;

        var webUrl = group.WebUrl ?? string.Empty;
        var externalId = group.Id.ToString(CultureInfo.InvariantCulture);

        var existing = spec.RemoteArtifacts.FirstOrDefault(artifact =>
            artifact.Kind == RemoteArtifactKind.Group
            && string.Equals(artifact.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.WebUrl = webUrl;
            existing.ExternalId = externalId;
            return null;
        }

        var created = new RemoteArtifact
        {
            Kind = RemoteArtifactKind.Group,
            End = null,
            Name = name,
            WebUrl = webUrl,
            ExternalId = externalId
        };

        spec.RemoteArtifacts.Add(created);
        return created;
    }

    /// <summary>
    /// 按仓库名精确检索云效仓库（幂等①）：列表接口不含克隆地址，只用于「是否存在 + 取 id」。
    /// </summary>
    /// <param name="codeup">云效客户端。</param>
    /// <param name="name">仓库名（精确匹配，忽略大小写）。</param>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中的仓库；未命中为 <c>null</c>。</returns>
    private static async Task<YunxiaoRepository?> FindRepositoryByNameAsync(
        CodeupClient codeup,
        string name,
        string organizationId,
        CancellationToken cancellationToken)
    {
        var page = await codeup.SearchRepositoriesAsync(
            name,
            page: 1,
            perPage: 100,
            organizationId: organizationId,
            cancellationToken: cancellationToken);

        return page?.Items?.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 读取云效 PAT（只用于「是否已配置」的前置判定；真正的出口门禁在连接器内）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>PAT；未配置或读取失败时为 <c>null</c>。</returns>
    private async Task<string?> TryGetPatAsync(CancellationToken cancellationToken)
    {
        var provider = Resolve<IYunxiaoCredentialProvider>();
        if (provider is null)
            return null;

        try
        {
            return await provider.GetPatAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 读取云效 PAT 失败。", ex);
            return null;
        }
    }

    /// <summary>
    /// 就「目标目录非空」征询用户三选一（PRD §4.3 Step 3）。
    /// </summary>
    /// <param name="repository">按端仓库信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户选择；无交互通道（聊天入口 / 面板未创建）时为 <c>null</c>（调用方中止本步）。</returns>
    private async Task<CloneConflictChoice?> AskCloneConflictAsync(EndRepository repository, CancellationToken cancellationToken)
    {
        var resolver = CloneConflictResolver;
        if (resolver is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        var conflict = new CloneConflict
        {
            End = repository.End,
            RepositoryName = repository.RepositoryName,
            LocalPath = repository.LocalPath,
            EntryCount = CountEntries(repository.LocalPath)
        };

        try
        {
            return await resolver(conflict);
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 克隆冲突决策回调异常（按「中止」处理）。", ex);
            return CloneConflictChoice.Abort;
        }
    }

    /// <summary>目录内顶层条目数（仅用于冲突提示文案）。</summary>
    /// <param name="path">目录。</param>
    /// <returns>条目数；不可读时为 0。</returns>
    private static int CountEntries(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateFileSystemEntries(path).Count() : 0;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[newproject] 统计目录条目失败：{path}", ex);
            return 0;
        }
    }

    /// <summary>
    /// 等待「建库后模板同步」把 pin 分支真的推到新仓（RC1：建库返回 ≠ 分支就绪）。
    /// </summary>
    /// <param name="codeup">云效连接器。</param>
    /// <param name="repositoryId">云效仓库 id（本步新建或同名复用的那条）。</param>
    /// <param name="branch">pin 分支名（精确匹配，区分大小写）。</param>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在 <see cref="BranchReadyTimeout"/> 内命中分支返回 <c>true</c>，超时返回 <c>false</c>。</returns>
    /// <remarks>
    /// 建库接口返回只代表仓库已登记，模板分支由服务端异步同步；此时立刻 <c>git clone -b</c> 必然失败，
    /// 且 git 会在目标目录留下 <c>.git/HEAD = ref: refs/heads/.invalid</c> 的残骸。故克隆前先轮询到位。
    /// </remarks>
    private static async Task<bool> WaitForBranchReadyAsync(
        CodeupClient codeup,
        long repositoryId,
        string branch,
        string organizationId,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < BranchReadyTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var branches = await codeup.ListBranchesAsync(
                repositoryId,
                page: 1,
                perPage: 100,
                organizationId: organizationId,
                cancellationToken: cancellationToken);

            if (branches is not null
                && branches.Any(item => string.Equals(item.Name, branch, StringComparison.Ordinal)))
                return true;

            await Task.Delay(BranchReadyPollInterval, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// 克隆失败时清理本次新建的残留目录（RC3：失败留下的坏 <c>.git</c> 会在重跑时被误当成「已克隆」）。
    /// </summary>
    /// <param name="path">目标目录。</param>
    /// <param name="dirPreExisted">克隆前该目录是否已存在（已存在则不动，交由三选一处置，不越权删除）。</param>
    /// <returns>是否执行了清理。</returns>
    /// <remarks>
    /// 删除走 <see cref="DirectoryCleaner.DeleteForce"/>（RC5）：<c>git</c> 的 pack 文件是<b>只读</b>的，
    /// 裸 <c>Directory.Delete</c> 必抛 <c>UnauthorizedAccessException</c>，且抛错前已删掉 HEAD / config / refs，
    /// 只会把残留越搞越烂。
    /// </remarks>
    private static bool TryCleanPartialClone(string path, bool dirPreExisted)
    {
        if (dirPreExisted)
            return false;

        try
        {
            if (Directory.Exists(path))
                DirectoryCleaner.DeleteForce(path);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[newproject] 清理克隆残留目录失败：{path}", ex);
            return false;
        }
    }

    /// <summary>
    /// Step 6 的推送目标分支：配置 <c>pushBranch</c> 优先（v0.28 起包内默认 <c>develop</c>，PRD §6 #17 改口径）；
    /// 配置留空才退回该端仓库的默认分支，仍为空则 <c>main</c>。
    /// </summary>
    /// <param name="repository">按端仓库信息。</param>
    /// <returns>目标分支名。</returns>
    private string ResolvePushBranch(EndRepository repository)
        => !string.IsNullOrWhiteSpace(_settings.PushBranch)
            ? _settings.PushBranch
            : string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch!;

    /// <summary>由 HTTPS 克隆地址推导仓库 Web 地址（去掉尾部 <c>.git</c>）。</summary>
    /// <param name="cloneUrl">克隆地址。</param>
    /// <returns>Web 地址。</returns>
    private static string BuildWebUrl(string cloneUrl)
        => cloneUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? cloneUrl[..^4] : cloneUrl;

    /// <summary>云效流水线控制台链接（供用户核对 / 人工绑定 / 手动删除）。</summary>
    /// <param name="pipelineId">流水线 ID。</param>
    /// <returns>控制台链接；id 为空时为空串。</returns>
    private static string BuildPipelineWebUrl(string? pipelineId)
        => string.IsNullOrWhiteSpace(pipelineId) ? string.Empty : $"https://flow.aliyun.com/pipelines/{pipelineId}/detail";

    /// <summary>
    /// 登记远端产物（重跑幂等：同端同类别同名只更新地址，不重复入册）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="repository">按端仓库信息。</param>
    /// <param name="kind">产物类别。</param>
    /// <param name="branch">分支产物的分支名（Step 6 实际推送的分支，见 <see cref="ResolvePushBranch"/>）。</param>
    /// <param name="name">
    /// 产物名覆盖（仅流水线用：名字是「流水线名」而不是分支名）；为空则按 <paramref name="kind"/> 推导。
    /// </param>
    /// <param name="externalId">云效侧标识覆盖（仅流水线用：流水线 id 与仓库 id 不同源）。</param>
    /// <param name="webUrl">控制台链接覆盖（仅流水线用：<c>flow.aliyun.com</c> 与代码库链接不同域名）。</param>
    /// <returns>新登记的产物；已存在时为 <c>null</c>。</returns>
    private static RemoteArtifact? AddArtifact(
        ProjectSpec spec,
        EndRepository repository,
        RemoteArtifactKind kind,
        string? branch = null,
        string? name = null,
        string? externalId = null,
        string? webUrl = null)
    {
        branch = string.IsNullOrWhiteSpace(branch)
            ? string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch
            : branch;
        name = string.IsNullOrWhiteSpace(name)
            ? kind == RemoteArtifactKind.Repository ? repository.RepositoryName : branch
            : name.Trim();
        webUrl = string.IsNullOrWhiteSpace(webUrl)
            ? kind == RemoteArtifactKind.Repository
                ? repository.WebUrl
                : string.IsNullOrWhiteSpace(repository.WebUrl) ? string.Empty : $"{repository.WebUrl}/tree/{branch}"
            : webUrl;
        externalId ??= repository.ExternalId;

        var existing = spec.RemoteArtifacts.FirstOrDefault(artifact =>
            artifact.Kind == kind
            && artifact.End == repository.End
            && string.Equals(artifact.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.WebUrl = webUrl;
            existing.ExternalId = externalId;
            return null;
        }

        var created = new RemoteArtifact
        {
            Kind = kind,
            End = repository.End,
            Name = name,
            WebUrl = webUrl,
            ExternalId = externalId
        };

        spec.RemoteArtifacts.Add(created);
        return created;
    }

    /// <summary>由工作区根路径 + 仓库名推导本地目录（档案路径为空时返回空串）。</summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="end">所属端。</param>
    /// <param name="repositoryName">仓库名。</param>
    /// <returns>本地目录绝对路径。</returns>
    private string ResolveRepositoryLocalPath(ProjectSpec spec, ProjectEnd end, string repositoryName)
    {
        if (string.IsNullOrWhiteSpace(spec.LocalPath))
            return string.Empty;

        var name = string.IsNullOrWhiteSpace(repositoryName)
            ? _settings.BuildRepositoryName(spec.Code, end)
            : repositoryName.Trim();

        return Path.Combine(spec.LocalPath, name);
    }

    /// <summary>端的中文标签（**已自带「端」字**：调用方不得再拼一个「端」）。</summary>
    /// <param name="end">端。</param>
    /// <returns>标签。</returns>
    /// <remarks>
    /// v0.29：此前多处写作 <c>$"{EndLabel(end)}端"</c>，产出「后端端」「管理端端」「移动端端」，
    /// 并被拼进 Step 6 的提交信息（真机提交 <c>chore: 初始化 测试项目一（管理端端）——…</c>）。
    /// 一律直接用本方法的返回值。
    /// </remarks>
    private static string EndLabel(ProjectEnd end) => end switch
    {
        ProjectEnd.Service => "后端",
        ProjectEnd.Admin => "管理端",
        ProjectEnd.Mobile => "移动端",
        _ => end.ToString()
    };

    /// <summary>可见性的中文标签。</summary>
    /// <param name="visibility">可见性。</param>
    /// <returns>标签。</returns>
    private static string VisibilityLabel(string? visibility)
        => string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase) ? "私有" : "组织内公开";

    /// <summary>是否为受支持的可见性取值（PRD §4.3 Step 3 确认区只提供这两档）。</summary>
    /// <param name="visibility">可见性。</param>
    /// <returns>是否受支持。</returns>
    private static bool IsSupportedVisibility(string? visibility)
        => string.Equals(visibility, "internal", StringComparison.OrdinalIgnoreCase)
           || string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase);

    /// <summary>归一化可见性取值（非法值一律回落到组织内公开）。</summary>
    /// <param name="visibility">可见性。</param>
    /// <returns>归一化结果。</returns>
    private static string NormalizeVisibility(string? visibility)
        => string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase) ? "private" : "internal";

    /// <summary>
    /// 执行本机 git 命令，并在**唯一出口**处过权限门禁（PRD §3.7）：插件自行拉起的 git 进程
    /// 不得借连接器之名绕过检查。操作名取**子命令**（<c>clone</c> / <c>push</c> / <c>add</c> /
    /// <c>commit</c> / <c>rev-parse</c> …），会跳过 <c>-c</c> / <c>-C</c> / <c>--git-dir</c> 等前置全局选项；
    /// 由宿主按 git 能力组判定
    /// （见框架 <c>PluginSecurityOptions.GitWhitelist</c>：<c>clone_push</c> / <c>commit</c> / <c>inspect</c>）。
    /// </summary>
    /// <param name="runner">进程执行器。</param>
    /// <param name="workingDirectory">仓库目录。</param>
    /// <param name="arguments">参数列表（不经 shell，避免引号转义问题）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果。</returns>
    private Task<ProcessResult> RunGitAsync(
        ProcessRunner runner,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var operation = ResolveGitOperation(arguments);
        var guard = Resolve<PluginPermissionGuard>() ?? PluginPermissionGuard.Default;
        guard.RequireGit(operation, $"NewProjectService.RunGitAsync({operation})");
        guard.RequireProcess("git", $"NewProjectService.RunGitAsync({operation})");

        return runner.RunAsync(
            "git",
            string.Empty,
            workingDirectory,
            stdin: null,
            timeoutMs: 120_000,
            cancellationToken: cancellationToken,
            argumentList: arguments,
            outputEncoding: Encoding.UTF8);
    }

    /// <summary>
    /// 从 git 参数列表里取出**子命令名**作为权限操作名：跳过 <c>-c name=value</c> / <c>-C path</c> /
    /// <c>--git-dir=</c> 这类前置全局选项（含其取值），取第一个非选项参数。
    /// </summary>
    /// <param name="arguments">git 参数列表。</param>
    /// <returns>子命令名（取不到时回落 <c>git</c>，由宿主判为越界）。</returns>
    private static string ResolveGitOperation(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (string.IsNullOrWhiteSpace(argument))
                continue;

            if (!argument.StartsWith('-'))
                return argument.Trim();

            // --opt=value 自带取值，无需再跳一项
            if (argument.Contains('='))
                continue;

            // -c <name=value> / -C <path> / --git-dir <path> / --work-tree <path> / --namespace <name>
            if (GitGlobalOptionsWithValue.Contains(argument))
                i++;
        }

        return "git";
    }

    /// <summary>自带取值的 git 前置全局选项（用于跳过其值，避免把取值当成子命令）。</summary>
    private static readonly HashSet<string> GitGlobalOptionsWithValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "-c",
        "-C",
        "--git-dir",
        "--work-tree",
        "--namespace",
        "--config-env",
        "--super-prefix"
    };

    /// <summary>
    /// 校验本地目录是否为可复用的 Git 工作树（RC4）：HEAD 可解析，且给定分支在本地 refs 中存在。
    /// </summary>
    /// <param name="runner">进程执行器。</param>
    /// <param name="repositoryPath">本地仓库目录。</param>
    /// <param name="branch">要求存在的本地分支（可为空，只校验 HEAD）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可复用返回 <c>null</c>；否则返回可展示的失败原因。</returns>
    /// <remarks>
    /// git 克隆中途失败会留下 <c>.git/HEAD = ref: refs/heads/.invalid</c> 的残骸，
    /// 仅凭「目录非空」无法识别，必须让 git 自己判断。
    /// </remarks>
    private async Task<string?> VerifyWorktreeAsync(
        ProcessRunner runner,
        string repositoryPath,
        string? branch,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repositoryPath))
            return $"本地目录不存在：{repositoryPath}";

        // ① HEAD 必须可解析（--verify --quiet：不存在时不输出，仅以非 0 退出码表示）
        var head = await RunGitAsync(
            runner, repositoryPath, new[] { "rev-parse", "--verify", "--quiet", "HEAD" }, cancellationToken);
        if (head.ExitCode != 0)
            return $"HEAD 无法解析（{Summarize(head)}）";

        // ② 要求的分支必须真实存在
        if (!string.IsNullOrWhiteSpace(branch))
        {
            var exists = await RunGitAsync(
                runner, repositoryPath, new[] { "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}" }, cancellationToken);
            if (exists.ExitCode != 0)
                return $"缺少分支 {branch}（{Summarize(exists)}）";
        }

        return null;
    }

    /// <summary>把 git 执行结果压成一行可展示的失败摘要（优先 stderr）。</summary>
    /// <param name="result">执行结果。</param>
    /// <returns>摘要。</returns>
    private static string Summarize(ProcessResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        var line = (text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .FirstOrDefault(item => item.Length > 0);

        if (string.IsNullOrEmpty(line))
            return $"git 退出码 {result.ExitCode}";

        return line.Length > 200 ? line[..200] + "…" : line;
    }

    /// <summary>
    /// 解析提交身份（F5=A：优先本机 <c>git config</c>，缺失时用插件配置兜底）。
    /// </summary>
    /// <param name="runner">进程执行器。</param>
    /// <param name="repositoryPath">仓库目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提交者姓名与邮箱。</returns>
    private async Task<(string Name, string Email)> ResolveCommitAuthorAsync(
        ProcessRunner runner,
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var name = await TryGetGitConfigAsync(runner, repositoryPath, "user.name", cancellationToken);
        var email = await TryGetGitConfigAsync(runner, repositoryPath, "user.email", cancellationToken);

        return (
            string.IsNullOrWhiteSpace(name) ? _settings.CommitAuthorName : name!,
            string.IsNullOrWhiteSpace(email) ? _settings.CommitAuthorEmail : email!);
    }

    /// <summary>读一条本机 git 配置（全局或仓库级，<c>git config --get</c> 已含继承语义）。</summary>
    /// <param name="runner">进程执行器。</param>
    /// <param name="repositoryPath">仓库目录。</param>
    /// <param name="key">配置键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>配置值；缺失或读取失败时为 <c>null</c>。</returns>
    private async Task<string?> TryGetGitConfigAsync(
        ProcessRunner runner,
        string repositoryPath,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunGitAsync(runner, repositoryPath, new[] { "config", "--get", key }, cancellationToken);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[newproject] 读取本机 git 配置 {key} 失败（改用插件配置兜底）。", ex);
            return null;
        }
    }

    /// <summary>
    /// 触发变更通知（面板据此刷新）。
    /// </summary>
    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 面板刷新回调异常（已忽略）。", ex);
        }
    }
}