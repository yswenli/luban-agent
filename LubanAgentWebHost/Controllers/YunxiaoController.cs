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
}
