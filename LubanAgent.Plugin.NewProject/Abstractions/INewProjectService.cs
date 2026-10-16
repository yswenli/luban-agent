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
/// <c>LuBan.AIAgent.Connectors.AliyunDevOps</c>（PRD §4.11.2），**不**在插件内自行拼 REST。
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
    Task<StepResult> CreateRepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 4 · 配置与建库：生成配置、执行建库脚本。
    /// </summary>
    Task<StepResult> SetupDatabaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 5 · 编译检查：逐端编译并汇总结果（失败阻断后续步骤，但保留已生成代码）。
    /// </summary>
    Task<StepResult> BuildAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 6 · 推送改动到自有仓库：提交本地最终改动并推送到各端专属仓库。
    /// </summary>
    Task<StepResult> PushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 7 · 创建流水线：逐端克隆参考流水线并替换后创建（所选端各 1 条，最多 3 条）。
    /// </summary>
    Task<StepResult> CreatePipelinesAsync(CancellationToken cancellationToken = default);

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