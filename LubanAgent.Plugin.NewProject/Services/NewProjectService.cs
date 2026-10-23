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
/// Step 4 / 5 已落地为「助手亲自执行」；Step 7（v0.32：内嵌新版 YAML 模板渲染 + 创建前选定部署目标、创建即写部署段 + 覆盖更新）见
/// <see cref="CreatePipelinesAsync"/> / <see cref="UpdatePipelinesAsync"/>。
/// </para>
/// </remarks>
public sealed class NewProjectService : INewProjectService
{
    /// <summary>英文缩写命名规范（PRD §4.3：<b>只允许小写字母 a-z</b>，不允许数字 / 大写 / 符号）。</summary>
    private static readonly Regex CodePattern = new("^[a-z]+$", RegexOptions.Compiled);

    /// <summary>云效仓库名规范（字母 / 数字 / 下划线 / 中划线 / 点，且以字母或数字开头）。</summary>
    private static readonly Regex RepositoryNamePattern = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    /// <summary>
    /// Step 3 执行续跑的最大轮数（§5.0 / §6.1.3）：<c>needsInput</c> 时把用户答复拼进下一轮同会话续跑；
    /// 连续 <c>needsInput</c> 超过本上限即判失败，防止无限往返。
    /// </summary>
    private const int MaxRepositoryRounds = 3;

    /// <summary>
    /// 操作记录在内存中保留的最近条数上限：面板刷新成本与记录条数成正比，
    /// 长跑场景不限量会让时间线重建越来越慢、分配越来越大。
    /// </summary>
    private const int MaxOperationRecords = 500;

    private readonly List<OperationRecord> _operations = new();
    private readonly NewProjectStateStore _store = new();
    private readonly object _gate = new();

    private IPluginHost? _host;
    private NewProjectSettings _settings = new();

    /// <summary>
    /// 最近一次 Step 7「准备（只读）」的读数（v0.3）：供同步的 <see cref="PreviewPipelines"/> 应用手工微调后回显。
    /// </summary>
    /// <remarks>随读随用、不落档案（§5.3）；<c>null</c>＝本次进程尚未跑过准备，面板应先调 <see cref="PreparePipelinesAsync"/>。</remarks>
    private PipelinePrepareResult? _pipelinePrepareResult;

    /// <summary>
    /// 最近一次 Step 3「准备（只读）」结论文件路径（执行续跑的<b>事实来源</b>，§5.2）。
    /// </summary>
    /// <remarks>
    /// <b>为什么记住路径而不是取最新一份</b>：准备与执行共用 <c>results/step3-*.json</c> 命名（设计 §4.3 第 78 行），
    /// 执行成功后再回取「最新一份」拿到的会是<b>执行</b>结论；把执行结论当准备结论下发会跑偏事实来源。
    /// 本字段跨调用有效；进程重启后为空，才回退到「取最新一份」（尽力而为）。
    /// </remarks>
    private string? _repositoryPrepareResultFile;

    /// <summary>最近一次 Step 7「准备（只读）」结论文件路径；语义同 <see cref="_repositoryPrepareResultFile"/>。</summary>
    private string? _pipelinePrepareResultFile;

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

    /// <summary>
    /// 确保档案里的云效组织 ID 有值：旧档案 / 新进程里为空时按当前云效连接配置回填。
    /// </summary>
    /// <remarks>
    /// 组织 ID 属<b>插件侧配置</b>，不该让助手从知识文档里猜；回填后由提示词原样下发，
    /// 助手每次调用 <c>mcp_yunxiao_*</c> 都显式传这个值（空串会绕过客户端回落配置的兜底）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    /// <returns><c>true</c>＝已有值；<c>false</c>＝连接配置里也没有。</returns>
    private bool EnsureOrganizationId(ProjectSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.OrganizationId))
            spec.OrganizationId = OrganizationHint;
        return !string.IsNullOrWhiteSpace(spec.OrganizationId);
    }

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
            errors.Add("英文缩写：只能由小写字母组成（不能有数字、大写字母、空格、中文或符号）。");
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

        // 各端目标仓库（仓库名默认 {Code}-Service/-Admin/-Mobile，如 Csxmy-Service；本地目录同源，Step 3 可改）。
        // 模板仓对照不再由插件配置提供：v0.33 起迁到 docs/org-reference.md，由助手在 Step 3 只读准备阶段按那里的
        // 按端名称约定定位模板仓，回填进 Step 3 预检产物（见 RepositoryPrecheck.TemplateRepository）。
        foreach (var end in selected)
        {
            spec.Repositories.Add(new EndRepository
            {
                End = end,
                TemplateRepository = string.Empty,
                TemplateBranch = string.Empty,
                RepositoryName = _settings.BuildRepositoryName(spec.Code, end),
                Visibility = _settings.DefaultVisibility,
                DefaultBranch = _settings.PushBranch,
                LocalPath = string.IsNullOrWhiteSpace(spec.LocalPath)
                    ? string.Empty
                    : Path.Combine(spec.LocalPath, _settings.BuildRepositoryName(spec.Code, end))
            });
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

            // ── 6b. 知识正文：把插件包 docs/ 复制进项目工作区（§4.2；覆盖式，不删用户补充）────
            // 宿主不会自动铺 docs（TryWriteSkillFile 只处理技能单文件），故由插件自铺；
            // 目标目录与 StepPrompts.DocsDirectory 保持同一约定。
            watch = BeginOperation(Step, "铺装知识正文", @"docs\ → .luban-agent\plugin-newproject\docs");
            var docs = CopyPluginDocs(workspace.RootPath);
            EndOperation(Step, "铺装知识正文",
                $"{docs.Count} 份知识正文已写入 {docs.Target}（覆盖式，保留用户手工补充的文件）",
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

            // 模板仓不再由插件配置校验：v0.33 起由助手在 Step 3 只读准备阶段按 docs/org-reference.md 的
            // 按端名称约定定位并回填（未定位到即 Step 3 预检失败），Step 2 此处不做模板仓必填校验。

            // pin 不再必填：留空＝助手在 Step 3 预检 / 执行续跑时按 docs/org-reference.md 的
            // develop → master → main → 模板默认分支 实时解析（模板库查分支需联网，校验发生在预检里）。
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
    /// <remarks>
    /// <para><b>v0.3 口径（一次只读准备下发）</b>：插件<b>不再</b>自己拿 <c>CodeupClient</c> / PAT 去联网预检；
    /// 改为把「只读准备」下发到当前项目会话，由助手按 <c>docs/step3-repositories.md</c> 用云效只读 MCP 拉事实，
    /// 写进结论文件 <c>step3-*.json</c>；插件回读该结论并映射成确认界面读数。</para>
    /// <para><b>薄校验</b>：只做不联网的前置校验（档案 / 建库计划 / 本地目录 / 组织 ID 非空）；任一不满足即
    /// 不下发（避免让助手白跑一轮）。代码组名与父级 ID 都改由结论产物 <c>kind=group</c> 的
    /// <see cref="GroupArtifactExtra"/> 映射，不再依赖本地配置模板。</para>
    /// </remarks>
    public async Task<RepositoryPrecheck> PrecheckRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Repositories;
        const string Action = "Step 3 准备（只读）";

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

        if (plan.Count == 0)
            result.Errors.Add("建库计划为空：Step 1 至少要选择后端与管理端。");

        if (string.IsNullOrWhiteSpace(result.OrganizationId))
            result.Errors.Add("未配置云效组织 ID：请到 codex「设置 → 云效连接」填写后重新准备（PRD §4.9）。");

        if (string.IsNullOrWhiteSpace(result.GroupName))
            result.Errors.Add("项目代码组名为空：请检查插件配置 groupNameFormat（默认 {CODE} 需能由项目缩写展开出非空名字）。");

        if (string.IsNullOrWhiteSpace(spec.LocalPath))
            result.Errors.Add("档案缺少本地目录，无法落结论文件：请重跑 Step 1 / Step 2。");

        // 组织 ID 是插件侧已知的运行时配置：写进档案，供 Step 3 / Step 7 四段提示词原样注入助手。
        // 否则助手只能按 schema 占位符写 null，执行时再传空串——空串会绕过客户端「未提供则回落配置」的兜底。
        if (!string.IsNullOrWhiteSpace(result.OrganizationId))
            spec.OrganizationId = result.OrganizationId;

        // 前置不满足即不下发（避免让助手白跑一轮）
        if (result.Errors.Count > 0)
        {
            Record(Step, Action, string.Join("；", result.Errors), StepStatus.Failed, TimeSpan.Zero);
            return result;
        }

        // 方案 A：知识正文不可用即不下发。助手读不到 docs 只能凭记忆，等于必然白跑或误报。
        var docsError = EnsureKnowledgeDocs(spec.LocalPath!);
        if (docsError is not null)
        {
            result.Errors.Add(docsError);
            Record(Step, Action, docsError, StepStatus.Failed, TimeSpan.Zero);
            return result;
        }

        using var permissionScope = EnterPermissionScope();
        var watch = BeginOperation(Step, Action,
            $"{plan.Count} 端 · 组织 {result.OrganizationId} · 代码组 {result.GroupName}");

        try
        {
            var resultFile = StepResultFile.BuildPath(spec.LocalPath!, 3, StepResultFile.PreparePhase);
            StepResultFile.EnsureDirectory(resultFile);

            // 时效基线：只有「本次下发之后」写出的结论文件才算本次产出（§6.1.3）
            var startedAt = DateTime.UtcNow;
            var outcome = await CreateBridge()
                .RunAsync(spec, StepPrompts.BuildRepositoryPreparePrompt(spec, plan, resultFile), resultFile, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.SessionCreated && !string.IsNullOrWhiteSpace(outcome.SessionId))
                spec.SessionId = outcome.SessionId;

            var (report, reportPath, takenAt, readError) = ReadFreshReport(spec.LocalPath!, 3, startedAt, prepare: true);
            if (report is null && !string.IsNullOrWhiteSpace(readError))
                result.Errors.Add(readError!);

            // 记住本次准备结论路径：执行续跑据它下发「事实来源」（进程重启后为空才回退取最新一份）
            if (report is not null && !string.IsNullOrWhiteSpace(reportPath))
                _repositoryPrepareResultFile = reportPath;

            var mapped = report is null
                ? new RepositoryPrecheck
                {
                    OrganizationId = result.OrganizationId,
                    GroupName = result.GroupName,
                    TakenAt = takenAt
                }
                : ArtifactViewModelMapper.ToRepositoryPrecheck(report, takenAt, spec.Ends);

            // 组织 ID 以插件侧运行时配置为准：结论文件里的同值只作事实记录。
            // 助手没回写（旧结论 / 漏字段）时不阻断展示，但判定必须失败——否则执行续跑仍会拿到 null。
            var organizationHint = result.OrganizationId;
            result.OrganizationId = string.IsNullOrWhiteSpace(mapped.OrganizationId)
                ? organizationHint
                : mapped.OrganizationId;
            if (report is not null && string.IsNullOrWhiteSpace(mapped.OrganizationId))
            {
                result.Errors.Add("准备结论缺少 group.extra.organizationId：请重跑本步"
                                  + "（准备提示词已注入组织 ID，正常应回写同值）。");
            }

            result.GroupName = mapped.GroupName;
            result.GroupParentId = mapped.GroupParentId;
            result.GroupId = mapped.GroupId;
            result.GroupExists = mapped.GroupExists;
            result.GroupWebUrl = mapped.GroupWebUrl;
            result.Ends.Clear();
            result.Ends.AddRange(mapped.Ends);
            result.Errors.AddRange(mapped.Errors.Where(one => !result.Errors.Contains(one)));
            result.Warnings.AddRange(mapped.Warnings);

            var (evaluatedOk, evaluatedStatus, message) = StepReportEvaluator.EvaluateRepositorySetup(
                report, spec.Ends, outcome.Errors, localIssues: null, requireCloned: false);

            // 判定入口说不过（status=failed / ok=false）时，即使映射没抓到具体缺口也不能算「准备就绪」。
            // needsInput 属正常形态（用户确认后在执行续跑里处理交互项），不在此列。
            if (!evaluatedOk && evaluatedStatus != StepReportStatus.NeedsInput
                && !string.IsNullOrWhiteSpace(message) && !result.Errors.Contains(message))
            {
                result.Errors.Add(message);
            }

            var lines = DescribeTurn(outcome, resultFile);
            if (report is not null && !string.IsNullOrWhiteSpace(reportPath))
                lines.Add($"结论文件解析：{reportPath}");
            lines.Add($"结论：{(result.IsReady ? "准备就绪，可在面板确认后执行" : result.Errors.FirstOrDefault() ?? message)}");

            EndOperation(Step, Action, string.Join("\n", lines),
                result.IsReady ? StepStatus.Succeeded : StepStatus.Failed, watch.Elapsed);

            // 落库必须在 EndOperation 之后：终态快照（StepStates）按内存操作记录回放生成，且只回放终态记录。
            SaveSpec(spec);

            return result;
        }
        catch (OperationCanceledException)
        {
            EndOperation(Step, Action, "已取消（准备阶段只读，未创建任何云效资源，可直接重跑本步）。",
                StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            result.Errors.Add("第 3 步准备（只读）已取消。");
            return result;
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 3 准备（只读）失败", ex);
            EndOperation(Step, Action, ex.Message, StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            result.Errors.Add($"第 3 步准备失败：{ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// 回读「本次下发之后」产出的结论文件（时效口径，§6.1.3）。
    /// </summary>
    /// <remarks>
    /// <see cref="HostAssistantBridge.RunAsync"/> 只按传入路径回读、<b>不做</b>修改时间过滤；助手写结论文件用的是
    /// 另一条路径，故这里按「文件修改时间 ≥ 下发时刻」判定本次新产出，取最新一份解析。旧结论一律不算数，
    /// 避免上一次尝试的读数被误当本次结果。
    /// </remarks>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <param name="step">步骤号。</param>
    /// <param name="startedAtUtc">本次下发时刻（UTC）。</param>
    /// <param name="prepare">是否回读<b>准备（只读）</b>阶段的结论（<c>step{N}-prepare-*.json</c>）。</param>
    /// <returns>已解析结论 + 路径 + 修改时间 + 读取 / 解析错误。</returns>
    private static (StepReport? Report, string? Path, DateTimeOffset TakenAt, string? Error) ReadFreshReport(
        string workspaceRoot,
        int step,
        DateTime startedAtUtc,
        bool prepare = false)
    {
        var fresh = (prepare
                ? StepResultFile.ListPrepare(workspaceRoot, step)
                : StepResultFile.List(workspaceRoot, step))
            .Select(path => new FileInfo(path))
            .Where(info => info.LastWriteTimeUtc >= startedAtUtc)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ThenByDescending(info => info.FullName, StringComparer.Ordinal)
            .FirstOrDefault();

        if (fresh is null)
        {
            var pattern = prepare ? $"step{step}-{StepResultFile.PreparePhase}-*.json" : $"step{step}-*.json";
            return (null, null, DateTimeOffset.Now,
                $"本次下发后未见新的结论文件（{pattern}）：助手未产出结论。");
        }

        var takenAt = new DateTimeOffset(fresh.LastWriteTimeUtc);

        string content;
        try
        {
            content = File.ReadAllText(fresh.FullName);
        }
        catch (Exception ex)
        {
            return (null, fresh.FullName, takenAt, $"结论文件不可读（{fresh.FullName}）：{ex.Message}");
        }

        if (!StepReportParser.TryParse(content, out var report, out var parseError))
            return (null, fresh.FullName, takenAt, $"结论文件不可解析（{fresh.FullName}）：{parseError}");

        return (report, fresh.FullName, takenAt, null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>执行形态（路线 ② · v0.3）</b>：Step 3 的「准备（只读）」由 <see cref="PrecheckRepositoriesAsync"/> 产出结论文件；
    /// 本方法是用户确认后的<b>执行续跑</b>——把确认后的建库计划 + 准备结论文件路径下发，助手在<b>同一会话</b>里
    /// 建组 / 建库 / 克隆 / 替换占位符，插件再回读 <c>artifacts[]</c> 落账 + 本地复核（§5.2 / §6.1.4）。</para>
    /// <para><b>needsInput 往返（§6.1.3）</b>：目录非空等助手无法自行判定的情形，助手写 <c>status=needsInput</c> +
    /// <c>inputs[]</c>；插件就 <c>id=clone-conflict</c> 征询用户三选一，把答复拼进下一轮执行续跑（最多 <see cref="MaxRepositoryRounds"/> 轮）。
    /// 「清空后重克隆」的目录删除由<b>助手</b>执行（C5′），插件不再介入。</para>
    /// <para><b>重跑联动（§6.6 / C7′）</b>：通过后作废上一轮 Step 5 编译结论与 Step 7 流水线结论——本地代码与仓库地址都可能已变，
    /// 旧结论会误放行 / 误指向。</para>
    /// </remarks>
    public async Task<StepResult> CreateRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Repositories;
        const string Action = "Step 3 执行（建组建库并克隆）";

        var spec = Current;
        if (spec is null)
        {
            Record(Step, Action, "尚未建立项目档案，请先完成第 1 步。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        using var permissionScope = EnterPermissionScope();

        var plan = PlanRepositories(spec);
        if (plan.Count == 0)
        {
            const string Tip = "建库计划为空：Step 1 至少要选择后端与管理端。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        if (string.IsNullOrWhiteSpace(spec.LocalPath))
        {
            const string Tip = "档案缺少本地目录，无法落结论文件：请重跑 Step 1 / Step 2。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        // 方案 A：知识正文不可用即不下发（否则助手读不到 docs 只能凭记忆执行）。
        var docsError = EnsureKnowledgeDocs(spec.LocalPath);
        if (docsError is not null)
        {
            Record(Step, Action, docsError, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, docsError);
        }

        // 执行续跑仍要显式把组织 ID 下发（旧档案可能没写过；提示词拿不到值助手就会传空串）。
        if (!EnsureOrganizationId(spec))
        {
            const string Tip = "未配置云效组织 ID：请到 codex「设置 → 云效连接」填写后重新准备（PRD §4.9）。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        // 执行续跑以「准备（只读）」结论为事实来源（§5.2）：优先用 PrecheckRepositoriesAsync 记下的那一份；
        // 进程重启导致字段为空时，才回退取最新一份 step3-prepare-*.json（尽力而为）。缺它就不下发，引导用户先跑准备。
        // 方案 A：准备结论与执行结论分名，绝不能再回退到最新一份执行结论（旧执行结论会被当成本次预检事实）。
        var prepareResultFile = !string.IsNullOrWhiteSpace(_repositoryPrepareResultFile)
                                && File.Exists(_repositoryPrepareResultFile)
            ? _repositoryPrepareResultFile
            : StepResultFile.FindLatestPrepare(spec.LocalPath!, 3);
        if (string.IsNullOrWhiteSpace(prepareResultFile) || !File.Exists(prepareResultFile))
        {
            const string Tip = "缺少 Step 3 准备结论文件（step3-prepare-*.json）：请先在面板执行「准备（只读）」，确认后再执行本步。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var watch = BeginOperation(Step, Action,
            $"{plan.Count} 端 · 准备结论 {Path.GetFileName(prepareResultFile)}");

        try
        {
            StepReport? report = null;
            string? reportPath = null;
            string? lastResultFile = null;
            string? readError = null;
            string? failReason = null;
            var takenAt = DateTimeOffset.Now;
            var resolvedInputs = new List<string>();
            HostAssistantBridge.TurnOutcome? lastOutcome = null;
            var rounds = 0;

            // 一次只读准备下发 + 用户确认后同会话执行续跑：needsInput 时把答复拼进下一轮（§6.1.3）
            while (rounds < MaxRepositoryRounds)
            {
                rounds++;
                cancellationToken.ThrowIfCancellationRequested();

                var resultFile = StepResultFile.BuildPath(spec.LocalPath!, 3);
                StepResultFile.EnsureDirectory(resultFile);
                lastResultFile = resultFile;

                // 时效基线：只有「本次下发之后」写出的结论文件才算本次产出（§6.1.3）
                var startedAt = DateTime.UtcNow;
                var outcome = await CreateBridge()
                    .RunAsync(spec,
                        StepPrompts.BuildRepositoryExecutePrompt(spec, plan, prepareResultFile!, resultFile, resolvedInputs),
                        resultFile, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.SessionCreated && !string.IsNullOrWhiteSpace(outcome.SessionId))
                    spec.SessionId = outcome.SessionId;

                lastOutcome = outcome;
                (report, reportPath, takenAt, readError) = ReadFreshReport(spec.LocalPath!, 3, startedAt);

                var status = report is null ? StepReportStatus.Failed : StepReportEvaluator.ResolveStatus(report);
                if (status != StepReportStatus.NeedsInput)
                    break;

                // needsInput：只认「克隆冲突三选一」这一项（§6.3）
                var input = report!.Inputs.FirstOrDefault(one =>
                    string.Equals(one.Id?.Trim(), "clone-conflict", StringComparison.OrdinalIgnoreCase));
                if (input is null)
                {
                    failReason = "助手请求用户补充输入，但未给出可识别的交互项（inputs[] 缺少 id=clone-conflict）："
                                 + "请在面板重跑本步。";
                    break;
                }

                failReason = await ResolveCloneConflictsAsync(plan, input, resolvedInputs, cancellationToken);
                if (failReason is not null)
                    break;
            }

            if (failReason is null && report is null)
            {
                failReason = string.IsNullOrWhiteSpace(readError)
                    ? "助手未产出可解析的结论文件（step3-*.json）。"
                    : readError;
            }

            if (failReason is null && rounds >= MaxRepositoryRounds
                && StepReportEvaluator.ResolveStatus(report!) == StepReportStatus.NeedsInput)
            {
                failReason = $"连续 {MaxRepositoryRounds} 轮仍停留在「需要用户补充输入」："
                             + "请确认交互项后重跑本步。";
            }

            var ok = false;
            var message = failReason ?? string.Empty;
            IReadOnlyList<string> localIssues = Array.Empty<string>();

            if (failReason is null)
            {
                // §6.1.4：对结论自报的产物做插件侧可独立核对项（目录 / .git / refs / 占位符残余）
                localIssues = StepLocalVerifier.VerifyRepositories(report!, spec.Ends);
                (ok, _, message) = StepReportEvaluator.EvaluateRepositorySetup(
                    report, spec.Ends, lastOutcome!.Errors, localIssues, requireCloned: true);
            }

            IReadOnlyList<RemoteArtifact> artifacts = Array.Empty<RemoteArtifact>();
            if (ok)
            {
                artifacts = ApplyRepositoryArtifacts(spec, plan, report!);

                // §6.6 / C7′：本步重跑成功＝本地代码可能已随模板 / pin / 占位符替换而变，作废上一轮编译结论
                InvalidateBuildConclusion(spec);

                // 同理作废上一轮流水线结论：YAML 里写死了代码源地址与分支，仓库改名 / 重建后旧结论是误导。
                // 只清结论，不删云效上的流水线本体（远端产物一律不自动删除，PRD §4.5）。
                InvalidatePipelineConclusion(spec);

                spec.AdvanceTo(WizardStep.Database);
            }

            var lines = lastOutcome is null
                ? new List<string> { "本步未下发（无执行读数）。" }
                : DescribeTurn(lastOutcome, lastResultFile ?? string.Empty);
            if (report is not null && !string.IsNullOrWhiteSpace(reportPath))
                lines.Add($"结论文件解析：{reportPath}");
            if (localIssues.Count > 0)
                lines.Add("　本地复核：" + string.Join("；", localIssues.Take(3)));
            lines.Add($"结论：{message}");

            EndOperation(Step, Action, string.Join("\n", lines),
                ok ? StepStatus.Succeeded : StepStatus.Failed, watch.Elapsed);

            // 硬纪律：EndOperation 必须在 SaveSpec 之前（终态快照按内存操作记录回放，只回放终态记录）
            SaveSpec(spec);

            return ok
                ? StepResult.Ok(Step, message, artifacts)
                : StepResult.Fail(Step, message);
        }
        catch (OperationCanceledException)
        {
            EndOperation(Step, Action,
                "已取消（远端产物一律保留，可稍后重跑本步：同名复用不会重复建库）。", StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, "Step 3 建库并克隆已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 3 建库并克隆失败", ex);
            EndOperation(Step, Action, ex.Message, StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step,
                $"Step 3 失败：{ex.Message}（远端产物一律保留，可在云效控制台确认；修正后可直接重跑本步）");
        }
    }

    /// <summary>
    /// 就 <c>inputs[]</c> 里的「克隆冲突三选一」逐端征询用户，把答复拼进 <paramref name="resolvedInputs"/>（§6.1.3 / §6.3）。
    /// </summary>
    /// <param name="plan">本次建库计划（按端）。</param>
    /// <param name="input">助手给出的 <c>id=clone-conflict</c> 交互项。</param>
    /// <param name="resolvedInputs">累积的答复行（拼进下一轮执行续跑提示词）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失败原因（可继续续跑时返回 <c>null</c>）。</returns>
    private async Task<string?> ResolveCloneConflictsAsync(
        IReadOnlyList<EndRepository> plan,
        StepReportInput input,
        List<string> resolvedInputs,
        CancellationToken cancellationToken)
    {
        if (input.Ends.Count == 0)
            return "助手请求处置目录冲突，但未指明是哪一端（inputs[].ends 为空）：请在面板重跑本步。";

        foreach (var end in input.Ends)
        {
            var repository = plan.FirstOrDefault(one => one.End == end);
            if (repository is null)
                return $"助手要求处置 {EndLabel(end)} 的目录冲突，但该项目不在建库计划中。";

            var choice = await AskCloneConflictAsync(repository, cancellationToken);
            if (choice is null)
                return $"目标目录已存在且非空（{EndLabel(end)}：{repository.LocalPath}），但没有可用的交互通道："
                     + "请从向导面板重跑本步并选择「复用已克隆 / 清空后重克隆 / 中止」。";

            if (choice == CloneConflictChoice.Abort)
                return $"用户选择「中止」：{EndLabel(end)} 保持原样（远端产物保留，本地目录未改动）。";

            var answer = choice switch
            {
                CloneConflictChoice.Reuse =>
                    "复用已克隆（先做健康校验：工作树可用且 refs/heads/<pin> 存在，再继续）",
                CloneConflictChoice.Reclone =>
                    "清空目录后重新克隆（清理含只读 pack 文件的目录用 RunPythonAsync 的 shutil.rmtree(..., onerror=清只读后重试)）",
                _ => choice.ToString()
            };

            resolvedInputs.Add($"clone-conflict · {EndLabel(end)}（目录 {repository.LocalPath}）：{answer}");
        }

        return null;
    }

    /// <summary>
    /// 把执行续跑结论的 <c>artifacts[]</c> 回写档案（§5.2 末段 + §6.1.1）：<c>kind=group</c> 登记代码组产物，
    /// <c>kind=repository</c> 逐端回写仓库事实并登记远端产物。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="plan">本次建库计划（按端）。</param>
    /// <param name="report">已解析的执行续跑结论。</param>
    /// <returns>本次新登记的远端产物（供操作记录 / 结果回传）。</returns>
    /// <remarks>
    /// 只回写「结论文件给了事实」的字段：缺失项保留档案原值，避免助手漏写某项时把用户确认过的计划清空。
    /// 调用方（<see cref="CreateRepositoriesAsync"/>）已在本地复核 + 判定通过后才会走到这里。
    /// </remarks>
    private static IReadOnlyList<RemoteArtifact> ApplyRepositoryArtifacts(
        ProjectSpec spec,
        IReadOnlyList<EndRepository> plan,
        StepReport report)
    {
        var registered = new List<RemoteArtifact>();

        var group = report.Artifacts.FirstOrDefault(one =>
            string.Equals(one.Kind?.Trim(), "group", StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            var groupArtifact = ApplyGroupArtifact(spec, group);
            if (groupArtifact is not null)
                registered.Add(groupArtifact);
        }

        foreach (var repository in plan)
        {
            var fact = StepReportEvaluator.RepositoryArtifact(report, repository.End);
            if (fact is null)
                continue;

            if (!string.IsNullOrWhiteSpace(fact.Name))
                repository.RepositoryName = fact.Name!.Trim();
            if (!string.IsNullOrWhiteSpace(fact.ExternalId))
                repository.ExternalId = fact.ExternalId!.Trim();
            if (!string.IsNullOrWhiteSpace(fact.CloneUrl))
                repository.CloneUrl = fact.CloneUrl!.Trim();
            if (!string.IsNullOrWhiteSpace(fact.LocalPath))
                repository.LocalPath = fact.LocalPath!.Trim();
            if (!string.IsNullOrWhiteSpace(fact.TemplateBranch))
                repository.TemplateBranch = fact.TemplateBranch!.Trim();

            // 结论给了页面链接就用它；否则由克隆地址推导（去尾 .git），再退回档案原值
            repository.WebUrl = !string.IsNullOrWhiteSpace(fact.WebUrl)
                ? fact.WebUrl!.Trim()
                : string.IsNullOrWhiteSpace(repository.CloneUrl) ? repository.WebUrl : BuildWebUrl(repository.CloneUrl);

            if (fact.Extra is RepositoryArtifactExtra extra)
            {
                if (!string.IsNullOrWhiteSpace(extra.TemplateRepository))
                    repository.TemplateRepository = extra.TemplateRepository!.Trim();
                if (!string.IsNullOrWhiteSpace(extra.Visibility))
                    repository.Visibility = extra.Visibility!.Trim();
                if (!string.IsNullOrWhiteSpace(extra.DefaultBranch))
                    repository.DefaultBranch = extra.DefaultBranch!.Trim();

                // Cloned 语义＝「本地目录已就绪」（真克隆成功，或「复用已克隆」经健康校验通过）
                repository.Cloned = extra.Cloned;
            }

            var artifact = AddArtifact(spec, repository, RemoteArtifactKind.Repository);
            if (artifact is not null)
                registered.Add(artifact);
        }

        return registered;
    }

    /// <summary>
    /// 登记项目级远端产物「代码组」（重跑幂等：同名只更新地址，不重复入册）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="artifact">结论文件里的 <c>kind=group</c> 产物。</param>
    /// <returns>新登记的产物；已存在或不可命名时为 <c>null</c>。</returns>
    private static RemoteArtifact? ApplyGroupArtifact(ProjectSpec spec, StepReportArtifact artifact)
    {
        var name = string.IsNullOrWhiteSpace(artifact.Name)
            ? (artifact.ExternalId ?? string.Empty).Trim()
            : artifact.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var webUrl = artifact.WebUrl ?? string.Empty;
        var externalId = string.IsNullOrWhiteSpace(artifact.ExternalId) ? null : artifact.ExternalId!.Trim();

        var existing = spec.RemoteArtifacts.FirstOrDefault(one =>
            one.Kind == RemoteArtifactKind.Group
            && string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));

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

        // 方案 A：知识正文不可用即不下发。
        var docsError = EnsureKnowledgeDocs(spec.LocalPath);
        if (docsError is not null)
        {
            Record(Step, Action, docsError, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, docsError);
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

        // 方案 A：知识正文不可用即不下发。
        var docsError = EnsureKnowledgeDocs(spec.LocalPath);
        if (docsError is not null)
        {
            Record(Step, Action, docsError, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, docsError);
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
    /// <remarks>
    /// <para><b>执行形态（路线 ② · v0.3）</b>：分支整理 / 暂存 / 提交 / 推送全部由助手用<b>框架 git 工具组</b>完成，
    /// 插件只把「待推送的端」下发、回读结论、做插件侧本地复核（§5.3 / §6.1.4）。</para>
    /// <para><b>凭据由用户在外部环境配置</b>（credential helper / 系统凭据管理器）：插件不提供凭据通路、不拼鉴权 URL，
    /// 也不再有 <c>CodeupClient.PushAsync</c> 直连。</para>
    /// <para><b>限流退避</b>：线性 10s / 20s / 30s、最多 3 次（原 <c>PushRetryPolicy</c> 口径迁 <c>docs/step6-push.md</c>），
    /// 由助手按 docs 执行；插件不重试。</para>
    /// </remarks>
    public async Task<StepResult> PushAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Push;
        const string Action = "推送改动到自有仓库";

        var spec = Current;
        if (spec is null)
        {
            Record(Step, Action, "尚未建立项目档案，请先完成第 1 步。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        // 编译失败阻断 Step 6（PRD §4.3 Step 5 / §4.7 / 设计 §6.7 C7′）：向导的步骤顺序只是界面约束——常驻「重新推送」、
        // 步骤条回退、聊天入口都能绕开，故此处按档案事实做硬校验；判据已改为读 Step 5 最近一份结论文件
        // （见 StepGate.CanPush），StepStates 降级为仅界面进度显示。
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

        if (string.IsNullOrWhiteSpace(spec.LocalPath))
        {
            const string Tip = "档案缺少本地目录，无法落结论文件：请重跑 Step 1 / Step 2。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        var targets = PlanRepositories(spec)
            .Where(repository => repository.Cloned && !string.IsNullOrWhiteSpace(repository.LocalPath))
            .ToList();
        if (targets.Count == 0)
        {
            const string Tip = "没有任何已克隆的仓库可推送：请先完成 Step 3（按模板建库并克隆）。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        // 方案 A：知识正文不可用即不下发。
        var docsError = EnsureKnowledgeDocs(spec.LocalPath);
        if (docsError is not null)
        {
            Record(Step, Action, docsError, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, docsError);
        }

        var watch = BeginOperation(Step, Action, $"{targets.Count} 端 · 推送分支 {_settings.PushBranch}");

        try
        {
            var resultFile = StepResultFile.BuildPath(spec.LocalPath!, 6);
            StepResultFile.EnsureDirectory(resultFile);

            // 时效基线：只有「本次下发之后」写出的结论文件才算本次产出（§6.1.3）
            var startedAt = DateTime.UtcNow;
            var outcome = await CreateBridge()
                .RunAsync(spec, StepPrompts.BuildPushPrompt(spec, targets, resultFile), resultFile, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.SessionCreated && !string.IsNullOrWhiteSpace(outcome.SessionId))
                spec.SessionId = outcome.SessionId;

            var (report, reportPath, _, readError) = ReadFreshReport(spec.LocalPath!, 6, startedAt);

            var ok = false;
            var message = string.Empty;
            IReadOnlyList<string> localIssues = Array.Empty<string>();

            if (report is null)
            {
                message = string.IsNullOrWhiteSpace(readError)
                    ? "助手未产出可解析的结论文件（step6-*.json）。"
                    : readError;
            }
            else
            {
                // §6.1.4：核对远端跟踪分支 refs/remotes/origin/<pushBranch> 与自报 commit id 是否一致
                localIssues = StepLocalVerifier.VerifyPush(report, spec.Ends);
                (ok, _, message) = StepReportEvaluator.EvaluatePush(report, spec.Ends, outcome.Errors, localIssues);
            }

            IReadOnlyList<RemoteArtifact> artifacts = Array.Empty<RemoteArtifact>();
            if (ok)
            {
                var registered = new List<RemoteArtifact>();
                foreach (var repository in targets)
                {
                    // 产物名取「实际推送的分支」（结论里的 templateBranch），缺失时由 AddArtifact 退回仓库默认分支
                    var pushedBranch = StepReportEvaluator.RepositoryArtifact(report!, repository.End)?.TemplateBranch;
                    var artifact = AddArtifact(spec, repository, RemoteArtifactKind.Branch, pushedBranch);
                    if (artifact is not null)
                        registered.Add(artifact);
                }

                artifacts = registered;
                spec.AdvanceTo(WizardStep.Pipeline);
            }

            var lines = DescribeTurn(outcome, resultFile);
            if (report is not null && !string.IsNullOrWhiteSpace(reportPath))
                lines.Add($"结论文件解析：{reportPath}");
            if (localIssues.Count > 0)
                lines.Add("　本地复核：" + string.Join("；", localIssues.Take(3)));
            lines.Add($"结论：{message}");

            EndOperation(Step, Action, string.Join("\n", lines),
                ok ? StepStatus.Succeeded : StepStatus.Failed, watch.Elapsed);

            // 硬纪律：EndOperation 必须在 SaveSpec 之前（终态快照按内存操作记录回放，只回放终态记录）
            SaveSpec(spec);

            return ok
                ? StepResult.Ok(Step, message, artifacts)
                : StepResult.Fail(Step, message);
        }
        catch (OperationCanceledException)
        {
            EndOperation(Step, Action, "已取消（已推送的端不受影响，可直接重跑本步）。", StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, "Step 6 推送已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 6 推送失败", ex);
            EndOperation(Step, Action, ex.Message, StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, $"Step 6 失败：{ex.Message}（已推送的端不受影响；修正后可直接重跑本步）");
        }
    }

    /// <inheritdoc />
    public Task<StepResult> CreatePipelinesAsync(
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets = null,
        CancellationToken cancellationToken = default)
        => RunPipelineStepAsync(update: false, yamlOverrides, deployTargets, cancellationToken);

    /// <inheritdoc />
    public Task<StepResult> UpdatePipelinesAsync(
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets = null,
        CancellationToken cancellationToken = default)
        => RunPipelineStepAsync(update: true, yamlOverrides, deployTargets, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>执行形态（路线 ② · v0.3）</b>：YAML 正文来自随包分发的 <c>docs/pipeline-*.yml.md</c>，
    /// 由助手按文档渲染并写入结论文件（<c>results/step7-*.json</c>），插件回读 <c>artifacts[]</c> 映射成预览——
    /// <b>不在插件内渲染模板</b>（C10′）。</para>
    /// <para><b>只读准备</b>：本阶段<b>不创建 / 不更新</b>云效流水线（写入动作在用户确认后的执行续跑，
    /// 见 <see cref="CreatePipelinesAsync"/>）；助手给出的部署目标候选写在结论文件的 <c>inputs[]</c> 里，
    /// 面板据此预选、聊天路径据此提问（C15′）。</para>
    /// <para>读数缓存在服务实例上，供同步的 <see cref="PreviewPipelines"/> 应用手工微调后回显。</para>
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>准备读数（预览 + 交互项 + 映射缺口）。</returns>
    public async Task<PipelinePrepareResult> PreparePipelinesAsync(CancellationToken cancellationToken = default)
    {
        const WizardStep Step = WizardStep.Pipeline;
        const string Action = "Step 7 准备（只读）";

        var spec = Current;
        if (spec is null)
        {
            const string Tip = "尚未建立项目档案，请先完成第 1 步。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return CachePrepare(new PipelinePrepareResult { Errors = new[] { Tip }, Message = Tip });
        }

        using var permissionScope = EnterPermissionScope();

        if (string.IsNullOrWhiteSpace(spec.LocalPath))
        {
            const string Tip = "档案缺少本地目录，无法落结论文件：请重跑 Step 1 / Step 2。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return CachePrepare(new PipelinePrepareResult { Errors = new[] { Tip }, Message = Tip });
        }

        // 流水线 MCP（flow_*）同样要求组织 ID：旧档案没写过时按当前连接配置回填，仍空则不下发。
        if (!EnsureOrganizationId(spec))
        {
            const string Tip = "未配置云效组织 ID：请到 codex「设置 → 云效连接」填写后重新准备（PRD §4.9）。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return CachePrepare(new PipelinePrepareResult { Errors = new[] { Tip }, Message = Tip });
        }

        var targets = PlanRepositories(spec)
            .Where(repository => repository.Cloned && !string.IsNullOrWhiteSpace(repository.CloneUrl))
            .ToList();
        if (targets.Count == 0)
        {
            const string Tip = "没有任何已建库并克隆的仓库可建流水线：请先完成 Step 3（按模板建库并克隆）。";
            Record(Step, Action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return CachePrepare(new PipelinePrepareResult { Errors = new[] { Tip }, Message = Tip });
        }

        // 方案 A：知识正文不可用即不下发。
        var docsError = EnsureKnowledgeDocs(spec.LocalPath);
        if (docsError is not null)
        {
            Record(Step, Action, docsError, StepStatus.Failed, TimeSpan.Zero);
            return CachePrepare(new PipelinePrepareResult { Errors = new[] { docsError }, Message = docsError });
        }

        var watch = BeginOperation(Step, Action, $"{targets.Count} 端 · 只读准备（不触网写入）");

        try
        {
            var resultFile = StepResultFile.BuildPath(spec.LocalPath!, 7);
            StepResultFile.EnsureDirectory(resultFile);

            // 时效基线：只有「本次下发之后」写出的结论文件才算本次产出（§6.1.3）
            var startedAt = DateTime.UtcNow;
            var outcome = await CreateBridge()
                .RunAsync(spec, StepPrompts.BuildPipelinePreparePrompt(spec, targets, resultFile), resultFile, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.SessionCreated && !string.IsNullOrWhiteSpace(outcome.SessionId))
                spec.SessionId = outcome.SessionId;

            var (report, reportPath, _, readError) = ReadFreshReport(spec.LocalPath!, 7, startedAt);

            // 记住本次准备结论路径：执行续跑据它下发「事实来源」（进程重启后为空才回退取最新一份）
            if (report is not null && !string.IsNullOrWhiteSpace(reportPath))
                _pipelinePrepareResultFile = reportPath;

            var errors = new List<string>();
            IReadOnlyList<PipelinePreview> previews = Array.Empty<PipelinePreview>();
            IReadOnlyList<StepReportInput> inputs = Array.Empty<StepReportInput>();
            var message = string.Empty;

            if (report is null)
            {
                message = string.IsNullOrWhiteSpace(readError)
                    ? "助手未产出可解析的结论文件（step7-*.json）。"
                    : readError;
                errors.Add(message);
            }
            else
            {
                // 判定复用 Step 7 的判定入口（口径单一来源）。准备阶段的 needsInput（缺部署目标）属正常形态——
                // 部署目标本来就要用户来选（面板下拉 / 对话回复），故不阻断预览渲染；但仍要求逐端产物齐备。
                var (ok, status, evaluateMessage) = StepReportEvaluator.EvaluatePipeline(report, spec.Ends, outcome.Errors);
                if (status == StepReportStatus.NeedsInput)
                {
                    var missing = spec.Ends.Where(end => !StepReportEvaluator.HasPipelineArtifact(report, end)).ToList();
                    if (missing.Count > 0)
                        errors.Add("结论缺少端流水线产物：" + string.Join("、", missing.Select(EndLabel))
                                   + "（逐端完备性要求每端一条 kind=pipeline）。");
                }
                else if (!ok)
                {
                    errors.Add(evaluateMessage);
                }

                var mapped = ArtifactViewModelMapper.ToPipelinePreviews(report);
                if (mapped.Errors.Count > 0)
                    errors.AddRange(mapped.Errors);

                previews = mapped.Previews;
                inputs = report.Inputs;
                message = string.IsNullOrWhiteSpace(evaluateMessage) ? "Step 7 准备就绪。" : evaluateMessage;
            }

            var lines = DescribeTurn(outcome, resultFile);
            if (report is not null && !string.IsNullOrWhiteSpace(reportPath))
                lines.Add($"结论文件解析：{reportPath}");
            if (inputs.Count > 0)
                lines.Add("　交互项：" + string.Join("、", inputs.Select(one => one.Id)));
            if (errors.Count > 0)
                lines.Add("　未就绪：" + string.Join("；", errors.Take(3)));
            lines.Add($"结论：{message}");

            EndOperation(Step, Action, string.Join("\n", lines),
                errors.Count == 0 && previews.Count > 0 ? StepStatus.Succeeded : StepStatus.Failed, watch.Elapsed);

            // 硬纪律：EndOperation 必须在 SaveSpec 之前（终态快照按内存操作记录回放，只回放终态记录）
            SaveSpec(spec);

            return CachePrepare(new PipelinePrepareResult
            {
                Previews = previews,
                Inputs = inputs,
                Errors = errors,
                Message = message
            });
        }
        catch (OperationCanceledException)
        {
            EndOperation(Step, Action, "已取消（只读准备未产生远端改动，可直接重跑本步）。", StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            const string Tip = "Step 7 准备已取消。";
            return CachePrepare(new PipelinePrepareResult { Errors = new[] { Tip }, Message = Tip });
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] Step 7 准备失败", ex);
            EndOperation(Step, Action, ex.Message, StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            var tip = $"Step 7 准备失败：{ex.Message}";
            return CachePrepare(new PipelinePrepareResult { Errors = new[] { tip }, Message = tip });
        }
    }

    /// <summary>
    /// 缓存准备读数并回传（供同步的 <see cref="PreviewPipelines"/> 应用微调后回显）。
    /// </summary>
    /// <param name="result">准备读数。</param>
    /// <returns>同一实例。</returns>
    private PipelinePrepareResult CachePrepare(PipelinePrepareResult result)
    {
        _pipelinePrepareResult = result;
        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>纯读缓存</b>：数据源是最近一次 <see cref="PreparePipelinesAsync"/> 的结论映射（不触网、不再下发助手）；
    /// 尚无准备结论时返回空列表（界面应先调 <see cref="PreparePipelinesAsync"/>）。命中 <paramref name="yamlOverrides"/>
    /// 时回显微调后的原文并置 <see cref="PipelinePreview.Edited"/>；<paramref name="deployTargets"/> 仅用于回显
    /// <see cref="PipelinePreview.RecommendedDeployTargetUuid"/>（用户已选目标优先）。
    /// </remarks>
    /// <param name="yamlOverrides">按端手工微调的 YAML 原文（<c>null</c> 或空字典＝全部用准备结论的 YAML）。</param>
    /// <param name="deployTargets">按端选定的部署目标（用于推荐目标回显）。</param>
    /// <returns>按端预览。</returns>
    public IReadOnlyList<PipelinePreview> PreviewPipelines(
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets = null)
    {
        var cached = _pipelinePrepareResult;
        if (cached is null || cached.Previews.Count == 0)
            return Array.Empty<PipelinePreview>();

        return cached.Previews.Select(preview =>
        {
            var overridden = TryGetYamlOverride(yamlOverrides, preview.End);
            var selected = TryGetDeployTarget(deployTargets, preview.End);

            return new PipelinePreview
            {
                End = preview.End,
                PipelineName = preview.PipelineName,
                // 与创建同源：预览到什么就提交什么（微调优先，否则用准备结论的 YAML）
                Yaml = overridden ?? preview.Yaml,
                Edited = overridden is not null,
                PendingBindings = preview.PendingBindings,
                PipelineId = preview.PipelineId,
                WebUrl = preview.WebUrl,
                YamlIncludesDeployStage = preview.YamlIncludesDeployStage,
                // 用户已选目标优先；否则回显准备结论里的推荐 uuid（面板据此预选下拉）
                RecommendedDeployTargetUuid = selected is not null && !string.IsNullOrWhiteSpace(selected.Uuid)
                    ? selected.Uuid
                    : preview.RecommendedDeployTargetUuid
            };
        }).ToList();
    }

    /// <summary>
    /// 取出某端的手工微调 YAML（空白视为未微调）。
    /// </summary>
    /// <param name="yamlOverrides">按端微调表（可为 <c>null</c>）。</param>
    /// <param name="end">端。</param>
    /// <returns>微调原文；未提供返回 <c>null</c>。</returns>
    private static string? TryGetYamlOverride(IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides, ProjectEnd end)
        => yamlOverrides is not null && yamlOverrides.TryGetValue(end, out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    /// <summary>
    /// 取出某端本次选定的部署目标（uuid 为空的项按「未选」处理）。
    /// </summary>
    /// <param name="deployTargets">按端目标表（可为 <c>null</c>）。</param>
    /// <param name="end">端。</param>
    /// <returns>选中的主机组；未选返回 <c>null</c>。</returns>
    private static DeployHostGroup? TryGetDeployTarget(
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets,
        ProjectEnd end)
        => deployTargets is not null
           && deployTargets.TryGetValue(end, out var target)
           && !string.IsNullOrWhiteSpace(target.Uuid)
            ? target
            : null;

    /// <inheritdoc />
    /// <remarks>
    /// <b>失败即抛</b>（不返回空列表）：口径是「读不到主机组列表就不许创建流水线」，界面据异常文案显示红字并阻断创建。
    /// 真机口径：列表是<b>裸数组</b>、不带机器明细（<c>hostInfos</c> 恒 <c>null</c>），且查询参数不生效（一律全量返回）。
    /// </remarks>
    public async Task<IReadOnlyList<DeployHostGroup>> ListDeployTargetsAsync(CancellationToken cancellationToken = default)
    {
        using var permissionScope = EnterPermissionScope();

        var flow = Resolve<FlowClient>()
            ?? throw new InvalidOperationException(
                "宿主能力不完整：云效流水线客户端（FlowClient）不可用，读不到部署主机组列表。");

        if (string.IsNullOrWhiteSpace(await TryGetPatAsync(cancellationToken)))
            throw new InvalidOperationException(
                "未配置云效个人令牌（PAT）：请到 codex「设置 → 云效连接」填写后重试（PRD §4.9）。");

        var groups = await flow.ListHostGroupsAsync(organizationId: null, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("云效返回了空响应，未读到部署主机组列表（请稍后重试）。");

        var list = groups
            .Select(group => new DeployHostGroup
            {
                HostGroupId = group.Id ?? 0,
                Uuid = group.Uuid ?? string.Empty,
                Name = group.Name ?? string.Empty,
                HostNum = group.HostNum ?? 0,
                Region = group.AliyunRegion
            })
            .ToList();

        _host?.Log($"[newproject] Step 7 读到 {list.Count} 个部署主机组，其中 {list.Count(item => string.IsNullOrWhiteSpace(item.Uuid))} 个缺 uuid（不可用于部署）。");
        return list;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>失败即抛</b>，理由同 <see cref="ListDeployTargetsAsync"/>。真机口径：明细只在详情接口的 <c>hostInfos</c> 里，
    /// 且路径键<b>必须是数字 id</b>（用 uuid 当路径键会 400 <c>NumberFormatException</c>）。
    /// </remarks>
    public async Task<IReadOnlyList<DeployHostMachine>> ListDeployTargetMachinesAsync(
        long hostGroupId,
        CancellationToken cancellationToken = default)
    {
        if (hostGroupId <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(hostGroupId), hostGroupId, "主机组 ID 必须是正数（云效详情接口的路径键只认数字 id，不是 uuid）。");

        using var permissionScope = EnterPermissionScope();

        var flow = Resolve<FlowClient>()
            ?? throw new InvalidOperationException(
                "宿主能力不完整：云效流水线客户端（FlowClient）不可用，读不到主机组机器明细。");

        if (string.IsNullOrWhiteSpace(await TryGetPatAsync(cancellationToken)))
            throw new InvalidOperationException(
                "未配置云效个人令牌（PAT）：请到 codex「设置 → 云效连接」填写后重试（PRD §4.9）。");

        var detail = await flow.GetHostGroupAsync(hostGroupId, organizationId: null, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"云效未返回主机组 {hostGroupId} 的详情（该主机组可能已被删除）。");

        // 真机观测：hostNum=0 的空组详情里 hostInfos 为空列表或 null，两种情况都按「无机器」处理（不报错）。
        return (detail.HostInfos ?? new List<YunxiaoHostInfo>())
            .Select(info => new DeployHostMachine
            {
                InstanceName = info.InstanceName,
                Ip = info.Ip,
                Status = info.Status
            })
            .ToList();
    }

    /// <summary>
    /// Step 7 执行体（v0.3 助手驱动）：把「准备结论 + 用户确认（含微调与部署目标）」下发，助手创建 / 覆盖更新云效流水线，
    /// 插件回读结论、做本地复核与判定，再把流水线事实落档案（§5.3 / §6.1.4）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么不再由插件渲染 YAML、也不再直连 CodeupClient</b>：YAML 正文与待绑定清单由随包
    /// <c>docs/pipeline-*.yml.md</c> 交给助手按项目值渲染并写进结论文件（C10′）；云效写入也由助手用框架 MCP 工具组完成，
    /// 插件只负责判定与落账（§6.1.1）。
    /// </para>
    /// <para>
    /// <b>两条入口共用本执行体</b>：<paramref name="update"/><c>=false</c> 为创建（要求 Step 6 已成功，
    /// 闸门见 <see cref="StepGate.CanCreatePipelines"/>）；<c>true</c> 为「覆盖更新」（按档案里的
    /// <see cref="EndPipelineResult.PipelineId"/> 直改已有流水线，不设闸门）。
    /// </para>
    /// <para>
    /// <b>前置＝准备结论</b>：执行续跑以准备阶段结论文件为事实来源，缺它就不下发（引导用户先在面板执行准备并复核预览）。
    /// </para>
    /// <para>
    /// <b>幂等与不自动运行</b>：创建前按流水线名精确检索、命中同名即覆盖更新本次 YAML，全部由助手按 docs 执行
    /// （插件不重试、不检索）；本步<b>不发</b> <c>flow_run_pipeline</c>（PRD §6 #14）。
    /// </para>
    /// </remarks>
    /// <param name="update">是否为「覆盖更新」口径。</param>
    /// <param name="yamlOverrides">按端手工微调的 YAML 原文（<c>null</c>＝全部走准备结论）。</param>
    /// <param name="deployTargets">按端选定的部署目标（<c>null</c>／缺项＝用准备结论里的推荐值）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>步骤结果（含逐端流水线地址）。</returns>
    private async Task<StepResult> RunPipelineStepAsync(
        bool update,
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets,
        CancellationToken cancellationToken)
    {
        const WizardStep Step = WizardStep.Pipeline;
        var action = update ? "覆盖更新云效流水线" : "创建云效流水线";

        var spec = Current;
        if (spec is null)
        {
            Record(Step, action, "尚未建立项目档案，请先完成第 1 步。", StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, "尚未建立项目档案，请先在 Step 1 填写项目基本信息。");
        }

        // 闸门：与「编译失败阻断推送」同口径，只认档案事实（界面顺序 / 常驻入口 / 聊天入口都能绕开）。
        // 创建要求 Step 6 已成功；覆盖更新不设闸门（目标是档案里那条，重跑即纠偏）。
        // 拒绝不记操作记录——一次「未执行的拒绝」不是本步的终态，记了会被 SnapshotStepStates 回放写进档案、
        // 把早已成功的 Step 7 状态改写成失败（提示由调用方按 StepResult.Message 呈现）。
        if (!update && !StepGate.CanCreatePipelines(spec))
        {
            const string Tip = "推送（Step 6）尚未全部成功，已拒绝创建流水线：仓库里还没有可构建的代码，"
                               + "请先完成 Step 6（推送）再执行本步（结论见 Step 6 的逐端状态行与右侧操作记录）。";
            return StepResult.Fail(Step, Tip);
        }

        using var permissionScope = EnterPermissionScope();

        if (string.IsNullOrWhiteSpace(spec.LocalPath))
        {
            const string Tip = "档案缺少本地目录，无法落结论文件：请重跑 Step 1 / Step 2。";
            Record(Step, action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        // 流水线 MCP（flow_*）同样要求组织 ID：旧档案没写过时按当前连接配置回填，仍空则不下发。
        if (!EnsureOrganizationId(spec))
        {
            const string Tip = "未配置云效组织 ID：请到 codex「设置 → 云效连接」填写后重新准备（PRD §4.9）。";
            Record(Step, action, Tip, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, Tip);
        }

        // 执行续跑以「准备（只读）」结论为事实来源（§5.3）：优先用 PreparePipelinesAsync 记下的那一份；
        // 进程重启导致字段为空时，才回退取最新一份 step7-*.json（尽力而为）。缺它就不下发，引导用户先跑准备。
        var prepareResultFile = !string.IsNullOrWhiteSpace(_pipelinePrepareResultFile)
                                && File.Exists(_pipelinePrepareResultFile)
            ? _pipelinePrepareResultFile
            : StepResultFile.FindLatest(spec.LocalPath!, 7);
        if (string.IsNullOrWhiteSpace(prepareResultFile) || !File.Exists(prepareResultFile))
        {
            const string Tip = "缺少 Step 7 准备结论文件（step7-*.json）：请先在面板执行「准备（只读）」，"
                               + "复核预览后再执行本步。";
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

        // 方案 A：知识正文不可用即不下发。
        var docsError = EnsureKnowledgeDocs(spec.LocalPath);
        if (docsError is not null)
        {
            Record(Step, action, docsError, StepStatus.Failed, TimeSpan.Zero);
            return StepResult.Fail(Step, docsError);
        }

        var watch = BeginOperation(Step, action,
            $"{targets.Count} 端 · 准备结论 {Path.GetFileName(prepareResultFile)}");

        try
        {
            var resultFile = StepResultFile.BuildPath(spec.LocalPath!, 7);
            StepResultFile.EnsureDirectory(resultFile);

            // 时效基线：只有「本次下发之后」写出的结论文件才算本次产出（§6.1.3）
            var startedAt = DateTime.UtcNow;
            var outcome = await CreateBridge()
                .RunAsync(spec,
                    StepPrompts.BuildPipelineExecutePrompt(
                        spec, targets, prepareResultFile!, resultFile, yamlOverrides, deployTargets, update),
                    resultFile, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.SessionCreated && !string.IsNullOrWhiteSpace(outcome.SessionId))
                spec.SessionId = outcome.SessionId;

            var (report, reportPath, _, readError) = ReadFreshReport(spec.LocalPath!, 7, startedAt);

            var ok = false;
            var message = string.Empty;
            IReadOnlyList<string> localIssues = Array.Empty<string>();

            if (report is null)
            {
                message = string.IsNullOrWhiteSpace(readError)
                    ? "助手未产出可解析的结论文件（step7-*.json）。"
                    : readError;
            }
            else
            {
                // §6.1.4：流水线在云效远端，无本地可独立核对的证据，只取一条如实的残余风险说明（不作为失败依据）
                localIssues = StepLocalVerifier.PipelineResidualNotes(report, spec.Ends);
                (ok, _, message) = StepReportEvaluator.EvaluatePipeline(report, spec.Ends, outcome.Errors);
            }

            IReadOnlyList<RemoteArtifact> artifacts = Array.Empty<RemoteArtifact>();
            if (ok)
            {
                artifacts = ApplyPipelineArtifacts(spec, targets, report!, deployTargets);
                spec.AdvanceTo(WizardStep.Pipeline);
            }

            var lines = DescribeTurn(outcome, resultFile);
            if (report is not null && !string.IsNullOrWhiteSpace(reportPath))
                lines.Add($"结论文件解析：{reportPath}");
            if (localIssues.Count > 0)
                lines.Add("　残余风险：" + string.Join("；", localIssues.Take(3)));
            lines.Add($"结论：{message}");

            EndOperation(Step, action, string.Join("\n", lines),
                ok ? StepStatus.Succeeded : StepStatus.Failed, watch.Elapsed);

            // 硬纪律：EndOperation 必须在 SaveSpec 之前（终态快照按内存操作记录回放，只回放终态记录）
            SaveSpec(spec);

            return ok
                ? StepResult.Ok(Step, message, artifacts)
                : StepResult.Fail(Step, message);
        }
        catch (OperationCanceledException)
        {
            EndOperation(Step, action, "已取消（远端产物一律保留，可直接重跑本步）。", StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, "Step 7 已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError($"[newproject] Step 7 {action}失败", ex);
            EndOperation(Step, action, ex.Message, StepStatus.Failed, watch.Elapsed);
            SaveSpec(spec);
            return StepResult.Fail(Step, $"Step 7 失败：{ex.Message}（已处理的端不受影响；修正后可直接重跑本步）");
        }
    }

    /// <summary>
    /// 把执行续跑结论的 <c>artifacts[]</c> 回写档案（§5.3 + §6.1.1）：逐端 <c>kind=pipeline</c> 回写流水线事实并登记远端产物。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="targets">本次要建流水线的端（已克隆）。</param>
    /// <param name="report">已解析的执行续跑结论。</param>
    /// <param name="deployTargets">按端选定的部署目标（用于回写本次实际用的主机组）。</param>
    /// <returns>本次新登记的远端产物（供操作记录 / 结果回传）。</returns>
    /// <remarks>
    /// <para>
    /// 只回写「结论文件给了事实」的字段：缺失项保留档案原值，避免助手漏写某项时把用户确认过的预览清空。
    /// </para>
    /// <para>
    /// <b>Created 的推导</b>：<see cref="PipelineArtifactExtra"/> 没有「本次是创建还是覆盖」标记，故
    /// <see cref="EndPipelineResult.Created"/> 只能由「档案里原先有没有该端 <see cref="EndPipelineResult.PipelineId"/>」推导
    /// ——首次落账为 <c>true</c>，此后重跑（含命中同名覆盖）为 <c>false</c>。这是一处轻微保真损失（见交付汇报）。
    /// </para>
    /// <para>调用方（<see cref="RunPipelineStepAsync"/>）已在本地复核 + 判定通过后才会走到这里。</para>
    /// </remarks>
    private static IReadOnlyList<RemoteArtifact> ApplyPipelineArtifacts(
        ProjectSpec spec,
        IReadOnlyList<EndRepository> targets,
        StepReport report,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets)
    {
        var registered = new List<RemoteArtifact>();

        foreach (var repository in targets)
        {
            var fact = StepReportEvaluator.PipelineArtifact(report, repository.End);
            if (fact is null)
                continue;

            var extra = StepReportEvaluator.PipelineExtra(report, repository.End);
            var selected = TryGetDeployTarget(deployTargets, repository.End);

            // 流水线名：结论给了就用它；否则退回仓库名（不做名称格式再推导，避免与远端实际不符）
            var name = string.IsNullOrWhiteSpace(fact.Name) ? repository.RepositoryName : fact.Name!.Trim();
            var pipelineId = fact.ExternalId?.Trim() ?? string.Empty;
            var webUrl = string.IsNullOrWhiteSpace(fact.WebUrl)
                ? BuildPipelineWebUrl(pipelineId)
                : fact.WebUrl!.Trim();

            var result = spec.PipelineResults.FirstOrDefault(one => one.End == repository.End);

            // Created 只能由「档案里原先没有该端流水线 id」推导（extra 无创建 / 覆盖标记）
            var created = string.IsNullOrWhiteSpace(result?.PipelineId);
            if (result is null)
            {
                result = new EndPipelineResult { End = repository.End };
                spec.PipelineResults.Add(result);
            }

            result.PipelineId = pipelineId;
            result.PipelineName = name;
            result.WebUrl = webUrl;
            result.Created = created;
            result.DeployStageIncluded = extra?.YamlIncludesDeployStage ?? false;
            // 本次实际用的主机组：用户已选优先，否则用结论里的推荐 uuid（名称只认用户选择）
            result.DeployMachineGroupUuid = selected is not null && !string.IsNullOrWhiteSpace(selected.Uuid)
                ? selected.Uuid
                : extra?.RecommendedDeployTargetUuid?.Trim() ?? string.Empty;
            result.DeployMachineGroupName = selected is not null ? selected.Name : string.Empty;
            if (extra is not null)
                result.PendingBindings = extra.PendingBindings?.ToList() ?? new List<string>();
            if (!string.IsNullOrWhiteSpace(fact.Yaml))
                result.Yaml = fact.Yaml!;

            var artifact = AddArtifact(spec, repository, RemoteArtifactKind.Pipeline,
                name: name, externalId: pipelineId, webUrl: webUrl);
            if (artifact is not null)
                registered.Add(artifact);
        }

        return registered;
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

            // 只保留最近 N 条：超长流程下避免记录无限增长拖慢整表刷新
            if (_operations.Count > MaxOperationRecords)
                _operations.RemoveRange(0, _operations.Count - MaxOperationRecords);
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
    /// 把插件包内的 <c>docs/</c> 知识正文复制进项目工作区（<c>{工作区}\.luban-agent\plugin-newproject\docs\</c>）。
    /// </summary>
    /// <param name="workspaceRoot">项目工作区根目录。</param>
    /// <returns>已复制份数 + 目标目录。</returns>
    /// <remarks>
    /// <para>覆盖式复制：同名文件以插件包为准（升级即生效），但**不删除**用户手工补充的文件（§4.2）。</para>
    /// <para>
    /// <b>方案 A</b>：插件包缺 <c>docs/</c> 或复制失败一律<b>抛异常</b>（Step 2 按失败呈现），不再静默 Skipped——
    /// 知识正文缺失时后续各步的助手只能凭记忆执行，必须在本步就暴露。
    /// </para>
    /// </remarks>
    private (int Count, string Target) CopyPluginDocs(string workspaceRoot)
    {
        var (ok, count, target, error) = KnowledgeDocs.Sync(ResolvePluginPackageRoot(), workspaceRoot);
        if (!ok)
        {
            Logger.Warn($"[newproject] 铺装 docs 知识正文失败：{error}");
            throw new InvalidOperationException($"插件包知识正文不可用：{error}");
        }

        return (count, target);
    }

    /// <summary>取当前插件包根目录（内置插件 / 卸载中返回 <c>null</c>）。</summary>
    private string? ResolvePluginPackageRoot()
    {
        var pluginId = _host?.PluginId;
        if (string.IsNullOrWhiteSpace(pluginId))
            return null;

        var manager = _host!.Services.GetService(typeof(PluginManager)) as PluginManager;
        return manager?.GetPlugin(pluginId)?.Directory;
    }

    /// <summary>
    /// 每步下发前的知识正文守卫（方案 A）：先覆盖式同步 <c>docs/</c>，不可用即返回可操作文案（调用方不下发）。
    /// </summary>
    /// <remarks>
    /// <b>为什么每步都做</b>：此前只在 Step 2 复制一次，工作区被外部清理（真机 13:40 运行期目录整体重建）后，
    /// Step 3 仍照常下发，助手读不到文档只能凭记忆回一个 DONE。改为每步自愈 + 不可用即失败。
    /// </remarks>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <returns>可用返回 <c>null</c>；不可用返回失败文案。</returns>
    private string? EnsureKnowledgeDocs(string workspaceRoot)
    {
        var (ok, _, target, error) = KnowledgeDocs.Sync(ResolvePluginPackageRoot(), workspaceRoot);
        if (ok)
            return null;

        Logger.Warn($"[newproject] 知识正文不可用，已阻止本步下发：{error}");
        return $"知识正文不可用（{target}）：{error}。"
               + "请重装「新建项目」插件包（其 docs/ 缺失或被清理）后重跑本步。";
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
    /// <param name="branch">分支产物的分支名（Step 6 实际推送的分支，见 <c>docs/step6-push.md</c>）。</param>
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