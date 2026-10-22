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
/// 云效步骤（Step 3 / 6 / 7）在向导路径下直连框架连接器
/// <c>LubanAgentCore.Connectors.AliyunDevOps</c>（PRD §4.11.2），**不**在插件内自行拼 REST。
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
    /// ② <c>{localPath}\{缩写}-Service|Admin|Mobile</c> 目标目录是否已存在且非空。
    /// 云效侧「同名仓库唯一」属 Step 3 的重跑幂等（同名复用），不在此处判定。
    /// </para>
    /// <para>
    /// 本方法<b>只读</b>：通过校验不产生任何副作用（不建目录、不落档案）。
    /// </para>
    /// </remarks>
    /// <param name="name">项目中文名称。</param>
    /// <param name="code">英文缩写（小写字母开头，可含数字）。</param>
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
    /// <param name="code">英文缩写（小写字母开头，可含数字）。</param>
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
    /// Step 3 · 按模板建库并克隆：以各端模板仓为模板创建专属仓库（可见性＝组织内公开），再按 HTTPS 地址克隆并替换占位符。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>重跑幂等（PRD §4.3 Step 3 / §6 #15）</b>：①目标代码组先查后建（同名组已存在则复用）；
    /// ②建库前先按目标仓库名检索，同名已存在则跳过创建、直接复用；
    /// ③克隆前判断目标目录是否非空，非空时经 <see cref="CloneConflictResolver"/> 让用户三选一；
    /// ④各端创建的仓库 id / 地址即时写入档案的 <see cref="ProjectSpec.RemoteArtifacts"/>。
    /// </para>
    /// <para>
    /// <b>模板与 pin（PRD §6 #5）</b>：模板仓在云效<b>模板库</b>（<c>templateType=1</c>）中按登记名称（或 ID）定位；
    /// pin 优先用配置登记值，留空则按 <c>develop → master → main → 模板默认分支</c> 实时解析；
    /// 两者任一解析不出即阻断本步——不跟随升级、不跳过、不猜测。
    /// </para>
    /// </remarks>
    Task<StepResult> CreateRepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 3 预检（<b>只读</b>）：把「即将使用的参数」摆给用户核对——目标代码组（新建还是复用）、
    /// 逐端模板库命中项、解析出的 pin 及来源、目标仓库名（新建还是复用）、可见性与落地目录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 预检<b>不创建任何云效资源</b>：代码组只查不建，仓库只查不建。真正的建组 / 建库都在用户确认后的
    /// <see cref="CreateRepositoriesAsync"/> 里发生（PRD §4.3「用户确认界面」）。
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
    /// 把确认后的建库计划写回档案（仓库名 / 可见性；模板与 pin 分支以档案与插件配置为准，本地目录随新仓库名重算）。
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
    Task<StepResult> PushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 7 · 创建流水线：按端渲染内嵌新版 YAML 模板并创建（所选端各 1 条，最多 3 条）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>v0.30 口径</b>：云效 OpenAPI 没有「复制流水线」接口，且只收顶层 <c>stages:</c> 的新版 YAML
    /// （喂 tb 形态 <c>flow</c> 原文必报 <c>1209300</c>），故改为「按企服版语义翻译成新版 YAML → 本地渲染 → 提交创建」，
    /// 变量与缓存一并生成（<c>variables:</c> / <c>cache:</c>）。
    /// </para>
    /// <para>
    /// <b>只含构建阶段</b>：部署阶段默认不写入（PRD §6 #14），仅以注释块附在 YAML 末尾并列出「待人工绑定清单」；
    /// 人工确认资源后可调 <see cref="AppendDeployStagesAsync"/> 补齐。<b>建完不自动运行</b>。
    /// </para>
    /// <para><b>前置闸门</b>：Step 6（推送）未成功时拒绝执行（判据见 <c>StepGate.CanCreatePipelines</c>）。</para>
    /// <para>
    /// <b>手工微调（v0.31）</b>：<paramref name="yamlOverrides"/> 按端提供用户改过的 YAML 原文，命中即以它替换模板渲染结果
    /// （流水线名与待绑定清单仍按模板口径，<c>name:</c> 必须与模板名一致，否则直接给可读失败、不发请求）；
    /// 未命中／空白即用模板。真正的语法校验交给云效（非法 YAML 原样透出 <c>1209300</c>）。
    /// </para>
    /// </remarks>
    /// <param name="yamlOverrides">按端手工微调的 YAML 原文（<c>null</c> 或空字典＝全部走模板）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>步骤结果（逐端结论见档案 <see cref="ProjectSpec.PipelineResults"/>）。</returns>
    Task<StepResult> CreatePipelinesAsync(
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 7 补充操作 · 一键补齐部署阶段：把该端部署阶段并存进已有流水线（<c>UpdatePipeline</c>）。
    /// </summary>
    /// <remarks>
    /// 仅有独立部署阶段的端生效（当前仅后端；前端构建产物上传 OSS 即发布），无流水线或无需补齐的端跳过并在结果里说明。
    /// <b>与手工微调的配合</b>：若该端提供了微调 YAML，则以微调文本为基础、在末尾追加部署阶段片段
    /// （微调文本里已自己写了 <c>VMDockerDeploy</c> 或已存在 <c>deploy</c> 阶段时视为自备部署阶段，不再追加）。
    /// </remarks>
    /// <param name="yamlOverrides">按端手工微调的 YAML 原文（<c>null</c> 或空字典＝全部走模板）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>步骤结果。</returns>
    Task<StepResult> AppendDeployStagesAsync(
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 7 补充操作 · 预览：按当前档案算出「各端将提交的流水线名 + 新版 YAML + 待绑定清单」。
    /// </summary>
    /// <remarks>
    /// <b>不调云效、不落档案</b>（纯本地渲染），供界面在建之前摊开原文复核；口径与创建同源，预览到什么就提交什么。
    /// 命中 <paramref name="yamlOverrides"/> 时回显微调后的原文（<see cref="PipelinePreview.Edited"/> 为 <c>true</c>）。
    /// </remarks>
    /// <param name="yamlOverrides">按端手工微调的 YAML 原文（<c>null</c> 或空字典＝全部走模板）。</param>
    /// <returns>按端预览（口径同 <see cref="CreatePipelinesAsync"/>：已克隆的所选端）。</returns>
    IReadOnlyList<PipelinePreview> PreviewPipelines(IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides = null);

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