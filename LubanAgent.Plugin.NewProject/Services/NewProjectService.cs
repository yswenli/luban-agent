/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： NewProjectService
*创建时间：2026/10/16
*描述：共享能力层实现（PRD §4.10.2）。向导面板与聊天入口共用同一实例。
*****************************************************************************/

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Lifecycle;
using LuBan.AIAgent.Plugins.Security;
using LuBan.AIAgent.Sessions;
using LubanAgentCore.Configuration;
using LubanAgentCore.Hosting;
using LubanAgentCore.Infrastructure;
using LubanAgentCore.Services;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Configuration;
using LubanAgent.Plugin.NewProject.Serialization;

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
/// <b>当前进度（M4-P2）</b>：Step 1 校验 + Step 2（工作区 / 会话 / 技能 / MCP / 档案快照）已落地；
/// Step 3~7 按计划在 P3~P5 分批实现（未实现的步骤返回明确的「未实现」结果，不抛异常）。
/// </para>
/// </remarks>
public sealed class NewProjectService : INewProjectService
{
    /// <summary>英文缩写命名规范（PRD §4.3：只能小写字母开头，后面可跟小写字母或数字）。</summary>
    private static readonly Regex CodePattern = new("^[a-z][a-z0-9]*$", RegexOptions.Compiled);

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
        _store.Save(spec);
        RaiseChanged();

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
            spec.CurrentStep = WizardStep.Repositories;
            _store.Save(spec);
            RaiseChanged();

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
            _store.Save(spec);
            RaiseChanged();

            return StepResult.Fail(Step,
                $"Step 2 初始化失败：{ex.Message}（已回滚本步；修正后可直接重跑本步）");
        }
    }

    /// <inheritdoc />
    public Task<StepResult> CreateRepositoriesAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Repositories, "M4-P3");

    /// <inheritdoc />
    public Task<StepResult> SetupDatabaseAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Database, "M4-P4");

    /// <inheritdoc />
    public Task<StepResult> BuildAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Build, "M4-P4");

    /// <inheritdoc />
    public Task<StepResult> PushAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Push, "M4-P3");

    /// <inheritdoc />
    public Task<StepResult> CreatePipelinesAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Pipeline, "M4-P5");

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
        var updated = false;
        lock (_gate)
        {
            for (var i = _operations.Count - 1; i >= 0; i--)
            {
                var operation = _operations[i];
                if (operation.Step != step || operation.Status != StepStatus.Running)
                    continue;

                if (!string.Equals(operation.Action, action, StringComparison.Ordinal))
                    continue;

                operation.Detail = detail;
                operation.Status = status;
                operation.Duration = duration;
                updated = true;
                break;
            }
        }

        if (updated)
            RaiseChanged();
        else
            Record(step, action, detail, status, duration);
    }

    /// <summary>
    /// 尚未实现的步骤：返回明确失败结果（不抛异常，面板可原样展示）。
    /// </summary>
    /// <param name="step">步骤。</param>
    /// <param name="batch">计划实现批次。</param>
    /// <returns>失败结果。</returns>
    private static Task<StepResult> NotYetAsync(WizardStep step, string batch)
        => Task.FromResult(StepResult.Fail(step, $"该步骤将在 {batch} 实现（M4 P1 骨架阶段）。"));

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