/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： INewProjectService
*创建时间：2026/10/16
*描述：共享能力层契约（PRD §4.10.2）：向导面板与聊天入口共用同一实例，零逻辑分叉
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// 「新建项目」共享能力层：封装七步流程中的六项后端能力（第 1 步表单采集不在此层）。
/// </summary>
/// <remarks>
/// <para>
/// PRD §4.10.2 要求向导面板与聊天式入口（<c>newproject-*</c> 技能）调用<b>同一个实例</b>，
/// 因此本契约的所有方法都必须以「当前项目档案」为唯一状态载体，不得各自持有流程状态。
/// </para>
/// <para>
/// 云效 / git 步骤（Step 3 / 6 / 7）为<b>助手驱动（v0.3）</b>：codeup / flow 操作由项目会话助手经云效 MCP 工具执行，
/// git 操作（克隆 / 推送）走框架 git 工具组；插件只做薄校验 + 下发提示词 + 回读结论文件 + 独立复核 + 更新档案，
/// <b>不</b>在插件内自行拼 REST，也<b>不</b>在插件内渲染 YAML 模板（组织专有值随包分发在 <c>docs/</c>）。
/// </para>
/// </remarks>
public interface INewProjectService
{
    /// <summary>
    /// 当前项目档案；尚未开始创建时为 <c>null</c>。
    /// </summary>
    ProjectSpec? Current { get; }

    /// <summary>
    /// 当前推进到的步骤。
    /// </summary>
    WizardStep CurrentStep { get; }

    /// <summary>
    /// Agent 操作记录（时间线，供右侧「操作记录区」渲染，PRD §4.6）。
    /// </summary>
    IReadOnlyList<OperationRecord> Operations { get; }

    /// <summary>
    /// 档案或操作记录发生变化时触发（面板据此刷新）。
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// 「归属组织」（云效组织 ID，只读展示）：取自 codex 云效连接配置；未配置时为 <c>null</c>。
    /// </summary>
    string? OrganizationHint { get; }

    /// <summary>
    /// 克隆冲突决策入口（PRD §4.3 Step 3 重跑幂等②）：由向导面板注入「三选一」内联选择；
    /// 为 <c>null</c>（如聊天入口、面板未创建）时 Step 3 遇到非空目标目录<b>即中止本步</b>并给出引导文案，
    /// 绝不静默覆盖、也不静默复用。
    /// </summary>
    Func<CloneConflict, Task<CloneConflictChoice>>? CloneConflictResolver { get; set; }

    /// <summary>
    /// 校验 Step 1 基本信息（PRD §4.3）：必填项、缩写命名规范、端必选、本地目录可写与唯一性。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>唯一性口径（P2 定案）</b>：① 该目录是否已被宿主登记为工作区（同路径必然派生同一 <c>WorkspaceId</c>）；
    /// ② <c>{localPath}\{Code}-Service|Admin|Mobile</c>（如 <c>Csxmy-Service</c>）目标目录是否已存在且非空。
    /// 云效侧「同名仓库唯一」属 Step 3 的重跑幂等（同名复用），不在此处判定。
    /// </para>
    /// <para>
    /// 本方法<b>只读</b>：通过校验不产生任何副作用（不建目录、不落档案）。
    /// </para>
    /// </remarks>
    /// <param name="name">项目中文名称。</param>
    /// <param name="code">英文缩写（只能是小写字母 a-z，不含数字）。</param>
    /// <param name="introduction">一句话项目介绍。</param>
    /// <param name="ends">所选端（后端、管理端必选）。</param>
    /// <param name="localPath">本地目录（工作区根目录）。</param>
    /// <param name="allowExistingWorkspace">
    /// 是否放行唯一性口径①（该目录已被宿主登记为工作区）。默认 <c>false</c>（拦截，防误把已有工作区当新项目）；
    /// 用户在面板上显式点「复用该工作区继续」时传 <c>true</c>（复用分支本身幂等，见 <see cref="InitializeAsync"/>）。
    /// </param>
    /// <returns>校验结果（字段级错误文案 + 非阻断提示 + 命中的已登记工作区）。</returns>
    Task<BasicInfoValidation> ValidateBasicInfoAsync(
        string name,
        string code,
        string introduction,
        IEnumerable<ProjectEnd> ends,
        string localPath,
        bool allowExistingWorkspace = false);

    /// <summary>
    /// 开始一个新项目：建立项目档案（不触网、不落盘副作用），并覆盖上一次未完成的档案。
    /// </summary>
    /// <param name="name">项目中文名称。</param>
    /// <param name="code">英文缩写（只能是小写字母 a-z，不含数字）。</param>
    /// <param name="introduction">一句话项目介绍。</param>
    /// <param name="ends">所选端（后端、管理端必选）。</param>
    /// <param name="localPath">本地目录（工作区根目录）。</param>
    /// <returns>新建的项目档案。</returns>
    ProjectSpec StartNewProject(
        string name,
        string code,
        string introduction,
        IEnumerable<ProjectEnd> ends,
        string localPath);

    /// <summary>
    /// 清空当前档案与操作记录（「中止并清理」的本地部分之后调用，PRD §4.7）。
    /// </summary>
    void Reset();

    /// <summary>
    /// Step 2 · 初始化项目环境：建工作区 → 授权 → 置为当前（内部建新会话）→ 装配技能 → 接通云效 → 生成项目助手。
    /// </summary>
    /// <remarks>
    /// <b>重跑幂等（PRD §4.3 陷阱①②③）</b>：先探测同路径工作区是否已存在；已存在则走「复用」分支
    /// （<b>禁止</b>再次 <c>CreateWorkspaceAsync</c>），且在 <c>SetCurrentAsync</c> 之后<b>显式新建会话</b>，
    /// <b>不</b>使用它恢复的旧会话；失败仅回滚本步（新建的工作区用 <c>DeleteWorkspaceAsync</c> 注销，
    /// <b>不删</b> <c>LocalPath</c> 目录）。
    /// </remarks>
    Task<StepResult> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 3 · 建库并克隆（<b>执行续跑</b>）：把用户确认后的建库计划 + 准备结论文件路径下发助手，
    /// 由助手建组 / 建库（<c>codeup_*</c> MCP 工具）、克隆落盘（框架 git 工具组）、替换占位符。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>重跑幂等（PRD §4.3 Step 3 / §6 #15）</b>：①目标代码组先查后建（同名组已存在则复用）；
    /// ②建库前先按目标仓库名检索，同名已存在则跳过创建、直接复用；
    /// ③克隆前判断目标目录是否非空，非空时助手回 <c>status=needsInput</c> + <c>inputs[id=clone-conflict]</c>，
    /// 插件经 <see cref="CloneConflictResolver"/> 让用户三选一后<b>同会话续跑</b>（「清空后重克隆」的目录删除由助手执行，C5′）；
    /// ④各端创建的仓库 id / 地址由结论 <c>artifacts[]</c> 回写档案的 <see cref="ProjectSpec.RemoteArtifacts"/>。
    /// </para>
    /// <para>
    /// <b>模板与 pin（PRD §6 #5）</b>：模板仓在云效<b>模板库</b>（<c>templateType=1</c>）中按 <c>docs/org-reference.md</c>
    /// 登记的模板定位、pin 按 <c>docs/step3-repositories.md</c> 的择优口径实时解析；两者任一解析不出即整步失败
    /// ——不跟随升级、不跳过、不猜测。判定＝结论文件（逐端完备）+ 事件流无错误 + 插件侧本地复核
    /// （<c>StepLocalVerifier.VerifyRepositories</c>：目录 / <c>.git</c> / <c>refs</c> / 占位符残余）。
    /// </para>
    /// <para><b>重跑联动</b>：本步成功后作废上一轮 Step 5 编译结论与 Step 7 流水线结论（本地代码与仓库地址都可能已变）。</para>
    /// </remarks>
    Task<StepResult> CreateRepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 3 预检（<b>只读准备</b>）：下发助手算出「即将使用的参数」供用户核对——目标代码组（新建还是复用）、
    /// 逐端模板库命中项、解析出的 pin 及来源、目标仓库名（新建还是复用）、可见性与落地目录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 准备<b>不创建任何云效资源、不克隆</b>：代码组只查不建，仓库只查不建。真正的建组 / 建库 / 克隆都在用户确认后的
    /// <see cref="CreateRepositoriesAsync"/> 里发生（PRD §4.3「用户确认界面」）。读数由 <c>artifacts[]</c> 映射而来。
    /// </para>
    /// <para>
    /// 任何一环出错都<b>不抛异常</b>，而是收集进 <see cref="RepositoryPrecheck.Errors"/> 供界面逐条展示。
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>预检读数。</returns>
    Task<RepositoryPrecheck> PrecheckRepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验 Step 3 建库计划（PRD §4.3「用户确认界面」）：仓库名非空 / 合法 / 同批不重复，可见性取值合法。
    /// </summary>
    /// <remarks>本方法<b>只读</b>：通过校验不产生副作用；面板在「我已确认」后调用，通过后再 <see cref="ApplyRepositoryPlan"/>。</remarks>
    /// <param name="plan">用户在确认区改过的建库计划（逐端仓库名 + 可见性）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验结果（错误文案；提示通常为空）。</returns>
    Task<BasicInfoValidation> ValidateRepositoryPlanAsync(
        IEnumerable<EndRepository> plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把确认后的建库计划写回档案（仓库名 / 可见性；本地目录随新仓库名重算，模板与 pin 由助手在准备阶段给出）。
    /// </summary>
    /// <param name="plan">建库计划。</param>
    void ApplyRepositoryPlan(IEnumerable<EndRepository> plan);

    /// <summary>
    /// Step 4 · 数据库探活（<b>v0.22 口径</b>）：把界面填的参数交给项目助手执行<b>只读</b>探活，回报结论；
    /// <b>不建库、不写任何项目文件 / <c>appsettings</c></b>，结论<b>不阻断</b>流程。
    /// </summary>
    /// <remarks>
    /// <para>判定口径＝「能连上且该库存在」。无论探活通过与否，本步结束后档案都推进到 Step 5。</para>
    /// <para>口令只在内存（随本轮提示词交给助手），不落档案、不写日志、操作记录一律脱敏。</para>
    /// </remarks>
    /// <param name="request">探活参数（服务器 / 端口 / 库名 / 账号 / 口令）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>探活结果（失败时 <see cref="StepResult.Message"/> 为可操作原因）。</returns>
    Task<StepResult> SetupDatabaseAsync(DatabaseProbeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 4 表单的默认值（<b>只读</b>）：优先档案里已填过的非敏感参数，其次解析已克隆后端仓库的
    /// <c>WebHost/appsettings.json</c> 连接串。
    /// </summary>
    /// <remarks>
    /// 供面板预填用。返回值的 <see cref="DatabaseProbeRequest.Password"/> <b>恒为空</b>：
    /// 口令只由用户在界面手工填，不回填、不落档案、不写日志。
    /// </remarks>
    /// <returns>默认探活参数。</returns>
    DatabaseProbeRequest SuggestDatabaseProbe();

    /// <summary>
    /// Step 5 · 编译检查：按所选端逐端编译并汇总结果。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 编译方式<b>由助手按工程类型自行判断</b>（.NET 端 <c>dotnet build</c>、Vite/Vue 端 <c>pnpm build</c> 等），
    /// 插件只提供白名单与结论契约。
    /// </para>
    /// <para><b>失败阻断</b>：只要有一端未通过，档案<b>不</b>推进到 Step 6；修好后可重跑本步。</para>
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>编译结果（逐端结论见档案 <see cref="ProjectSpec.BuildResults"/>）。</returns>
    Task<StepResult> BuildAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 6 · 推送改动到自有仓库：提交本地最终改动并推送到各端专属仓库。
    /// </summary>
    /// <remarks>
    /// <b>v0.3 助手驱动</b>：提交与推送由项目会话助手按 <c>docs/step6-push.md</c> 的口径调用框架 git 工具组
    /// （<c>GitAddAsync</c> / <c>GitCommitAsync</c> / <c>GitPushAsync</c> 等），插件回读结论文件并做独立复核
    /// （<c>StepLocalVerifier.VerifyPush</c>：本地 <c>refs/remotes/origin/&lt;pushBranch&gt;</c> 与自报提交 id 比对）。
    /// <b>前置闸门</b>：Step 5 编译未全部通过时拒绝执行（<c>StepGate.CanPush</c>，判据读 Step 5 结论文件）。
    /// git 凭据由用户在外部环境自行配置；插件只提示失败原因，<b>不</b>转述任何凭据。
    /// </remarks>
    Task<StepResult> PushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 7 · 创建流水线（<b>执行续跑</b>）：把准备结论文件 + 用户确认的微调 YAML / 部署目标下发助手，
    /// 由助手按 <c>docs/pipeline-*.yml.md</c> 渲染并创建（所选端各 1 条，最多 3 条）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>v0.30 口径保留</b>：云效 OpenAPI 没有「复制流水线」接口，且只收顶层 <c>stages:</c> 的新版 YAML
    /// （喂 tb 形态 <c>flow</c> 原文必报 <c>1209300</c>），故仍是「按企服版语义写成新版 YAML → 提交创建」；
    /// 变化只在<b>谁来渲染</b>——v0.3 起 YAML 正文来自随包分发的 <c>docs/pipeline-*.yml.md</c>，由助手渲染（C10′）。
    /// </para>
    /// <para>
    /// <b>v0.32 口径保留（部署阶段随创建写入）</b>：有独立部署阶段的端（仅后端）在<b>创建时就写入</b> <c>deploy</c> 段，
    /// 目标主机组由用户在<b>创建之前</b>从云效主机组列表里选（<see cref="ListDeployTargetsAsync"/>，或聊天路径在对话里选，C15′）。
    /// <b>建完不自动运行</b>（不发 <c>flow_run_pipeline</c>）。
    /// </para>
    /// <para>
    /// <b>创建前置守卫</b>：需要部署阶段的端若没选到目标，插件给出可读失败、<b>不发任何创建请求</b>—— 避免把
    /// <c>'&lt;未选择：主机组 uuid&gt;'</c> 占位符提交到云效（不回退成手填 uuid：填错云效只回「该机器组uuid[x]不存在」，白跑一次且污染远端）。
    /// </para>
    /// <para>
    /// <b>幂等（编排 B · 全端）</b>：创建前按流水线名精确检索，<b>命中同名即对该条覆盖更新本次 YAML</b>
    /// （不再「命中即复用旧的那条」——旧流水线可能缺部署段或残留上一版模板 / 占位符，复用会让本次 YAML 与远端不一致）。
    /// 未命中才创建。<b>副作用</b>：普通重跑也会覆盖远端（含控制台上的人工改动）。
    /// </para>
    /// <para><b>前置闸门</b>：Step 6（推送）未成功时拒绝执行（判据见 <c>StepGate.CanCreatePipelines</c>）。</para>
    /// <para>
    /// <b>手工微调</b>：<paramref name="yamlOverrides"/> 按端提供用户改过的 YAML 原文，命中即随提示词下发、由助手原样提交
    /// （插件<b>不做</b>即时校验，非法的原文由云效回 <c>1209300</c> 原样透出）。
    /// </para>
    /// </remarks>
    /// <param name="yamlOverrides">按端手工微调的 YAML 原文（<c>null</c> 或空字典＝全部用准备结论的 YAML）。</param>
    /// <param name="deployTargets">按端选定的部署目标（<c>null</c>／缺项＝用准备结论里的推荐值）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>步骤结果（逐端结论见档案 <see cref="ProjectSpec.PipelineResults"/>）。</returns>
    Task<StepResult> CreatePipelinesAsync(
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 7 补充操作 · 覆盖更新：按<b>当前口径</b>重新出各端 YAML 并覆盖已有流水线（<c>UpdatePipeline</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用途（v0.32 沿用）：把 v0.31 及更早建的「只含构建段、末尾带注释块」的旧流水线补成含部署段的完整 YAML；
    /// 或用户换了部署目标后纠偏。<b>按档案里的 <c>PipelineId</c> 直改</b>，不做按名检索。
    /// </para>
    /// <para>
    /// <b>破坏性</b>：远端流水线已有的人工改动会被本步覆盖（界面另有确认框）；无流水线的端跳过并在结果里说明。
    /// 部署目标守卫与 <see cref="CreatePipelinesAsync"/> 完全一致（缺目标即失败、不发请求）。
    /// </para>
    /// <para>执行续跑的输入仍取最近一次 <see cref="PreparePipelinesAsync"/> 的准备结论（覆盖更新不设 Step 6 闸门）。</para>
    /// </remarks>
    /// <param name="yamlOverrides">按端手工微调的 YAML 原文（<c>null</c> 或空字典＝全部用准备结论的 YAML）。</param>
    /// <param name="deployTargets">按端选定的部署目标（<c>null</c>／缺项＝用准备结论里的推荐值）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>步骤结果。</returns>
    Task<StepResult> UpdatePipelinesAsync(
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 7 准备（只读下发）：驱动项目会话助手算出「各端将提交的流水线名 + 新版 YAML + 待绑定清单 + 部署目标候选」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>v0.3 助手驱动</b>：YAML 正文来自随包分发的 <c>docs/pipeline-*.yml.md</c>，由助手按文档渲染并写成结论文件
    /// （<c>results/step7-*.json</c>），插件回读映射——<b>不在插件内渲染模板</b>（C10′）。
    /// </para>
    /// <para>
    /// <b>不建 / 不改云效流水线</b>：本阶段只读，创建 / 覆盖更新发生在用户确认后的执行续跑
    /// （<see cref="CreatePipelinesAsync"/> / <see cref="UpdatePipelinesAsync"/>）。
    /// </para>
    /// <para>结果同时缓存在服务实例上，供同步的 <see cref="PreviewPipelines"/> 应用手工微调后回显。</para>
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>准备读数（预览 + 交互项 + 映射缺口）；助手跑不起来时 <see cref="PipelinePrepareResult.Errors"/> 非空。</returns>
    Task<PipelinePrepareResult> PreparePipelinesAsync(CancellationToken cancellationToken = default);


    /// <summary>
    /// Step 7 补充操作 · 预览：回显<b>最近一次只读准备</b>算出的「各端将提交的流水线名 + 新版 YAML + 待绑定清单」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>纯读缓存</b>：数据源是上一次 <see cref="PreparePipelinesAsync"/> 的结论映射（不触网、不再下发助手）。
    /// 尚无准备结论时返回空列表（界面应先调 <see cref="PreparePipelinesAsync"/>）。
    /// </para>
    /// <para>命中 <paramref name="yamlOverrides"/> 时回显微调后的原文（<see cref="PipelinePreview.Edited"/> 为 <c>true</c>）。</para>
    /// </remarks>
    /// <param name="yamlOverrides">按端手工微调的 YAML 原文（<c>null</c> 或空字典＝全部用准备结论的 YAML）。</param>
    /// <param name="deployTargets">按端选定的部署目标（当前仅用于 <see cref="PipelinePreview.RecommendedDeployTargetUuid"/> 回显）。</param>
    /// <returns>按端预览（口径同 <see cref="CreatePipelinesAsync"/>：已克隆的所选端）。</returns>
    IReadOnlyList<PipelinePreview> PreviewPipelines(
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets = null);

    /// <summary>
    /// Step 7 · 拉取<b>部署目标候选</b>（云效主机组列表，v0.32 只读能力）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 让用户在<b>创建流水线之前</b>看清「能部署到哪些机器」。失败时<b>抛可读异常</b>（不返回空列表）：
    /// 界面据此显示红字并<b>阻断创建</b>，绝不静默降级成手填 uuid（口径见 PRD §4.3 Step 7 v0.32）。
    /// </para>
    /// <para>机器明细不在列表里（真机观测列表恒不带 <c>hostInfos</c>），需要时调 <see cref="ListDeployTargetMachinesAsync"/>。</para>
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>主机组（按云效返回顺序；<c>HostGroupId</c> 用于取明细，<c>Uuid</c> 用于写 YAML）。</returns>
    Task<IReadOnlyList<DeployHostGroup>> ListDeployTargetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 7 · 拉取某个主机组的<b>机器明细</b>（把「到底打哪台机器」摊给用户）。
    /// </summary>
    /// <param name="hostGroupId">主机组<b>数字 ID</b>（云效详情接口的路径键只认数字，传 uuid 会 400）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>机器明细（可能为空：真机观测存在 <c>hostNum=0</c> 的空组）。</returns>
    Task<IReadOnlyList<DeployHostMachine>> ListDeployTargetMachinesAsync(
        long hostGroupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 「中止并清理」（PRD §4.7）：注销本项目工作区（<b>不删</b> <c>LocalPath</c> 目录）、
    /// 删除已写入的 <c>newproject-*</c> 技能与 <c>yunxiao</c> MCP 文件、清空档案（等价于「未完成」，不留误导性进度）；
    /// <b>远端产物一律不自动删除</b>，只保留孤儿清单。
    /// </summary>
    /// <remarks>
    /// 清理顺序固定为「先删技能 / MCP，再注销工作区」：宿主删除技能时会先按本进程记录的技能根目录定位，
    /// 记录为空（如重启后再清理）才退化到「当前工作区」——Step 2 刚把项目工作区置为当前，此时定位最准。
    /// 先注销工作区会切走当前工作区，反而让退化路径指向错误目录。
    /// </remarks>
    Task AbortAndCleanupAsync(CancellationToken cancellationToken = default);
}