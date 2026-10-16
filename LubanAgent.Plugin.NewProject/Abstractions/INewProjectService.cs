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
    /// 删除已写入的 <c>newproject-*</c> 技能与 <c>yunxiao</c> MCP 文件、标记档案未完成；
    /// <b>远端产物一律不自动删除</b>，只保留孤儿清单。
    /// </summary>
    Task AbortAndCleanupAsync(CancellationToken cancellationToken = default);
}