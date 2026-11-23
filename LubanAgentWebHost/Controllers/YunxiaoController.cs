/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： YunxiaoController
*描述：云效工作台 REST（薄路由，照抄 PluginController 形态：只做参数透传，业务在 WebYunxiaoService）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 云效工作台 REST。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebYunxiaoService"/>（壳）→ <see cref="YunxiaoWorkbenchService"/>（编排）。
/// </remarks>
public sealed class YunxiaoController : Controller
{
    /// <summary>读取连接设置。</summary>
    /// <returns><c>{ organizationId, hasPat }</c>（不含 PAT 明文）。</returns>
    [HttpGet]
    public Task<ActionResult> Settings() => WebYunxiaoService.GetSettingsAsync();

    /// <summary>测试连接（只读探活）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoTestConnectionRequest"/> JSON。</param>
    /// <returns><c>{ ok, projectCount, message? }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> TestConnection(string payload) => WebYunxiaoService.TestConnectionAsync(payload);

    /// <summary>保存连接设置（服务端先探活，探活失败拒绝保存）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoSaveSettingsRequest"/> JSON。</param>
    /// <returns><c>{ saved: true }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> SaveSettings(string payload) => WebYunxiaoService.SaveSettingsAsync(payload);

    /// <summary>项目列表。</summary>
    /// <returns><c>{ items, total, page, perPage }</c>。</returns>
    [HttpGet]
    public Task<ActionResult> Projects() => WebYunxiaoService.ListProjectsAsync();

    // ── S2（批次 D）：项目初始化 + FR-3 流水线 ─────────────────────────────

    /// <summary>项目初始化清单（母版库 / 代码组 / 工作区 / 会话通路）。</summary>
    /// <returns><c>{ repos, groups, workspaces, sessionReady }</c>。</returns>
    [HttpGet]
    public Task<ActionResult> Manifests() => WebYunxiaoService.ManifestsAsync();

    /// <summary>初始化预检（零副作用：命名派生 + git 命令序列 + 冲突清单）。</summary>
    /// <param name="payload">包装后的 <see cref="PreflightRequest"/> JSON。</param>
    /// <returns>预检结论（400 参数非法）。</returns>
    [HttpPost]
    public Task<ActionResult> Preflight(string payload) => WebYunxiaoService.PreflightAsync(payload);

    /// <summary>流水线命名派生（S2e §14 修复一：反查母版真名 → 逐端派生，零副作用轻量接口）。</summary>
    /// <returns><c>{ items: [{ end, from, to }] }</c>；参数取查询串 <c>templateRepoName</c>/<c>newCode</c>/<c>ends</c>。</returns>
    [HttpGet]
    public Task<ActionResult> PipelineDerive() => WebYunxiaoService.PipelineDeriveAsync();

    /// <summary>执行初始化（秒回 taskId，后台逐库推进）。</summary>
    /// <param name="payload">包装后的 <see cref="ExecRequest"/> JSON。</param>
    /// <returns><c>{ taskId }</c>（400 参数 / 409 同代号互斥 / 503 会话不可用）。</returns>
    [HttpPost]
    public Task<ActionResult> InitExec(string payload) => WebYunxiaoService.InitExecAsync(payload);

    /// <summary>任务态查询（前端 2s 轮询；taskId 取查询串）。</summary>
    /// <param name="payload">GET 无请求体；保留签名统一（未用）。</param>
    /// <returns><c>{ id, codename, status, libs, … }</c>；404 任务不存在。</returns>
    [HttpGet]
    public Task<ActionResult> InitTask(string payload) => WebYunxiaoService.InitTaskAsync(payload);

    /// <summary>重跑指定失败库（幂等复用同任务）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoRetryRequest"/> JSON。</param>
    /// <returns><c>{ taskId }</c>（404 任务/库不存在；409 执行上下文失效）。</returns>
    [HttpPost]
    public Task<ActionResult> InitRetry(string payload) => WebYunxiaoService.InitRetryAsync(payload);

    /// <summary>流水线预览（逐端 YAML diff）。</summary>
    /// <param name="payload">包装后的 <see cref="PipelinePreviewRequest"/> JSON。</param>
    /// <returns><c>{ items: [{ end, name, diff, warning? }] }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> PipelinePreview(string payload) => WebYunxiaoService.PipelinePreviewAsync(payload);

    /// <summary>流水线提交（逐端独立 create / update；覆盖须 confirm:true）。</summary>
    /// <param name="payload">包装后的 <see cref="PipelineCommitRequest"/> JSON。</param>
    /// <returns><c>{ results: [{ end, ok, pipelineId?, message? }] }</c>；409 覆盖未确认。</returns>
    [HttpPost]
    public Task<ActionResult> PipelineCommit(string payload) => WebYunxiaoService.PipelineCommitAsync(payload);

    /// <summary>项目初始化完成登记（代码库 done + 三端流水线就绪后落盘，供项目列表标注「已初始化」）。</summary>
    /// <param name="payload">包装后的 <see cref="ProjectInitRequest"/> JSON。</param>
    /// <returns><c>{ ok:true, data:true }</c>；缺项目 ID/标识 400。</returns>
    [HttpPost]
    public Task<ActionResult> ProjectInit(string payload) => WebYunxiaoService.ProjectInitAsync(payload);

    /// <summary>反查项目初始化完成登记（已初始化项目再次进入初始化页时反显配置）。</summary>
    /// <param name="payload">包装后的 <see cref="ProjectInitGetRequest"/> JSON。</param>
    /// <returns><c>{ ok:true, data:{...}|null }</c>；缺项目 ID/标识 400。</returns>
    [HttpPost]
    public Task<ActionResult> ProjectInitGet(string payload) => WebYunxiaoService.ProjectInitGetAsync(payload);

    // ── S3：PRD 同步 + 拆分落库 ────────────────────────────────────────────

    /// <summary>PRD 来源信息（项目名 / 可用用例库）。</summary>
    /// <returns><c>{ projectId, projectName, testRepos, hasTestRepo, message? }</c>；参数取查询串 <c>projectId</c>。</returns>
    [HttpGet]
    public Task<ActionResult> PrdSource() => WebYunxiaoService.PrdSourceAsync();

    /// <summary>同步云效需求（描述 / 验收标准 / 附件元数据）。</summary>
    /// <param name="payload">包装后的 <see cref="PrdSyncRequest"/> JSON。</param>
    /// <returns><c>{ projectId, status, requirements, attachments, warnings }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> PrdSync(string payload) => WebYunxiaoService.PrdSyncAsync(payload);

    /// <summary>拆分预览（同步来源 + 补充文档 → 一次性模型调用 → 结构化计划）。</summary>
    /// <param name="payload">包装后的 <see cref="PrdSplitRequest"/> JSON。</param>
    /// <returns><c>{ plan, syncStatus, requirementCount, documentNames, warnings }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> PrdSplitPreview(string payload) => WebYunxiaoService.PrdSplitPreviewAsync(payload);

    /// <summary>拆分提交（迭代 / 需求 / 任务 / 关系 / 用例 / 计划逐项落库，幂等重跑失败项）。</summary>
    /// <param name="payload">包装后的 <see cref="PrdSubmitRequest"/> JSON。</param>
    /// <returns><c>{ sprintId, receipts, okCount, failedCount, warnings }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> PrdSplitSubmit(string payload) => WebYunxiaoService.PrdSplitSubmitAsync(payload);

    // ── S3：迭代测试 F9（按来源生成用例 + 用例/测试计划落库） ────────────────

    /// <summary>按来源生成测试用例（一次性模型调用，不落库）。</summary>
    /// <param name="payload">包装后的 <see cref="IterationTestRequest"/> JSON。</param>
    /// <returns><c>{ cases, mergedCount, sourceRequirementCount, warnings }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> IterationTestPreview(string payload) => WebYunxiaoService.IterationTestPreviewAsync(payload);

    /// <summary>用例与测试计划落库（幂等重跑）。</summary>
    /// <param name="payload">包装后的 <see cref="IterationTestSaveRequest"/> JSON。</param>
    /// <returns><c>{ receipts, okCount, failedCount, testPlanId, warnings }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> IterationTestSave(string payload) => WebYunxiaoService.IterationTestSaveAsync(payload);

    // ── S4：迭代任务看板 F1（只读） ──────────────────────────────────────────

    /// <summary>迭代任务看板（迭代列表 + 迭代内任务 + 动态状态列）。</summary>
    /// <returns><c>{ projectName, sprints, columns, totalTasks, doneCount, overdueCount }</c>；参数取查询串。</returns>
    [HttpGet]
    public Task<ActionResult> Board() => WebYunxiaoService.BoardAsync();

    /// <summary>任务详情、工程上下文与首轮输入。</summary>
    /// <returns>任务聚合详情；参数取查询串 <c>projectId</c>/<c>workitemId</c>。</returns>
    [HttpGet]
    public Task<ActionResult> TaskDetail() => WebYunxiaoService.TaskDetailAsync();

    /// <summary>创建 Markdown 任务评论。</summary>
    /// <param name="payload">任务标识与评论正文 JSON。</param>
    /// <returns>新评论 ID。</returns>
    [HttpPost]
    public Task<ActionResult> TaskComment(string payload) => WebYunxiaoService.TaskCommentAsync(payload);

    /// <summary>读取任务当前有效的宿主会话绑定。</summary>
    [HttpGet]
    public Task<ActionResult> TaskSession() => WebYunxiaoService.TaskSessionAsync();

    /// <summary>保存或替换任务当前有效的宿主会话绑定。</summary>
    [HttpPost]
    public Task<ActionResult> TaskSessionSave(string payload) => WebYunxiaoService.TaskSessionSaveAsync(payload);
}
