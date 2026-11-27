/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： YunxiaoInitService
*描述：云效 S2 项目初始化服务 Part1（manifests + preflight）（设计 §3/§4 / S2b §11）——
*      模板库（自定义模板接口）全量翻页聚合 / 代码组 / 工作区清单 / 会话通路探测；
*      预检零副作用：NamingDeriver 派生 + GitCommandPlanner 序列 + 冲突检查 + 复用标记。
*      执行编排（Exec/Retry）在批次 C 追加。
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §3 接口契约 / §4 服务端设计 / §11 S2b 增补
*
*****************************************************************************/

using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgentWebHost.Services.Init;

/// <summary>工作区目录条目（manifests 的 <c>workspaces</c> 项）。</summary>
/// <param name="Id">工作区标识（内部绑定）。</param>
/// <param name="RootPath">根目录（仅展示与目录冲突检查）。</param>
public sealed record WorkspaceDir(string Id, string RootPath);

/// <summary>
/// 工作区目录提供者（测试假实现 / 生产实现可替换）。
/// </summary>
public interface IWorkspaceDirectoryProvider
{
    /// <summary>列出当前用户的工作区目录。</summary>
    IReadOnlyList<WorkspaceDir> List();
}

/// <summary>
/// <see cref="IWorkspaceDirectoryProvider"/> 生产实现：经
/// <c>WebApi.Runtime.CreateRequestScope(WebApi.UserId)</c> 取 <c>IWorkspaceManager</c>
/// （与 <c>WebAgentService</c> 壳同款取 DI）。
/// </summary>
public sealed class WorkspaceDirectoryProvider : IWorkspaceDirectoryProvider
{
    /// <inheritdoc />
    public IReadOnlyList<WorkspaceDir> List()
    {
        // 接口为同步（预检同步路径）；异步源经 Task.Run 包装，避免阻塞调用链上的同步上下文
        return Task.Run(async () =>
        {
            await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
            var workspaces = await scope.GetRequiredService<IWorkspaceManager>()
                .GetUserWorkspacesAsync()
                .ConfigureAwait(false);
            return workspaces
                .Select(w => new WorkspaceDir(w.WorkspaceId, w.RootPath))
                .ToList();
        }).GetAwaiter().GetResult();
    }
}

/// <summary>母版库清单项（manifests 的 <c>repos</c> 项）。</summary>
/// <param name="Name">模板名（单端 / 综合模板均收录，S2b §11.1 模板源改真值）。</param>
/// <param name="Description">描述（可空；模板登记的说明自带停用提示）。</param>
/// <param name="TemplateProjectId">模板 ProjectId（建库 <c>templateProjectId</c> 降级路径预留）。</param>
public sealed record ManifestRepo(string Name, string? Description, long TemplateProjectId = 0);

/// <summary>代码组清单项（manifests 的 <c>groups</c> 项）。</summary>
/// <param name="Id">代码组 ID（建库 <c>namespaceId</c>）。</param>
/// <param name="Name">代码组名。</param>
public sealed record ManifestGroup(long Id, string Name);

/// <summary>manifests 出参（设计 §3：<c>{ repos, groups, workspaces, sessionReady }</c>）。</summary>
/// <param name="Repos">模板库（<c>ListTemplateRepositoriesAsync</c> 全量翻页聚合，含单端与综合模板）。</param>
/// <param name="Groups">代码组。</param>
/// <param name="Workspaces">工作区目录。</param>
/// <param name="SessionReady">会话通路可用性（false 时壳层禁「开始执行」并明示）。</param>
public sealed record ManifestsResult(
    IReadOnlyList<ManifestRepo> Repos,
    IReadOnlyList<ManifestGroup> Groups,
    IReadOnlyList<WorkspaceDir> Workspaces,
    bool SessionReady);

/// <summary>三端配置（设计 §3 preflight 的 <c>ends</c>：mobile / admin / backend）。</summary>
/// <param name="End">端名（mobile / admin / backend）。</param>
/// <param name="On">是否勾选。</param>
/// <param name="Name">流水线名（可改；FR-3 用）。</param>
/// <param name="Dir">构建目录（可改；FR-3 用）。</param>
/// <param name="Port">后端端口（仅 backend；预检端口占用用）。</param>
public sealed record InitEndConfig(
    string End,
    bool On,
    string? Name = null,
    string? Dir = null,
    int? Port = null);

/// <summary>预检入参。</summary>
/// <param name="TemplateRepoName">模板名（首个 <c>-</c> 前段为模板代号，单端 / 综合模板均可）。</param>
/// <param name="NewCode">新项目代号。</param>
/// <param name="WorkspaceId">工作区标识。</param>
/// <param name="Branch">分支（默认 master 由壳层传）。</param>
/// <param name="GroupNewName">拟新建代码组名（可空=复用既有组，批次 C 建组用）。</param>
/// <param name="TemplatePipelineNames">母版流水线名（命名派生用）。</param>
/// <param name="Ends">三端配置（端口预检用）。</param>
public sealed record PreflightRequest(
    string TemplateRepoName,
    string NewCode,
    string WorkspaceId,
    string Branch,
    IReadOnlyList<string> TemplatePipelineNames,
    string? GroupNewName = null,
    IReadOnlyList<InitEndConfig>? Ends = null);

/// <summary>预检出参（壳层映射见 <see cref="YunxiaoInitService.PreflightAsync"/>）。</summary>
/// <param name="Plan">三段命名派生（默认值，可逐段改写后传执行；复用库 <see cref="NamingPlan.Dirs"/> 为空——无改名动作）。</param>
/// <param name="GitCommands">git 命令序列（与执行同源，脱敏裸 URL；复用库不生成 git 步骤）。</param>
/// <param name="Conflicts">冲突清单（空 = 可执行；「库已存在」不在此列——转复用标记）。</param>
/// <param name="SessionReady">会话通路可用性（false → 存在需新建的库时执行入口禁用）。</param>
/// <param name="Warnings">非阻断提示（如复用库「结构待校验」）。</param>
/// <param name="ReusedLibs">将复用的既有库名清单（命中 <c>FindRepoByNameAsync</c>，零 git 步骤）。</param>
/// <param name="StructureChecks">复用库结构校验（S2e §14 修复四：子目录存在性结果 / 待校验；新建库为空）。</param>
public sealed record PreflightResult(
    NamingPlan Plan,
    IReadOnlyList<GitStep> GitCommands,
    IReadOnlyList<string> Conflicts,
    bool SessionReady,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<string>? ReusedLibs = null,
    IReadOnlyList<string>? StructureChecks = null);

/// <summary>流水线派生入参（S2e §14 修复一：轻量命名派生接口）。</summary>
/// <param name="TemplateRepoName">模板名（首个 <c>-</c> 前段为模板代号）。</param>
/// <param name="NewCode">新项目代号。</param>
/// <param name="Ends">三端配置（缺省 = 三端全开；仅 <c>On</c> 的端出派生项；backend 的 <c>Port</c> 参与派生）。</param>
public sealed record DeriveRequest(
    string TemplateRepoName,
    string NewCode,
    IReadOnlyList<InitEndConfig>? Ends = null);

/// <summary>单端派生结果（S2e §14）。</summary>
/// <param name="End">端名（mobile / admin / backend）。</param>
/// <param name="From">真实模板流水线名（<c>ListPipelinesAsync</c> 按母版后缀段反查）；反查不到为 <c>null</c>。</param>
/// <param name="To">派生名（带真实前缀；From 为 null 时为 <c>DEV_Deploy_{代号}-{端}</c>，<b>无前导下划线</b>）。</param>
public sealed record PipelineDeriveItem(string End, string? From, string To);

/// <summary>流水线派生出参。</summary>
/// <param name="Items">逐端派生项。</param>
public sealed record PipelineDeriveResult(IReadOnlyList<PipelineDeriveItem> Items);

/// <summary>
/// 云效项目初始化服务（Part1：清单 + 预检；执行编排批次 C 追加）。
/// </summary>
public sealed class YunxiaoInitService
{
    /// <summary>母版库名后缀（与 <see cref="NamingDeriver"/> 同口径）。</summary>
    private const string TemplateRepoSuffix = "-Mixed";

    /// <summary>
    /// 兜底克隆地址主机（<b>待实测</b>）：<c>HttpUrlToRepo</c> 缺失时按设计
    /// <c>https://{Endpoint}/{path}.git</c> 拼装。真实形态先例
    /// <c>https://codeup.aliyun.com/&lt;组织路径&gt;/&lt;组&gt;/&lt;库&gt;.git</c>
    /// （见 LubanAgent.Plugin.NewProject/docs/pipeline-*.yml.md）；组织路径段待实机核对（R-S2-1 仅覆盖会话通路）。
    /// </summary>
    private const string FallbackGitHost = "codeup.aliyun.com";

    /// <summary>翻页每页条数（clamp 上限；CodeupClient 不截断，本服务自行 clamp）。</summary>
    private const int PerPage = 100;

    /// <summary>翻页页号上限（防 Total 虚标死循环）。</summary>
    private const int MaxPage = 150;

    private readonly ICodeupClient _codeup;
    private readonly IFlowClient _flow;
    private readonly ISessionGateway _gateway;
    private readonly InitTaskStore _taskStore;
    private readonly IYunxiaoSettingsService _settings;
    private readonly IWorkspaceDirectoryProvider _workspaces;
    private readonly ProjectInitStore? _projectInitStore;

    /// <summary>创建云效项目初始化服务。</summary>
    /// <param name="codeup">Codeup 客户端（接口注入，单测可替换）。</param>
    /// <param name="flow">Flow 客户端（FR-3 流水线预览/提交用，批次 D 接线）。</param>
    /// <param name="gateway">会话通路收口。</param>
    /// <param name="taskStore">任务态落盘（执行/重跑用，批次 C 接线）。</param>
    /// <param name="settings">云效连接配置（组织 ID 读取）。</param>
    /// <param name="workspaces">工作区目录提供者。</param>
    /// <param name="projectInitStore">项目初始化完成登记（可空：单测不接线时登记接口返回 500）。</param>
    public YunxiaoInitService(
        ICodeupClient codeup,
        IFlowClient flow,
        ISessionGateway gateway,
        InitTaskStore taskStore,
        IYunxiaoSettingsService settings,
        IWorkspaceDirectoryProvider workspaces,
        ProjectInitStore? projectInitStore = null)
    {
        _codeup = codeup ?? throw new ArgumentNullException(nameof(codeup));
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _projectInitStore = projectInitStore;
    }

    /// <summary>
    /// 项目初始化完成登记：代码库任务收口 + 三端流水线就绪后落盘，
    /// 供项目列表判定「已初始化」。
    /// </summary>
    /// <param name="request">登记入参（项目 ID / 标识至少一个）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功载荷恒为 <c>true</c>（400 缺键）。</returns>
    public async Task<YunxiaoWorkbenchResult<bool>> SaveProjectInitAsync(ProjectInitRequest request, CancellationToken ct)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.ProjectId) && string.IsNullOrWhiteSpace(request.Identifier)))
        {
            return YunxiaoWorkbenchResult<bool>.Failure(400, "项目 ID / 项目标识至少提供一个");
        }

        if (string.IsNullOrWhiteSpace(request.Codename))
        {
            return YunxiaoWorkbenchResult<bool>.Failure(400, "项目代号不能为空");
        }

        if (_projectInitStore is null)
        {
            return YunxiaoWorkbenchResult<bool>.Failure(500, "初始化登记存储未接线");
        }

        await _projectInitStore.UpsertAsync(new ProjectInitRecord
        {
            ProjectId = request.ProjectId?.Trim() ?? string.Empty,
            Identifier = string.IsNullOrWhiteSpace(request.Identifier) ? null : request.Identifier!.Trim(),
            Codename = request.Codename.Trim(),
            WorkspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? null : request.WorkspaceId.Trim(),
            TemplateCode = string.IsNullOrWhiteSpace(request.TemplateCode) ? null : request.TemplateCode!.Trim(),
            SelectedRepos = (request.SelectedRepos ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .ToList(),
            GroupName = string.IsNullOrWhiteSpace(request.GroupName) ? null : request.GroupName!.Trim(),
            Branch = string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch!.Trim(),
            RepoName = string.IsNullOrWhiteSpace(request.RepoName) ? null : request.RepoName!.Trim(),
            CompletedAt = DateTime.Now,
            Pipelines = (request.Ends ?? Array.Empty<ProjectInitEnd>())
                .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.End))
                .Select(e => new ProjectInitPipeline
                {
                    End = e.End.Trim(),
                    Enabled = e.Enabled,
                    Name = e.Name,
                    TemplateName = e.TemplateName,
                    Dir = string.IsNullOrWhiteSpace(e.Dir) ? null : e.Dir.Trim(),
                    Port = e.Port,
                    PipelineId = e.PipelineId,
                })
                .ToList(),
        }).ConfigureAwait(false);

        return YunxiaoWorkbenchResult<bool>.Success(true);
    }

    /// <summary>
    /// 反查项目初始化完成登记（已初始化项目再次进入初始化页时反显配置）。
    /// </summary>
    /// <param name="projectId">云效项目 ID。</param>
    /// <param name="identifier">项目标识。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>登记（未登记时 <c>Data</c> 为 null）。</returns>
    public async Task<YunxiaoWorkbenchResult<ProjectInitRecordDto?>> GetProjectInitAsync(
        string? projectId, string? identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(identifier))
        {
            return YunxiaoWorkbenchResult<ProjectInitRecordDto?>.Failure(400, "项目 ID / 项目标识至少提供一个");
        }

        if (_projectInitStore is null)
        {
            return YunxiaoWorkbenchResult<ProjectInitRecordDto?>.Failure(500, "初始化登记存储未接线");
        }

        var record = await _projectInitStore.FindAsync(projectId, identifier).ConfigureAwait(false);
        if (record is null)
        {
            return YunxiaoWorkbenchResult<ProjectInitRecordDto?>.Success(null);
        }

        return YunxiaoWorkbenchResult<ProjectInitRecordDto?>.Success(new ProjectInitRecordDto(
            record.ProjectId,
            record.Identifier,
            record.Codename,
            record.TemplateCode,
            record.SelectedRepos,
            record.GroupName,
            record.Branch,
            record.RepoName,
            record.CompletedAt,
            record.Pipelines.Select(p => new ProjectInitEnd(
                p.End, p.Name, p.PipelineId, p.Enabled, p.Port, p.TemplateName, p.Dir)).ToList(),
            record.WorkspaceId));
    }

    /// <summary>
    /// 项目初始化清单（只读）：模板库（自定义模板接口全量翻页聚合）+ 代码组 + 工作区 + 会话通路探测。
    /// </summary>
    /// <remarks>
    /// 模板源口径（S2b §11.1）：走 <c>ListTemplateRepositoriesAsync(templateType:1)</c> 模板登记真值
    /// （含单端与综合模板，描述自带停用说明），不再按名搜索 <c>-Mixed</c> 后缀筛选。
    /// 翻页口径：<c>perPage</c> clamp 100、<c>page</c> 递增直到空页（本接口裸数组无 x-total 合成载体），
    /// 跨页按 (ProjectId, Name) 合并去重、本页零新增即截止，page 上限 <see cref="MaxPage"/> 截断防死循环。
    /// 代码组翻页口径同前（见 <see cref="ListAllNamespacesAsync"/>）。
    /// </remarks>
    /// <param name="ct">取消令牌。</param>
    /// <returns>清单。</returns>
    public async Task<ManifestsResult> GetManifestsAsync(CancellationToken ct)
    {
        var orgId = _settings.GetOrganizationId();
        var repos = new List<ManifestRepo>();
        var seen = new HashSet<(long, string?)>();

        for (var page = 1; page <= MaxPage; page++)
        {
            var items = await _codeup
                .ListTemplateRepositoriesAsync(1, page, PerPage, orgId, ct)
                .ConfigureAwait(false);
            if (items is null || items.Count == 0)
            {
                break;
            }

            var added = 0;
            foreach (var template in items)
            {
                if (template is null)
                {
                    continue;
                }

                if (seen.Add((template.ProjectId, template.Name)))
                {
                    repos.Add(new ManifestRepo(template.Name ?? "", template.Description, template.ProjectId));
                    added++;
                }
            }

            if (added == 0)
            {
                break;
            }
        }

        var namespaces = await ListAllNamespacesAsync(orgId, ct).ConfigureAwait(false);
        var groups = namespaces
            .Select(n => new ManifestGroup(n.Id, n.Name ?? ""))
            .ToList();

        var sessionReady = await ProbeSessionAsync(ct).ConfigureAwait(false);

        return new ManifestsResult(repos, groups, _workspaces.List(), sessionReady);
    }

    /// <summary>
    /// 预检（<b>零副作用</b>：不建组、不建库、不发会话、不动流水线）：
    /// 命名派生 + git 命令序列 + 冲突检查（工作区目录被占用 / 后端端口占用）+ 复用标记（S2b §11.2）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 壳层映射（批次 D）：<see cref="YunxiaoWorkbenchResult{T}.Ok"/> → <c>ApiEnvelope.Ok(Data)</c>；
    /// 非法参数按 <c>Code=400</c> → <c>WebApi.FailJson(400, Message)</c>；会话不可用不在此处报错，
    /// 以 <see cref="PreflightResult.SessionReady"/> 标记（存在需新建的库时前端禁执行，批次 C Exec 再拒绝）。
    /// </para>
    /// <para>
    /// 复用语义（S2b §11.2）：新库名已存在 → 不再列 <c>Conflicts</c>，转
    /// <see cref="PreflightResult.ReusedLibs"/> 标记（提示「将复用已有库」）；复用库不生成 git 步骤；
    /// 综合模板复用库做三子目录结构校验（本地未克隆 / 缺子目录出 Warnings「结构待校验」，不阻断）。
    /// 命名派生按 S2b §11.1：代号前缀替换（<see cref="NamingDeriver.RenameByCode"/>），
    /// 三子目录仅综合模板（<c>-Mixed</c> 后缀）派生。
    /// </para>
    /// <para>
    /// URL 口径：<c>HttpUrlToRepo</c> 优先；缺失按 <c>https://{Endpoint}/{path}.git</c> 兜底（待实测）。
    /// 库名查重与模板定位走 <see cref="FindRepoByNameAsync"/>（搜索翻页 + Name/Path 精确匹配；
    /// 实机证实 <c>GET .../repositories/{repoId}</c> 只认路径 id，按名查 404）。
    /// </para>
    /// </remarks>
    /// <param name="request">预检入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>预检结论（含 Code/Message 的结果壳）。</returns>
    public async Task<YunxiaoWorkbenchResult<PreflightResult>> PreflightAsync(
        PreflightRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.TemplateRepoName)
            || string.IsNullOrWhiteSpace(request.NewCode)
            || string.IsNullOrWhiteSpace(request.WorkspaceId)
            || string.IsNullOrWhiteSpace(request.Branch))
        {
            return YunxiaoWorkbenchResult<PreflightResult>.Failure(400, "母版库名 / 新代号 / 工作区 / 分支均不能为空");
        }

        var templateName = request.TemplateRepoName.Trim();
        var newCode = request.NewCode.Trim();

        var templateCode = NamingDeriver.ParseTemplateCode(templateName);
        if (templateCode is null)
        {
            return YunxiaoWorkbenchResult<PreflightResult>.Failure(400, "模板名必须含代号段（首个 - 之前为模板代号）");
        }

        var workspace = _workspaces.List().FirstOrDefault(w =>
            string.Equals(w.Id, request.WorkspaceId.Trim(), StringComparison.Ordinal));
        if (workspace is null)
        {
            return YunxiaoWorkbenchResult<PreflightResult>.Failure(400, "工作区不存在");
        }

        // 综合模板（-Mixed 后缀）才派生三子目录改名候选；库名走代号前缀替换口径
        var isMixedTemplate = templateName.EndsWith(TemplateRepoSuffix, StringComparison.Ordinal);

        // 后端端口（S2c §12.3）：后端流水线派生名增 (端口) 后缀；端口盒值参与派生
        var backendPortForName = request.Ends?
            .FirstOrDefault(e => string.Equals(e.End, "backend", StringComparison.OrdinalIgnoreCase) && e.On)
            ?.Port;

        var orgId = _settings.GetOrganizationId();

        // S2d §13.3 服务端权威：母版流水线名缺省（或未覆盖某启用端）时，从 ListPipelinesAsync
        // 反查母版真实名（按后缀段 _DEV_Deploy_{TemplateCode}-{EndSuffix} 比对、前缀容忍）再派生
        // → Plan.Pipelines 带真实前缀，预检回执即正确名（前端据此回填 F6）。
        // 前缀不写死：分组前缀取自反查到的真实名本身，缺省名场景下用空串派生会丢前缀（§13.3 根因）。
        var templatePipelineNames = (request.TemplatePipelineNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .ToList();
        var scanEnds = request.Ends is null
            ? new[] { new InitEndConfig("mobile", true), new InitEndConfig("admin", true), new InitEndConfig("backend", true) }
            : request.Ends.Where(e => e is not null && e.On);
        List<YunxiaoPipeline>? pipelineCatalog = null;
        foreach (var end in scanEnds)
        {
            var endSuffix = "-" + EndSuffix(end.End);
            var covered = templatePipelineNames.Any(n =>
                NamingDeriver.StripPortSuffix(n).EndsWith(endSuffix, StringComparison.OrdinalIgnoreCase));
            if (covered)
            {
                continue;
            }

            pipelineCatalog ??= await _flow
                .ListPipelinesAsync(organizationId: orgId, cancellationToken: ct).ConfigureAwait(false);
            var realName = FindRealTemplatePipelineName(pipelineCatalog, templateCode, end.End);
            if (realName is not null)
            {
                templatePipelineNames.Add(realName);
            }
        }

        NamingPlan plan;
        try
        {
            plan = NamingDeriver.Derive(
                templateCode,
                newCode,
                templatePipelineNames,
                includeDirs: isMixedTemplate,
                backendPort: backendPortForName);
            plan = plan with
            {
                RepoName = NamingDeriver.RenameByCode(templateName, templateCode, newCode),
            };
        }
        catch (ArgumentException ex)
        {
            return YunxiaoWorkbenchResult<PreflightResult>.Failure(400, ex.Message);
        }

        var conflicts = new List<string>();
        var warnings = new List<string>();
        var reusedLibs = new List<string>();

        // 复用判定（S2b §11.2）：新库名已存在 → 复用标记，不列冲突
        var existing = await FindRepoByNameAsync(plan.RepoName, orgId, ct).ConfigureAwait(false);
        var reused = existing is not null;
        if (reused)
        {
            reusedLibs.Add(plan.RepoName);
        }

        // 检出目标 {workspaceRoot}/{新库名}（S2c §12.1.2 幂等判据：已有源码 → 零 git 步骤；
        // S2e §14 修复三：LocalSourceLayout 三形态判定——根即检出 / 根含 .git 也算已有源码）
        var targetDir = Path.Combine(workspace.RootPath, plan.RepoName);
        var localProbe = LocalSourceLayout.Detect(workspace.RootPath, plan.RepoName);
        var hasLocalSource = localProbe.HasSource;

        IReadOnlyList<GitStep> gitCommands;
        if (hasLocalSource)
        {
            // 目录已有源码 → 零 git 步骤（幂等，S2c §12.1.2）
            gitCommands = [];
        }
        else if (reused)
        {
            // 复用库 + 本地无源码 → 本地检出计划（S2c §12.1.2）：clone 复用库真 URL → 切 develop
            var reuseUrl = !string.IsNullOrWhiteSpace(existing!.HttpUrlToRepo)
                ? existing!.HttpUrlToRepo!
                : BuildFallbackHttpsUrl(existing!.Path ?? plan.RepoName);
            gitCommands = GitCommandPlanner.PlanLocalCheckout(
                workspace.RootPath,
                reuseUrl,
                plan.RepoName,
                request.Branch.Trim());
        }
        else
        {
            // 新建库 + 本地无源码 → 原 clone-rename-push 序列 + 尾部「切 develop」步（S2c §12.2）
            // 母版库信息：取 HttpUrlToRepo 当克隆地址（按名查找走搜索翻页 + 精确匹配）
            var template = await FindRepoByNameAsync(templateName, orgId, ct).ConfigureAwait(false);
            var templateRepoUrl = !string.IsNullOrWhiteSpace(template?.HttpUrlToRepo)
                ? template!.HttpUrlToRepo!
                : BuildFallbackHttpsUrl(template?.Path ?? templateName);

            // 新库尚不存在：克隆地址按「组/库」形态兜底拼装（待实测）
            var newRepoPath = string.IsNullOrWhiteSpace(request.GroupNewName)
                ? plan.RepoName
                : $"{request.GroupNewName.Trim()}/{plan.RepoName}";
            var newRepoUrl = BuildFallbackHttpsUrl(newRepoPath);

            // 序列与执行同源（GitCommandPlanner 单一入口，防展示≠执行）
            var steps = GitCommandPlanner.PlanCloneRenamePush(
                workspace.RootPath,
                templateRepoUrl,
                newRepoUrl,
                templateCode,
                newCode,
                plan.Dirs.Select(d => d.To).ToList(),
                request.Branch.Trim()).ToList();
            steps.Add(GitCommandPlanner.PlanSwitchDevelopLocal(targetDir));
            gitCommands = steps;
        }

        var structureChecks = new List<string>();
        if (reused)
        {
            // 结构校验（S2e §14 修复四）：复用库无改名动作 → 不输出「将改名目录」对照（Plan.Dirs 清空），
            // 改输出「结构校验」子目录存在性结果 / 待校验；本地未克隆 / 缺子目录仍出
            // 「结构待校验」Warnings（不阻断）。源码目录随 LocalSourceLayout 三形态探测（修复三）。
            if (isMixedTemplate)
            {
                if (localProbe.HasSource && localProbe.SourceDir is not null)
                {
                    foreach (var dir in plan.Dirs)
                    {
                        var exists = Directory.Exists(Path.Combine(localProbe.SourceDir, dir.To));
                        structureChecks.Add($"{dir.To}：{(exists ? "存在" : "缺失")}");
                    }

                    var missing = plan.Dirs
                        .Select(d => d.To)
                        .Where(name => !Directory.Exists(Path.Combine(localProbe.SourceDir, name)))
                        .ToList();
                    if (missing.Count > 0)
                    {
                        warnings.Add($"结构待校验：缺少子目录 {string.Join("、", missing)}");
                    }
                }
                else
                {
                    structureChecks.Add($"本地未克隆 {plan.RepoName}：子目录待校验");
                    warnings.Add($"结构待校验：本地未克隆 {plan.RepoName}");
                }
            }
            else
            {
                structureChecks.Add("单端库：无子目录结构段");
            }

            plan = plan with { Dirs = [] };
        }
        else
        {
            // 冲突①：工作区目录被占用（{RootPath}/{新库名}；仅新建库需要 clone 落位）。
            // S2c §12.1.2 幂等：目录已有源码 → 零 git 步骤（不视为冲突）；仅「目录存在但无源码」才冲突。
            if (!hasLocalSource && Directory.Exists(targetDir))
            {
                conflicts.Add($"工作区目录已存在：{plan.RepoName}");
            }
        }

        // 冲突②：后端端口占用（仅后端端勾选时检查）
        var backendPort = request.Ends?
            .FirstOrDefault(e => string.Equals(e.End, "backend", StringComparison.OrdinalIgnoreCase) && e.On)
            ?.Port;
        if (backendPort is int port && IsPortBusy(port))
        {
            conflicts.Add($"后端端口已被占用：{port}");
        }

        var sessionReady = await ProbeSessionAsync(ct).ConfigureAwait(false);

        return YunxiaoWorkbenchResult<PreflightResult>.Success(
            new PreflightResult(plan, gitCommands, conflicts, sessionReady, warnings, reusedLibs, structureChecks));
    }

    /// <summary>
    /// 流水线命名派生（S2e §14 修复一，轻量只读）：逐端反查母版真实流水线名 → 派生新名。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>零副作用</b>：只调 <c>ListPipelinesAsync</c> 反查（S2d §13.3 同口径，复用
    /// <see cref="FindRealTemplatePipelineName"/>——前缀不写死，从真实名自身解析）。
    /// From = 反查到的真实模板流水线名，反查不到为 <c>null</c>；
    /// To = <see cref="NamingDeriver.DerivePipelineName"/>（From 非空且带非空前缀 → 带真实前缀只换代号段；
    /// 否则 <c>DEV_Deploy_{代号}-{端}</c>——<b>无前导下划线</b>，根因修复：空前缀拼 <c>_DEV_Deploy_</c>
    /// 产生前导下划线）；后端端按 Ends 的 Port 增 <c>(端口)</c> 后缀（S2c §12.3 同口径）。
    /// </para>
    /// <para>
    /// 壳层映射（GET /yunxiao/pipelinederive）：400 = 母版库名 / 新代号非法；
    /// 仅 <c>On</c> 的端出派生项，<c>Ends</c> 缺省 = 三端全开。
    /// </para>
    /// </remarks>
    /// <param name="request">派生入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>逐端派生结果（含 Code/Message 的结果壳）。</returns>
    public async Task<YunxiaoWorkbenchResult<PipelineDeriveResult>> DerivePipelinesAsync(
        DeriveRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.TemplateRepoName)
            || string.IsNullOrWhiteSpace(request.NewCode))
        {
            return YunxiaoWorkbenchResult<PipelineDeriveResult>.Failure(400, "母版库名 / 新代号不能为空");
        }

        var templateName = request.TemplateRepoName.Trim();
        var newCode = request.NewCode.Trim();

        var templateCode = NamingDeriver.ParseTemplateCode(templateName);
        if (templateCode is null)
        {
            return YunxiaoWorkbenchResult<PipelineDeriveResult>.Failure(400, "模板名必须含代号段（首个 - 之前为模板代号）");
        }

        try
        {
            NamingDeriver.Derive(templateCode, newCode, []);
        }
        catch (ArgumentException ex)
        {
            return YunxiaoWorkbenchResult<PipelineDeriveResult>.Failure(400, ex.Message);
        }

        var ends = (IReadOnlyList<InitEndConfig>)(request.Ends is null
            ? [new InitEndConfig("mobile", true), new InitEndConfig("admin", true), new InitEndConfig("backend", true)]
            : request.Ends.Where(e => e is not null && e.On).ToList());

        var orgId = _settings.GetOrganizationId();
        List<YunxiaoPipeline>? catalog = null;
        var items = new List<PipelineDeriveItem>();
        foreach (var end in ends)
        {
            catalog ??= await _flow
                .ListPipelinesAsync(organizationId: orgId, cancellationToken: ct).ConfigureAwait(false);

            var from = FindRealTemplatePipelineName(catalog, templateCode, end.End);
            var port = string.Equals(end.End, "backend", StringComparison.OrdinalIgnoreCase)
                ? end.Port
                : null;
            var to = NamingDeriver.DerivePipelineName(from, newCode, "-" + EndSuffix(end.End), port);
            items.Add(new PipelineDeriveItem(end.End, from, to));
        }

        return YunxiaoWorkbenchResult<PipelineDeriveResult>.Success(new PipelineDeriveResult(items));
    }

    /// <summary>
    /// 按库名精确查仓库（预检冲突 / TOCTOU 查重 / 模板定位共用）；查不到返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 实机口径（S2 验证）：Codeup 的 <c>GET .../repositories/{repoId}</c> 只认路径 id，
    /// 按库名调 <c>GetRepositoryAsync</c> 返回 404，且 <c>CodeupClient</c> 对 404 抛
    /// <see cref="YunxiaoApiException"/>——调用方期望「查不到返回 null」，故按名查询
    /// 统一改走本助手：<c>SearchRepositoriesAsync</c>（其 <c>search</c> 是模糊关键字，
    /// 相似名一并返回）翻页 + <c>Name</c>/<c>Path</c> 精确匹配（OrdinalIgnoreCase）。
    /// 翻页口径：perPage=100、page 递增、命中即返、上限 <see cref="MaxPage"/> 或 x-total 用尽；
    /// <see cref="YunxiaoApiException"/> 404 与空结果归一为 null，其余异常向上抛。
    /// 不改 <c>GetRepositoryAsync</c> 既有语义（按 id 查询的其它消费方依赖其抛异常）。
    /// </remarks>
    /// <param name="name">库名（Name 或 Path 精确等值命中）。</param>
    /// <param name="orgId">组织 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>命中仓库；不存在为 <c>null</c>。</returns>
    private async Task<YunxiaoRepository?> FindRepoByNameAsync(string name, string? orgId, CancellationToken ct)
    {
        var fetched = 0;
        for (var page = 1; page <= MaxPage; page++)
        {
            YunxiaoPagedResult<YunxiaoRepository>? result;
            try
            {
                result = await _codeup
                    .SearchRepositoriesAsync(name, page, PerPage, null, null, null, orgId, ct)
                    .ConfigureAwait(false);
            }
            catch (YunxiaoApiException ex) when (ex.ErrorCode == 404)
            {
                return null;
            }

            var items = result?.Items;
            if (items is null || items.Count == 0)
            {
                return null;
            }

            var hit = items.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Path, name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return hit;
            }

            fetched += items.Count;
            if (result!.Total > 0 && fetched >= result.Total)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// 代码组全量翻页聚合（<c>groups</c> 不再单页截断），跨页按 <c>Id</c> 合并去重。
    /// </summary>
    /// <remarks>
    /// <c>ListNamespacesAsync</c> 是裸数组返回（无 x-total 合成载体），翻页终点按
    /// 「空页 / 本页零新增 Id」判定（防页数据漂移死循环），page 上限同
    /// <see cref="MaxPage"/>，<c>perPage</c> 同 <see cref="PerPage"/>。
    /// </remarks>
    /// <param name="orgId">组织 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>全量代码组（按 Id 去重）。</returns>
    private async Task<List<YunxiaoNamespace>> ListAllNamespacesAsync(string? orgId, CancellationToken ct)
    {
        var merged = new List<YunxiaoNamespace>();
        var seen = new HashSet<long>();
        for (var page = 1; page <= MaxPage; page++)
        {
            var items = await _codeup
                .ListNamespacesAsync(null, null, page, PerPage, null, null, orgId, ct)
                .ConfigureAwait(false);
            if (items is null || items.Count == 0)
            {
                break;
            }

            var added = 0;
            foreach (var ns in items)
            {
                if (seen.Add(ns.Id))
                {
                    merged.Add(ns);
                    added++;
                }
            }

            if (added == 0)
            {
                break;
            }
        }

        return merged;
    }

    /// <summary>会话通路轻量探测（失败按不可用处理，不抛）。</summary>
    private async Task<bool> ProbeSessionAsync(CancellationToken ct)
    {
        try
        {
            return await _gateway.ProbeAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>兜底 HTTPS 克隆地址（形态待实测，见 <see cref="FallbackGitHost"/>）。</summary>
    private static string BuildFallbackHttpsUrl(string path)
        => $"https://{FallbackGitHost}/{path}.git";

    /// <summary>
    /// 本地是否已有源码检出（S2c §12.1.2 幂等判据；S2e §14 修复三改走
    /// <see cref="LocalSourceLayout.Detect"/> 三形态判定：子目录 / 根即检出 / 根含 .git）。
    /// </summary>
    /// <remarks>
    /// 「不存在或为空」→ 需 git 检出（复用库 <c>PlanLocalCheckout</c> / 新建库 clone-rename-push + 切 develop）；
    /// 「已有源码」→ 零 git 步骤（幂等，重跑不再 clone）。空目录按「无源码」处理（可安全 clone 落位）。
    /// </remarks>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="repoName">目标库名。</param>
    /// <returns>命中任一检出形态时为 <c>true</c>。</returns>
    private static bool HasLocalSource(string workspaceRoot, string repoName)
        => LocalSourceLayout.HasSource(workspaceRoot, repoName);

    /// <summary>
    /// 流水线名「前缀容忍」匹配（S2c §12.3 + S2d §13.3）：剥离尾部 <c>(端口)</c> 后按
    /// <c>_DEV_Deploy_</c> 后缀段比对（<see cref="NamingDeriver.MatchesPipelineName"/>）。
    /// </summary>
    /// <remarks>
    /// <c>(7001)</c> 与 <c>(9000)</c> 互认命中（端口仅展示，不参与命中判据）；
    /// 表单空前缀 <c>_DEV_Deploy_{代号}-{端}</c> 与真名 <c>ACME_DEV_Deploy_{代号}-{端}</c> 互认
    /// （后缀段从两侧名字各自解析，前缀不写死）。任一为空即不命中。
    /// 用于 Exists 标注与 create 前 TOCTOU 复查。
    /// </remarks>
    private static bool SamePipelineName(string? a, string? b)
        => NamingDeriver.MatchesPipelineName(a, b);

    /// <summary>端口是否被占用（活动 TCP 监听）。</summary>
    private static bool IsPortBusy(int port)
        => IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(ep => ep.Port == port);

    // ═══════════════════════════════════════════════════════════════════════
    // Part2（批次 C）：执行 / 任务 / 重跑 + FR-3 流水线预览 / 提交
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>单库执行计划（派生结果缓存，后台推进用）。</summary>
    private sealed record ExecLibPlan(string TemplateRepoName, string TemplateCode, string NewRepoName);

    /// <summary>一次执行的上下文（重跑复用；宿主重启后失效——见 <see cref="RetryAsync"/>）。</summary>
    private sealed record ExecRunContext(
        string NewCode,
        string WorkspaceId,
        string Branch,
        string? GroupNewName,
        string? Visibility,
        string? Description,
        IReadOnlyList<ExecLibPlan> Libs);

    /// <summary>执行上下文留存（按 taskId；重跑派生命名与建库参数用）。</summary>
    private readonly ConcurrentDictionary<string, ExecRunContext> _runContexts = new();

    /// <summary>
    /// PAT 形态 token（<c>pt-</c> 前缀）剥离正则：不认字母数字前导（防误伤 <c>opt-</c> 等）。
    /// </summary>
    private static readonly Regex PatTokenPattern = new(
        @"(?<![A-Za-z0-9])pt-[A-Za-z0-9._\-]+", RegexOptions.Compiled);

    /// <summary>
    /// 执行：建组建库 + 会话 git 逐库推进（<b>秒回 taskId</b>，后台 <c>Task.Run</c> 推进）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 壳层映射（批次 D）：<see cref="YunxiaoWorkbenchResult{T}.Ok"/> → <c>ApiEnvelope.Ok(Data)</c>；
    /// 参数非法 <c>Code=400</c>、同代号进行中 <c>Code=409</c>、存在需新建的库且会话通路不可用 <c>Code=503</c>
    /// （全复用不检查会话，S2b §11.2；壳层映射 <c>WebApi.FailJson(code, message)</c>）。
    /// </para>
    /// <para>
    /// 状态机（逐库独立，互不影响；后台异常永不逃逸出线程池）：
    /// <c>pending → cloning → renaming → pushing → done / failed</c>；
    /// 会话 git 为「一次初始化一个会话、多库共用」，整序列单指令下发（D2 不做逐条进度），
    /// 回执确认后按序列阶段顺序收口中间态；全部库终态后置 <c>FinishedAt</c> + 任务态
    /// （全 done → done，含 failed → failed）。
    /// </para>
    /// </remarks>
    /// <param name="request">执行入参（预检确认后的完整清单）。</param>
    /// <param name="ct">取消令牌（仅同步校验段生效；后台推进不随请求取消）。</param>
    /// <returns>任务标识。</returns>
    public async Task<YunxiaoWorkbenchResult<string>> ExecAsync(ExecRequest request, CancellationToken ct)
    {
        // ① 参数校验（400 语义）
        if (request is null
            || string.IsNullOrWhiteSpace(request.NewCode)
            || string.IsNullOrWhiteSpace(request.WorkspaceId)
            || string.IsNullOrWhiteSpace(request.Branch)
            || request.Libs is null
            || request.Libs.Count == 0
            || request.Libs.Any(l => l is null
                || string.IsNullOrWhiteSpace(l.TemplateRepoName)
                || (l.NewRepoName is not null && string.IsNullOrWhiteSpace(l.NewRepoName))))
        {
            return YunxiaoWorkbenchResult<string>.Failure(400, "母版库 / 新代号 / 工作区 / 分支均不能为空");
        }

        var newCode = request.NewCode.Trim();
        var plans = new List<ExecLibPlan>();
        foreach (var lib in request.Libs)
        {
            var templateName = lib.TemplateRepoName.Trim();
            var templateCode = NamingDeriver.ParseTemplateCode(templateName);
            if (templateCode is null)
            {
                return YunxiaoWorkbenchResult<string>.Failure(400, "模板名必须含代号段（首个 - 之前为模板代号）");
            }

            // 新库名缺省按代号前缀替换派生（TPLX-Mixed→NEWX-Mixed、TPLX-Service→NEWX-Service）
            string repoName;
            try
            {
                repoName = string.IsNullOrWhiteSpace(lib.NewRepoName)
                    ? NamingDeriver.RenameByCode(templateName, templateCode, newCode)
                    : lib.NewRepoName!.Trim();
            }
            catch (ArgumentException ex)
            {
                return YunxiaoWorkbenchResult<string>.Failure(400, ex.Message);
            }

            plans.Add(new ExecLibPlan(templateName, templateCode, repoName));
        }

        try
        {
            NamingDeriver.Derive(plans[0].TemplateCode, newCode, []);
        }
        catch (ArgumentException ex)
        {
            return YunxiaoWorkbenchResult<string>.Failure(400, ex.Message);
        }

        var workspace = _workspaces.List().FirstOrDefault(w =>
            string.Equals(w.Id, request.WorkspaceId.Trim(), StringComparison.Ordinal));
        if (workspace is null)
        {
            return YunxiaoWorkbenchResult<string>.Failure(400, "工作区不存在");
        }

        // ② 会话通路（503 语义；壳层映射 WebApi.FailJson(503, …)）
        // S2c §12.2 门禁口径（收紧 S2b §11.2）：仅当「全部库已有本地源码检出」才免会话；
        // 只要有一个库要 clone/checkout（本地无源码）就要求 sessionReady——
        // 全复用但无本地源码时同样需要会话（复用库发 PlanLocalCheckout）。
        if (!await ProbeSessionAsync(ct).ConfigureAwait(false))
        {
            var anyGitWork = plans.Any(p =>
                !HasLocalSource(workspace.RootPath, p.NewRepoName));
            if (anyGitWork)
            {
                return YunxiaoWorkbenchResult<string>.Failure(503, "工作区会话通路不可用，无法执行 git 动作");
            }
        }

        // ③ 同代号互斥（409 语义：同代号进行中）
        if (!await _taskStore.TryBeginAsync(newCode).ConfigureAwait(false))
        {
            return YunxiaoWorkbenchResult<string>.Failure(409, "该项目代号已有进行中的初始化任务");
        }

        // ④ 占位补全（批次 B 口径：TryBegin 已写占位，经 ListByCodenameAsync 找无 FinishedAt 条目补全）
        var task = (await _taskStore.ListByCodenameAsync(newCode).ConfigureAwait(false))
            .FirstOrDefault(t => t.FinishedAt is null);
        if (task is null)
        {
            return YunxiaoWorkbenchResult<string>.Failure(500, "任务占位缺失");
        }

        task.Libs = plans.Select(p => new LibState { Repo = p.NewRepoName, Status = LibStatus.Pending }).ToList();
        task.WorkspaceId = request.WorkspaceId.Trim();
        await _taskStore.UpsertAsync(task).ConfigureAwait(false);

        _runContexts[task.Id] = new ExecRunContext(
            newCode,
            task.WorkspaceId,
            request.Branch.Trim(),
            request.GroupNewName?.Trim(),
            request.Visibility,
            request.Description,
            plans);

        // ⑤ 后台推进：秒回 taskId，不等完成（不受 HTTP 超时约束）
        _ = Task.Run(() => RunInitAsync(task.Id, null, reuseExisting: false), CancellationToken.None);
        return YunxiaoWorkbenchResult<string>.Success(task.Id);
    }

    /// <summary>按任务标识取任务态（前端 2s 轮询）；找不到 → 404 语义。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>任务态。</returns>
    public async Task<YunxiaoWorkbenchResult<InitTask>> GetTaskAsync(string taskId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return YunxiaoWorkbenchResult<InitTask>.Failure(400, "任务标识不能为空");
        }

        var task = await _taskStore.GetAsync(taskId.Trim()).ConfigureAwait(false);
        return task is null
            ? YunxiaoWorkbenchResult<InitTask>.Failure(404, "任务不存在")
            : YunxiaoWorkbenchResult<InitTask>.Success(task);
    }

    /// <summary>
    /// 重跑指定失败库（幂等复用同任务：目标库清回 pending 重新推进、FinishedAt 置空再收口；
    /// 非 failed 库不动；找不到任务/库 → 404 语义）。
    /// </summary>
    /// <remarks>
    /// 幂等口径：重跑对「已存在新库」走复用（不再建库）——首跑已建库、git 段失败的库可继续推；
    /// 重复 Retry 遇非 failed 库直接回执、零调用。
    /// </remarks>
    /// <param name="taskId">任务标识。</param>
    /// <param name="repoNames">要重跑的库名清单。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>任务标识（复用原任务）。</returns>
    public async Task<YunxiaoWorkbenchResult<string>> RetryAsync(string taskId, IReadOnlyList<string> repoNames, CancellationToken ct)
    {
        // ① 参数校验（400 语义）
        if (string.IsNullOrWhiteSpace(taskId)
            || repoNames is null
            || repoNames.Count == 0
            || repoNames.Any(string.IsNullOrWhiteSpace))
        {
            return YunxiaoWorkbenchResult<string>.Failure(400, "任务标识 / 重跑库清单不能为空");
        }

        var task = await _taskStore.GetAsync(taskId.Trim()).ConfigureAwait(false);
        if (task is null)
        {
            return YunxiaoWorkbenchResult<string>.Failure(404, "任务不存在");
        }

        // ② 库存在性（404 语义）；只收集当前 failed 的目标库（非 failed 不动）
        var targets = new List<LibState>();
        foreach (var name in repoNames)
        {
            var lib = task.Libs.FirstOrDefault(l =>
                string.Equals(l.Repo, name.Trim(), StringComparison.Ordinal));
            if (lib is null)
            {
                return YunxiaoWorkbenchResult<string>.Failure(404, $"库不存在：{name}");
            }

            if (lib.Status == LibStatus.Failed)
            {
                targets.Add(lib);
            }
        }

        // ③ 幂等：无 failed 目标 → 不动任何库、零调用，直接回执
        if (targets.Count == 0)
        {
            return YunxiaoWorkbenchResult<string>.Success(task.Id);
        }

        if (!_runContexts.ContainsKey(task.Id))
        {
            // 执行上下文只存于本进程（不落盘——不扩批次 B 任务模型签名）；宿主重启后重跑需重新发起
            return YunxiaoWorkbenchResult<string>.Failure(409, "执行上下文已失效（宿主重启后请重新发起初始化）");
        }

        foreach (var lib in targets)
        {
            lib.Status = LibStatus.Pending;
            lib.Detail = null;
        }

        task.Status = InitTaskStatus.Running;
        task.FinishedAt = null;
        await _taskStore.UpsertAsync(task).ConfigureAwait(false);

        var only = targets.Select(l => l.Repo).ToList();
        _ = Task.Run(() => RunInitAsync(task.Id, only, reuseExisting: true), CancellationToken.None);
        return YunxiaoWorkbenchResult<string>.Success(task.Id);
    }

    /// <summary>
    /// 后台逐库推进（吞掉一切异常，永不抛到线程池）：
    /// 二次查重（TOCTOU）→ 命中转复用 / 查/建代码组 → 建库 → 会话 git（预批清单 = GitCommandPlanner 同源序列）→ 状态收口。
    /// </summary>
    /// <remarks>
    /// 复用语义（S2b §11.2）：目标库已存在（首跑命中含 TOCTOU 被抢先创建）→ 该库
    /// <c>Detail=复用已有库</c> 直接 done，跳过 建组/建库/clone/改名/push 且不发会话指令；
    /// 重跑（<paramref name="reuseExisting"/>=true）命中 → 复用既有库对象继续推 git
    /// （首跑已建库、git 段失败的库可继续推）。
    /// </remarks>
    /// <param name="taskId">任务标识。</param>
    /// <param name="onlyRepos">只推进这些库（null = 全部；重跑用）。</param>
    /// <param name="reuseExisting">重跑幂等口径：已存在新库时复用库对象继续 git（首跑 false=整库转复用跳过）。</param>
    private async Task RunInitAsync(string taskId, IReadOnlyList<string>? onlyRepos, bool reuseExisting)
    {
        InitTask? task = null;
        try
        {
            task = await _taskStore.GetAsync(taskId).ConfigureAwait(false);
            if (task is null || !_runContexts.TryGetValue(taskId, out var ctx))
            {
                return;
            }

            var orgId = _settings.GetOrganizationId();
            var workspaceRoot = _workspaces.List()
                .FirstOrDefault(w => string.Equals(w.Id, ctx.WorkspaceId, StringComparison.Ordinal))
                ?.RootPath ?? string.Empty;
            var targets = task.Libs
                .Where(l => onlyRepos is null || onlyRepos.Contains(l.Repo, StringComparer.Ordinal))
                .ToList();

            // 代码组：同名复用，无则建（懒解析——只有需新建库才落建组副作用；全复用零建组）
            long? namespaceId = null;
            var namespaceResolved = false;

            // 一次初始化一个会话，多库共用（D5：完成后留存）
            string? sessionId = null;

            foreach (var lib in targets)
            {
                var plan = ctx.Libs.FirstOrDefault(p =>
                    string.Equals(p.NewRepoName, lib.Repo, StringComparison.Ordinal));
                if (plan is null)
                {
                    continue;
                }

                try
                {
                    // ① 二次查重（TOCTOU）+ 本地源码幂等判据（S2c §12.1.2）
                    var existing = await FindRepoByNameAsync(lib.Repo, orgId, CancellationToken.None)
                        .ConfigureAwait(false);
                    var targetDir = Path.Combine(workspaceRoot, lib.Repo);
                    var hasLocal = HasLocalSource(workspaceRoot, lib.Repo);
                    // 目录已有源码 → 零 git 步骤（幂等，重跑不再 clone）
                    if (hasLocal)
                    {
                        lib.RepoId = existing?.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        lib.Status = LibStatus.Done;
                        lib.Detail = existing is not null ? "复用已有库" : "已有本地源码";
                        await _taskStore.UpsertAsync(task).ConfigureAwait(false);
                        continue;
                    }

                    IReadOnlyList<GitStep> steps;
                    var isReuseCheckout = existing is not null && !reuseExisting;
                    if (isReuseCheckout)
                    {
                        // ② 真复用（远端已有）+ 无本地源码 → 本地检出计划（S2c §12.1.2）：clone 复用库真 URL → 切 develop
                        lib.RepoId = existing!.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var reuseUrl = !string.IsNullOrWhiteSpace(existing.HttpUrlToRepo)
                            ? existing.HttpUrlToRepo!
                            : BuildFallbackHttpsUrl(existing.Path ?? lib.Repo);
                        steps = GitCommandPlanner.PlanLocalCheckout(workspaceRoot, reuseUrl, lib.Repo, ctx.Branch);
                    }
                    else
                    {
                        // ③ 新建库 或 重跑已建库 → 建库（如需）+ clone-rename-push + 尾部切 develop（S2c §12.2）
                        var newRepoUrl = existing?.HttpUrlToRepo;
                        var repoIdSource = existing;
                        if (existing is null)
                        {
                            // ②③ 建组（懒解析）+ 建库（挂组）
                            if (!namespaceResolved)
                            {
                                namespaceResolved = true;
                                if (!string.IsNullOrWhiteSpace(ctx.GroupNewName))
                                {
                                    var namespaces = await ListAllNamespacesAsync(orgId, CancellationToken.None).ConfigureAwait(false);
                                    var hit = namespaces.FirstOrDefault(n =>
                                        string.Equals(n.Name, ctx.GroupNewName, StringComparison.Ordinal));
                                    namespaceId = hit is not null
                                        ? hit.Id
                                        : (await _codeup
                                            .CreateNamespaceAsync(new YunxiaoCreateNamespaceRequest { Name = ctx.GroupNewName }, orgId, CancellationToken.None)
                                            .ConfigureAwait(false))?.Id;
                                }
                            }

                            var created = await _codeup.CreateRepositoryAsync(new YunxiaoCreateRepositoryRequest
                            {
                                Name = lib.Repo,
                                Visibility = ctx.Visibility,
                                NamespaceId = namespaceId,
                                Description = ctx.Description,
                            }, orgId, CancellationToken.None).ConfigureAwait(false);
                            newRepoUrl = created?.HttpUrlToRepo;
                            repoIdSource = created;
                        }

                        // 回填新库 id（供流水线区 newRepoId 取用）
                        lib.RepoId = repoIdSource?.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture);

                        // 克隆地址：母版 HttpUrlToRepo 优先，缺失按兜底形态拼装（待实测）
                        var template = await FindRepoByNameAsync(plan.TemplateRepoName, orgId, CancellationToken.None)
                            .ConfigureAwait(false);
                        var templateRepoUrl = !string.IsNullOrWhiteSpace(template?.HttpUrlToRepo)
                            ? template!.HttpUrlToRepo!
                            : BuildFallbackHttpsUrl(template?.Path ?? plan.TemplateRepoName);
                        newRepoUrl ??= BuildFallbackHttpsUrl(
                            string.IsNullOrWhiteSpace(ctx.GroupNewName) ? lib.Repo : $"{ctx.GroupNewName}/{lib.Repo}");

                        var cloneSteps = GitCommandPlanner.PlanCloneRenamePush(
                            workspaceRoot,
                            templateRepoUrl,
                            newRepoUrl,
                            plan.TemplateCode,
                            ctx.NewCode,
                            NamingDeriver.Derive(
                                plan.TemplateCode,
                                ctx.NewCode,
                                [],
                                includeDirs: plan.TemplateRepoName.EndsWith(TemplateRepoSuffix, StringComparison.Ordinal))
                                .Dirs.Select(d => d.To).ToList(),
                            ctx.Branch).ToList();
                        // 新库远端无 develop → 推完 master 后本地切 develop（S2c §12.2）
                        // 与 PlanCloneRenamePush 同用正斜杠 repoDir（展示与执行同源，避免路径分隔符不一致）
                        cloneSteps.Add(GitCommandPlanner.PlanSwitchDevelopLocal($"{workspaceRoot}/{lib.Repo}"));
                        steps = cloneSteps;
                    }

                    // ④ 会话 git：展示与执行同源（GitCommandPlanner 单一入口），凭据不进序列
                    sessionId ??= await _gateway
                        .CreateInitSessionAsync(ctx.WorkspaceId, CancellationToken.None)
                        .ConfigureAwait(false);
                    task.SessionId = sessionId;

                    var commands = steps.Select(s => s.Command).ToList();

                    // ⑤ 状态机：pending → cloning → renaming → pushing → done / failed（每步 Upsert）
                    lib.Status = LibStatus.Cloning;
                    await _taskStore.UpsertAsync(task).ConfigureAwait(false);

                    await _gateway
                        .SendAsync(sessionId, BuildInstruction(lib.Repo, steps, isReuseCheckout ? "本地检出" : "复制初始化"), commands, CancellationToken.None)
                        .ConfigureAwait(false);
                    var receipt = await _gateway.WaitReceiptAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                    if (!receipt.Ok)
                    {
                        lib.Status = LibStatus.Failed;
                        lib.Detail = Sanitize(receipt.Error) is { Length: > 0 } msg ? msg : "会话执行未成功收尾";
                        await _taskStore.UpsertAsync(task).ConfigureAwait(false);
                        continue;
                    }

                    // 整序列单指令下发（D2 无逐条进度信号）：回执确认后按序列阶段顺序收口中间态
                    lib.Status = LibStatus.Renaming;
                    await _taskStore.UpsertAsync(task).ConfigureAwait(false);
                    lib.Status = LibStatus.Pushing;
                    await _taskStore.UpsertAsync(task).ConfigureAwait(false);
                    lib.Status = LibStatus.Done;
                    lib.Detail = isReuseCheckout ? "复用已有库" : null;
                    await _taskStore.UpsertAsync(task).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 单库失败不影响他库；Detail 脱敏（剥 pt- 形态 token、截 200 字）
                    lib.Status = LibStatus.Failed;
                    lib.Detail = Sanitize(ex.Message);
                    await _taskStore.UpsertAsync(task).ConfigureAwait(false);
                }
            }

            // 收口：全 done → done，含 failed → failed
            task.FinishedAt = DateTime.Now;
            task.Status = task.Libs.All(l => l.Status == LibStatus.Done)
                ? InitTaskStatus.Done
                : InitTaskStatus.Failed;
            await _taskStore.UpsertAsync(task).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 吞掉后台异常（永不抛到线程池）：未收口的库全落 failed + 脱敏摘要
            try
            {
                if (task is not null)
                {
                    var detail = Sanitize(ex.Message);
                    foreach (var lib in task.Libs.Where(l => l.Status is not (LibStatus.Done or LibStatus.Failed)))
                    {
                        lib.Status = LibStatus.Failed;
                        lib.Detail = detail;
                    }

                    task.FinishedAt ??= DateTime.Now;
                    task.Status = task.Libs.All(l => l.Status == LibStatus.Done)
                        ? InitTaskStatus.Done
                        : InitTaskStatus.Failed;
                    await _taskStore.UpsertAsync(task).ConfigureAwait(false);
                }
            }
            catch
            {
                // 二次兜底：落盘再失败也不外抛
            }
        }
    }

    /// <summary>
    /// FR-3 流水线预览：反查母版 PIPELINEASCODE 流水线 → 取 YAML → 替换 → 逐端 diff。
    /// </summary>
    /// <remarks>
    /// 反查口径：列表按名含 <c>_DEV_Deploy_{TemplateCode}-{End后缀}</c> 匹配（mobile/admin/backend →
    /// Mobile/Admin/Service）；<b>PIPELINEASCODE 判别</b>：<see cref="YunxiaoPipeline"/> 无 <c>type</c>
    /// 字段（实机待 T13 校正），现按 <c>PipelineConfig.Flow</c> 含顶层 <c>stages:</c> 判别新版 YAML 形态
    /// （tb 形态为控制台私有结构）；反查不到 → 该端带警告（允许手选 <c>TemplatePipelineName</c> 兜底）。
    /// </remarks>
    /// <param name="request">预览入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>逐端 diff。</returns>
    public async Task<YunxiaoWorkbenchResult<PipelinePreviewResult>> PreviewPipelinesAsync(PipelinePreviewRequest request, CancellationToken ct)
    {
        // ① 参数校验（400 语义）：S2c §12.3.5 全复用 F6 不等 FR-2 —— NewRepoId 可空，按 NewRepoName 回查
        if (request is null
            || string.IsNullOrWhiteSpace(request.NewRepoName)
            || string.IsNullOrWhiteSpace(request.TemplateCode)
            || string.IsNullOrWhiteSpace(request.NewCode)
            || request.Ends is null
            || request.Ends.Count == 0)
        {
            return YunxiaoWorkbenchResult<PipelinePreviewResult>.Failure(400, "新库名 / 母版代号 / 新代号 / 三端配置均不能为空");
        }

        var templateCode = request.TemplateCode.Trim();
        var newCode = request.NewCode.Trim();
        try
        {
            NamingDeriver.Derive(templateCode, newCode, []);
        }
        catch (ArgumentException ex)
        {
            return YunxiaoWorkbenchResult<PipelinePreviewResult>.Failure(400, ex.Message);
        }

        // S2c §12.3.5：NewRepoId 未给（全复用未跑 FR-2）→ 按库名回查真实 id（FindRepoByNameAsync）
        var newRepoId = request.NewRepoId?.Trim();
        if (string.IsNullOrWhiteSpace(newRepoId))
        {
            var found = await FindRepoByNameAsync(request.NewRepoName.Trim(), _settings.GetOrganizationId(), ct)
                .ConfigureAwait(false);
            newRepoId = found?.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(newRepoId))
            {
                return YunxiaoWorkbenchResult<PipelinePreviewResult>.Failure(404, $"按库名未查到新库：{request.NewRepoName}");
            }
        }

        var orgId = _settings.GetOrganizationId();
        var list = await _flow.ListPipelinesAsync(organizationId: orgId, cancellationToken: ct).ConfigureAwait(false);

        var items = new List<PipelinePreviewItem>();
        foreach (var end in request.Ends.Where(e => e is not null && e.Enabled))
        {
            // 候选流水线：① TemplatePipelineName 非空 → 手选母版（按名精确查）
            //  ② 否则 PipelineName 非空 → 先按它查 catalog（用户已填目标名，若流水线已存在可直接命中）
            //  ③ 若按 PipelineName 也没命中（新项目流水线尚未创建）→ fallback 到 marker 反查母版
            var marker = $"_DEV_Deploy_{templateCode}-{EndSuffix(end.End)}";
            List<YunxiaoPipeline> candidates;
            if (!string.IsNullOrWhiteSpace(end.TemplatePipelineName))
            {
                candidates = list.Where(p => string.Equals(p.DisplayName, end.TemplatePipelineName!.Trim(), StringComparison.Ordinal)).ToList();
            }
            else if (!string.IsNullOrWhiteSpace(end.PipelineName))
            {
                // 先按 PipelineName 查 catalog：用户已填真实流水线名，若已存在则直接命中
                candidates = list.Where(p => SamePipelineName(p.DisplayName, end.PipelineName!.Trim())).ToList();
                // 若没命中（新项目流水线尚未创建）→ fallback marker 反查母版
                if (candidates.Count == 0)
                    candidates = list.Where(p => p.DisplayName?.Contains(marker, StringComparison.Ordinal) == true).ToList();
            }
            else
            {
                candidates = list.Where(p => p.DisplayName?.Contains(marker, StringComparison.Ordinal) == true).ToList();
            }

            YunxiaoPipeline? source = null;
            foreach (var candidate in candidates)
            {
                var detail = candidate.DisplayId is null
                    ? null
                    : await _flow.GetPipelineAsync(candidate.DisplayId, orgId, ct).ConfigureAwait(false);
                // 不再区分 tb（表单流水线）还是 PIPELINEASCODE——YamlSubstitutor 做纯文本替换，tb 形态同样能处理
                if (detail?.PipelineConfig?.Flow is string flow && flow.Length > 0)
                {
                    source = detail;
                    break;
                }
            }

            var name = !string.IsNullOrWhiteSpace(end.PipelineName)
                ? end.PipelineName!.Trim()
                : (source?.DisplayName is string sourceName
                    ? NamingDeriver.Derive(templateCode, newCode, [sourceName]).Pipelines.FirstOrDefault()?.To ?? sourceName
                    : string.Empty);

            // S2c §12.3：派生名前缀容忍命中（去 (端口) 后 OrdinalIgnoreCase）→ Exists（提交时默认跳过）
            var matched = list.FirstOrDefault(p => SamePipelineName(p.DisplayName, name));
            var exists = matched is not null && !string.IsNullOrWhiteSpace(name);
            PipelineInfo? pipelineInfo = null;
            if (exists)
            {
                var actualName = matched!.DisplayName ?? name;
                pipelineInfo = new PipelineInfo(
                    matched.DisplayId ?? string.Empty,
                    actualName,
                    NamingDeriver.ParsePortSuffix(actualName));
            }

            if (source is null)
            {
                items.Add(new PipelinePreviewItem(
                    end.End, name, string.Empty, null,
                    "未反查到母版流水线（catalog 中无对应名称 / 详情无 flow 字段），可手选 TemplatePipelineName 兜底",
                    exists, pipelineInfo));
                continue;
            }

            var yaml = source.PipelineConfig?.Flow ?? string.Empty;
            var vars = new PipelineVars(
                newRepoId!,
                request.NewRepoName.Trim(),
                end.BuildDir?.Trim(),
                "master",
                string.Equals(end.End, "backend", StringComparison.OrdinalIgnoreCase) ? end.Port : null);
            var modified = YamlSubstitutor.Substitute(
                yaml, YamlSubstitutor.BuildPairs(vars, templateCode, newCode));
            items.Add(new PipelinePreviewItem(
                end.End, name, YamlSubstitutor.Diff(yaml, modified), modified, null, exists, pipelineInfo));
        }

        return YunxiaoWorkbenchResult<PipelinePreviewResult>.Success(new PipelinePreviewResult(items));
    }

    /// <summary>
    /// FR-3 流水线提交：逐端独立 create / update（update 覆盖须 <c>Confirm=true</c>，缺失 → 409 语义整体拒绝）。
    /// </summary>
    /// <remarks>
    /// 前置校验（整体 400/409、零写调用）：Content 须含顶层 <c>stages:</c>（防 tb 形态误喂，
    /// 仓库既有真机注释口径——tb 形态 <c>flow</c> 提交必报「至少要包含一个stage」）；
    /// 校验通过后逐端独立执行，一端失败不影响他端（失败原因脱敏进 <c>Message</c>）。
    /// 补全语义（S2b §11.2）：create 且 <c>SkipIfExisting</c>（默认 true）→ create 前同名 TOCTOU 复查
    /// （<c>ListPipelinesAsync</c> 精确匹配 OrdinalIgnoreCase），命中即跳过并回执
    /// <c>{End,Ok:true,Message:"已存在跳过"}</c>（零写调用）；显式覆盖仍走 <c>Mode:"update"+Confirm:true</c>。
    /// </remarks>
    /// <param name="request">提交入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>逐端结果。</returns>
    public async Task<YunxiaoWorkbenchResult<PipelineCommitResult>> CommitPipelinesAsync(PipelineCommitRequest request, CancellationToken ct)
    {
        // ① 前置校验（400/409 语义；零调用）
        if (request is null || request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i is null))
        {
            return YunxiaoWorkbenchResult<PipelineCommitResult>.Failure(400, "提交清单不能为空");
        }

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.End)
                || string.IsNullOrWhiteSpace(item.Name)
                || string.IsNullOrWhiteSpace(item.Content))
            {
                return YunxiaoWorkbenchResult<PipelineCommitResult>.Failure(400, "端名 / 流水线名 / 内容均不能为空");
            }

            // 不再校验顶层 stages：tb 形态（表单流水线 schema: tb → pipeline → stages）同样允许提交
            // （YamlSubstitutor 做纯文本替换，不依赖 YAML 结构；云效 API 也不要求 PIPELINEASCODE）

            var mode = item.Mode?.Trim().ToLowerInvariant();
            if (mode is not ("create" or "update"))
            {
                return YunxiaoWorkbenchResult<PipelineCommitResult>.Failure(400, "mode 仅支持 create / update");
            }

            if (mode == "update")
            {
                if (item.Confirm != true)
                {
                    return YunxiaoWorkbenchResult<PipelineCommitResult>.Failure(409, "覆盖为破坏性操作，需 confirm:true");
                }

                if (string.IsNullOrWhiteSpace(item.PipelineId))
                {
                    return YunxiaoWorkbenchResult<PipelineCommitResult>.Failure(400, "update 须指定流水线 id");
                }
            }
        }

        // ② 逐端独立执行：一端失败不影响他端
        var results = new List<PipelineCommitItemResult>();
        var orgId = _settings.GetOrganizationId();
        List<YunxiaoPipeline>? existingPipelines = null;   // TOCTOU 复查懒取（只为 create+SkipIfExisting）
        foreach (var item in request.Items)
        {
            try
            {
                if (string.Equals(item.Mode.Trim(), "create", StringComparison.OrdinalIgnoreCase))
                {
                    // 补全语义：create 前同名 TOCTOU 复查，命中即跳过（零写调用）
                    if (item.SkipIfExisting)
                    {
                        existingPipelines ??= await _flow
                            .ListPipelinesAsync(organizationId: orgId, cancellationToken: ct)
                            .ConfigureAwait(false);
                        var hit = existingPipelines.FirstOrDefault(p =>
                            SamePipelineName(p.DisplayName, item.Name.Trim()));
                        if (hit is not null)
                        {
                            // S2c §12.3：已存在端零操作，回执「已存在·已同步信息」（含 Id/ActualName，可追溯）
                            var actualName = hit.DisplayName ?? item.Name.Trim();
                            results.Add(new PipelineCommitItemResult(
                                item.End,
                                true,
                                hit.DisplayId,
                                $"已存在·已同步信息（{actualName}）"));
                            continue;
                        }
                    }

                    var id = await _flow.CreatePipelineAsync(
                        new YunxiaoCreatePipelineRequest { PipelineName = item.Name.Trim(), Content = item.Content },
                        orgId, ct).ConfigureAwait(false);
                    results.Add(string.IsNullOrEmpty(id)
                        ? new PipelineCommitItemResult(item.End, false, null, "创建流水线失败（未返回 id）")
                        : new PipelineCommitItemResult(item.End, true, id, null));
                }
                else
                {
                    var pipelineId = item.PipelineId!.Trim();
                    var ok = await _flow.UpdatePipelineAsync(
                        pipelineId,
                        new YunxiaoUpdatePipelineRequest { PipelineName = item.Name.Trim(), Content = item.Content },
                        orgId, ct).ConfigureAwait(false);
                    results.Add(ok
                        ? new PipelineCommitItemResult(item.End, true, pipelineId, null)
                        : new PipelineCommitItemResult(item.End, false, pipelineId, "更新流水线失败"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new PipelineCommitItemResult(item.End, false, item.PipelineId, Sanitize(ex.Message)));
            }
        }

        return YunxiaoWorkbenchResult<PipelineCommitResult>.Success(new PipelineCommitResult(results));
    }

    /// <summary>
    /// 按母版后缀段反查真实模板流水线名（S2d §13.3 / S2e §14 修复一共用）：
    /// <see cref="NamingDeriver.PipelineSuffixSegment"/> 与 <c>_DEV_Deploy_{templateCode}-{端后缀}</c>
    /// 精确等值（OrdinalIgnoreCase）；分组前缀从真实名自身解析，<b>不写死</b>；反查不到返回 <c>null</c>。
    /// </summary>
    /// <param name="catalog"><c>ListPipelinesAsync</c> 流水线清单。</param>
    /// <param name="templateCode">模板代号。</param>
    /// <param name="end">端名（mobile / admin / backend）。</param>
    /// <returns>真实模板流水线名；无命中为 <c>null</c>。</returns>
    private static string? FindRealTemplatePipelineName(
        IReadOnlyList<YunxiaoPipeline> catalog, string templateCode, string end)
    {
        var expectedSegment = $"_DEV_Deploy_{templateCode}-{EndSuffix(end)}";
        return catalog
            .Select(p => p.DisplayName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)
                && string.Equals(
                    NamingDeriver.PipelineSuffixSegment(n!),
                    expectedSegment,
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>端名 → 流水线名端后缀（mobile/admin/backend → Mobile/Admin/Service）。</summary>
    private static string EndSuffix(string end)
        => end.Trim().ToLowerInvariant() switch
        {
            "mobile" => "Mobile",
            "admin" => "Admin",
            "backend" => "Service",
            _ => end.Trim(),
        };

    /// <summary>
    /// PIPELINEASCODE（新版 YAML 形态）判别：flow 含顶层 <c>stages:</c>
    /// （tb 形态为控制台私有结构、无顶层 <c>stages:</c>；type 字段实机待 T13 校正）。
    /// </summary>
    private static bool IsPipelineAsCode(YunxiaoPipeline pipeline)
        => HasTopLevelStages(pipeline.PipelineConfig?.Flow);

    /// <summary>是否含顶层 <c>stages:</c>（行首、无缩进）。</summary>
    private static bool HasTopLevelStages(string? content)
        => !string.IsNullOrWhiteSpace(content)
            && content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Any(line => line.StartsWith("stages:", StringComparison.Ordinal));

    /// <summary>
    /// 指令文案：明示将执行的命令序列（与 GitCommands 同源——GitCommandPlanner 产物）；
    /// 预批清单由 <see cref="ISessionGateway.SendAsync"/> 逐条 allow、偏离 deny。
    /// </summary>
    /// <param name="repoName">目标库名。</param>
    /// <param name="steps">命令序列。</param>
    /// <param name="action">动作语（复制初始化 / 本地检出，S2c §12.1.2 区分复用检出与整库复制）。</param>
    private static string BuildInstruction(string repoName, IReadOnlyList<GitStep> steps, string action = "复制初始化")
        => $"请在当前会话中严格依次执行以下 git 命令，完成 {repoName} 的{action}（仅放行下列命令）："
            + string.Concat(steps.Select(s => $"\n- {s.Label}：{s.Command}"));

    /// <summary>
    /// 脱敏：剥离 <c>pt-</c> 形态 token（PAT 明文零落盘零日志红线）后截 200 字。
    /// </summary>
    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var text = PatTokenPattern.Replace(message, string.Empty);
        return text.Length <= 200 ? text : text[..200];
    }
}

/// <summary>单库执行规格（预检确认后的完整清单条目）。</summary>
/// <param name="TemplateRepoName">模板名（首个 <c>-</c> 前段为模板代号，单端 / 综合模板均可）。</param>
/// <param name="NewRepoName">改写后新库名；null = 按代号前缀替换派生（综合库 <c>{NewCode}-Mixed</c>，单端 <c>{NewCode}-{后缀}</c>）。</param>
public sealed record ExecLibSpec(string TemplateRepoName, string? NewRepoName = null);

/// <summary>
/// 执行入参（预检确认后的完整清单，含改写后名字；设计 §3 <c>/yunxiao/init/exec</c>）。
/// </summary>
/// <param name="Libs">选中母版库清单（每库一条 Lib）。</param>
/// <param name="NewCode">新项目代号（互斥键）。</param>
/// <param name="WorkspaceId">工作区标识。</param>
/// <param name="Branch">分支。</param>
/// <param name="GroupNewName">拟新建代码组名（同名复用，无则建；null = 不挂组）。</param>
/// <param name="Visibility">新库可见性（透传建库请求）。</param>
/// <param name="Description">新库描述（透传建库请求）。</param>
public sealed record ExecRequest(
    IReadOnlyList<ExecLibSpec> Libs,
    string NewCode,
    string WorkspaceId,
    string Branch,
    string? GroupNewName = null,
    string? Visibility = null,
    string? Description = null);

/// <summary>FR-3 三端配置（设计 §3 pipelines 的 <c>ends</c>）。</summary>
/// <param name="End">端名（mobile / admin / backend）。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="BuildDir">构建目录（<c>{{WORK_DIR}}</c> 替换值）。</param>
/// <param name="PipelineName">新流水线名。</param>
/// <param name="Port">后端端口（仅 backend）。</param>
/// <param name="TemplatePipelineName">手选母版流水线名（反查不到时兜底）。</param>
public sealed record EndConfig(
    string End,
    bool Enabled,
    string? BuildDir = null,
    string? PipelineName = null,
    int? Port = null,
    string? TemplatePipelineName = null);

/// <summary>流水线预览入参。</summary>
/// <param name="NewRepoId">新库 id（<c>{{REPO_ID}}</c>）；S2c §12.3.5 全复用 F6 可空，按 NewRepoName 回查真实 id。</param>
/// <param name="NewRepoName">新库名（<c>{{REPO_NAME}}</c>）。</param>
/// <param name="TemplateCode">母版代号。</param>
/// <param name="NewCode">新代号。</param>
/// <param name="Ends">三端配置。</param>
public sealed record PipelinePreviewRequest(
    string? NewRepoId,
    string NewRepoName,
    string TemplateCode,
    string NewCode,
    IReadOnlyList<EndConfig> Ends);

/// <summary>单端预览结果。</summary>
/// <param name="End">端名。</param>
/// <param name="Name">新流水线名。</param>
/// <param name="Diff">逐条 unified diff。</param>
/// <param name="Content">替换后全文（提交用）。</param>
/// <param name="Warning">警告（如反查不到母版流水线）；null = 正常。</param>
/// <param name="Exists">派生名已存在（<c>ListPipelinesAsync</c> 前缀容忍命中，OrdinalIgnoreCase）：提交默认跳过。</param>
/// <param name="Pipeline">已存在端的真实流水线信息（S2c §12.3 回显+反向回填）；缺失端为 <c>null</c>。</param>
public sealed record PipelinePreviewItem(
    string End,
    string Name,
    string Diff,
    string? Content = null,
    string? Warning = null,
    bool Exists = false,
    PipelineInfo? Pipeline = null);

/// <summary>
/// 已存在流水线的真实信息（S2c §12.3）：回显真实名/ID + 反向回填解析端口。
/// </summary>
/// <param name="Id">真实流水线 id（<see cref="YunxiaoPipeline.DisplayId"/>）。</param>
/// <param name="ActualName">真实流水线名（<see cref="YunxiaoPipeline.DisplayName"/>，含 <c>(端口)</c> 后缀）。</param>
/// <param name="Port">从 <paramref name="ActualName"/> 尾部 <c>(数字)</c> 解析的端口；解析不出为 <c>null</c>。</param>
public sealed record PipelineInfo(string Id, string ActualName, int? Port = null);

/// <summary>流水线预览出参（设计 §3 <c>{ diffs:[{end,name,diff}] }</c>）。</summary>
/// <param name="Items">逐端结果。</param>
public sealed record PipelinePreviewResult(IReadOnlyList<PipelinePreviewItem> Items);

/// <summary>流水线提交单条目（设计 §3 <c>{end,mode,name,content,...}</c>）。</summary>
/// <param name="End">端名。</param>
/// <param name="Mode"><c>create</c> / <c>update</c>。</param>
/// <param name="Name">流水线名。</param>
/// <param name="Content">YAML 内容（须含顶层 <c>stages:</c>）。</param>
/// <param name="Confirm">覆盖确认（update 必须 <c>true</c>，否则 409）。</param>
/// <param name="PipelineId">被覆盖流水线 id（update 必填）。</param>
/// <param name="SkipIfExisting">补全语义（S2b §11.2，默认 true）：create 前同名 TOCTOU 复查命中 → 跳过（零写调用）。</param>
public sealed record CommitItem(
    string End,
    string Mode,
    string Name,
    string Content,
    bool? Confirm = null,
    string? PipelineId = null,
    bool SkipIfExisting = true);

/// <summary>流水线提交入参。</summary>
/// <param name="Items">逐端条目。</param>
public sealed record PipelineCommitRequest(IReadOnlyList<CommitItem> Items);

/// <summary>单端提交结果。</summary>
/// <param name="End">端名。</param>
/// <param name="Ok">是否成功。</param>
/// <param name="PipelineId">流水线 id（create 回填 / update 透传）。</param>
/// <param name="Message">失败原因（脱敏）。</param>
public sealed record PipelineCommitItemResult(string End, bool Ok, string? PipelineId = null, string? Message = null);

/// <summary>流水线提交出参（设计 §3 <c>{ results:[{end,ok,pipelineId?,message?}] }</c>）。</summary>
/// <param name="Results">逐端结果（一端失败不影响他端）。</param>
public sealed record PipelineCommitResult(IReadOnlyList<PipelineCommitItemResult> Results);

/// <summary>项目初始化完成登记入参。</summary>
/// <param name="ProjectId">云效项目 ID（优先匹配键；未知可空）。</param>
/// <param name="Identifier">项目标识（云效 customCode；ProjectId 为空时按它匹配）。</param>
/// <param name="Codename">新项目代号。</param>
/// <param name="RepoName">绑定的代码库名。</param>
/// <param name="Ends">三端流水线登记。</param>
/// <param name="TemplateCode">母版库代号（再次进入初始化页反显用）。</param>
/// <param name="GroupName">代码组名。</param>
/// <param name="Branch">初始化分支。</param>
/// <param name="SelectedRepos">选中的母版库原库名。</param>
public sealed record ProjectInitRequest(
    string? ProjectId,
    string? Identifier,
    string Codename,
    string? RepoName = null,
    IReadOnlyList<ProjectInitEnd>? Ends = null,
    string? TemplateCode = null,
    string? GroupName = null,
    string? Branch = null,
    IReadOnlyList<string>? SelectedRepos = null,
    string? WorkspaceId = null);

/// <summary>登记的单端流水线。</summary>
/// <param name="End">端名。</param>
/// <param name="Name">流水线名。</param>
/// <param name="PipelineId">云效流水线 id。</param>
/// <param name="Enabled">该端是否启用。</param>
/// <param name="Port">后端监听端口。</param>
/// <param name="TemplateName">母版流水线名（反显用）。</param>
public sealed record ProjectInitEnd(
    string End,
    string? Name = null,
    string? PipelineId = null,
    bool Enabled = true,
    int? Port = null,
    string? TemplateName = null,
    string? Dir = null);

/// <summary>完成登记反查入参。</summary>
/// <param name="ProjectId">云效项目 ID。</param>
/// <param name="Identifier">项目标识。</param>
public sealed record ProjectInitGetRequest(string? ProjectId, string? Identifier);

/// <summary>完成登记反查出参（再次进入初始化页反显）。</summary>/// <param name="ProjectId">云效项目 ID。</param>
/// <param name="Identifier">项目标识。</param>
/// <param name="Codename">新项目代号。</param>
/// <param name="TemplateCode">母版库代号。</param>
/// <param name="SelectedRepos">选中的母版库原库名。</param>
/// <param name="GroupName">代码组名。</param>
/// <param name="Branch">初始化分支。</param>
/// <param name="RepoName">绑定的代码库名。</param>
/// <param name="CompletedAt">完成时间。</param>
/// <param name="Ends">三端登记。</param>
public sealed record ProjectInitRecordDto(
    string ProjectId,
    string? Identifier,
    string Codename,
    string? TemplateCode,
    IReadOnlyList<string> SelectedRepos,
    string? GroupName,
    string? Branch,
    string? RepoName,
    DateTime CompletedAt,
    IReadOnlyList<ProjectInitEnd> Ends,
    string? WorkspaceId = null);
