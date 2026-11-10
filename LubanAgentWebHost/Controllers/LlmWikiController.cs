/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： LlmWikiController
*描述：LLM Wiki 知识库接口（创建 / 删除 / 列文件 / 索引 / 检索 / 状态 / lint / 重建 / ingest / 清空）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// LLM Wiki 知识库接口，对应 <c>/llmwiki</c> 命令与 Codex 知识库窗口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebLlmWikiService"/>。
/// </remarks>
public sealed class LlmWikiController : Controller
{
    /// <summary>创建知识库工作区。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiCreateRequest"/> JSON。</param>
    /// <returns>新工作区。</returns>
    [HttpPost]
    public Task<ActionResult> Create(string payload) => WebLlmWikiService.CreateAsync(payload);

    /// <summary>删除知识库工作区并级联清理会话与索引。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>删除结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebLlmWikiService.DeleteAsync(payload);

    /// <summary>列出已索引文件。</summary>
    /// <param name="workspaceId">知识库工作区标识。</param>
    /// <returns>已索引文件列表。</returns>
    [HttpGet]
    public Task<ActionResult> ListFiles(string? workspaceId) => WebLlmWikiService.ListFilesAsync(workspaceId);

    /// <summary>同步索引原始资料。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiIndexRequest"/> JSON。</param>
    /// <returns>索引报告。</returns>
    /// <remarks>索引属长耗时动作，受宿主同步等待上限约束（<c>LubanAgentWeb:TimeoutSeconds</c>）。</remarks>
    [HttpPost]
    public Task<ActionResult> Index(string payload) => WebLlmWikiService.IndexAsync(payload);

    /// <summary>后台索引原始资料；宿主未启用后台任务服务时返回 null。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiIndexRequest"/> JSON。</param>
    /// <returns>后台任务信息。</returns>
    [HttpPost]
    public Task<ActionResult> StartIndex(string payload) => WebLlmWikiService.StartIndexAsync(payload);

    /// <summary>检索知识库。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiSearchRequest"/> JSON。</param>
    /// <returns>命中列表。</returns>
    [HttpPost]
    public Task<ActionResult> Search(string payload) => WebLlmWikiService.SearchAsync(payload);

    /// <summary>读取知识库统计。</summary>
    /// <param name="workspaceId">知识库工作区标识；为空时取宿主当前工作区。</param>
    /// <returns>统计。</returns>
    [HttpGet]
    public Task<ActionResult> Status(string? workspaceId) => WebLlmWikiService.StatusAsync(workspaceId);

    /// <summary>客观项 lint。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>lint 报告。</returns>
    [HttpPost]
    public Task<ActionResult> Lint(string payload) => WebLlmWikiService.LintAsync(payload);

    /// <summary>同步重建 wiki 向量索引。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>重建报告。</returns>
    [HttpPost]
    public Task<ActionResult> Rebuild(string payload) => WebLlmWikiService.RebuildAsync(payload);

    /// <summary>后台重建 wiki 向量索引；宿主未启用后台任务服务时返回 null。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>后台任务信息。</returns>
    [HttpPost]
    public Task<ActionResult> StartRebuild(string payload) => WebLlmWikiService.StartRebuildAsync(payload);

    /// <summary>同步提取 ingest 源文件并合并为工作区内临时 markdown。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiIngestRequest"/> JSON。</param>
    /// <returns>合并草稿。</returns>
    [HttpPost]
    public Task<ActionResult> PrepareIngest(string payload) => WebLlmWikiService.PrepareIngestAsync(payload);

    /// <summary>后台提取 ingest 源文件；宿主未启用后台任务服务时返回 null。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiIngestRequest"/> JSON。</param>
    /// <returns>后台任务信息。</returns>
    [HttpPost]
    public Task<ActionResult> StartIngest(string payload) => WebLlmWikiService.StartIngestAsync(payload);

    /// <summary>清空指定工作区的文件与切块索引（不删除工作区记录）。</summary>
    /// <param name="payload">包装后的 <see cref="LlmWikiWorkspaceRequest"/> JSON。</param>
    /// <returns>清空结果。</returns>
    [HttpPost]
    public Task<ActionResult> Clear(string payload) => WebLlmWikiService.ClearAsync(payload);
}