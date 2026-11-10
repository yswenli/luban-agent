/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： SessionController
*描述：会话接口：列表 / 详情 / 增删改 / 消息 / 统计
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 会话接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebSessionService"/>。
/// 会话归属校验统一走 <see cref="SessionRepository.GetOwnerUserIdAsync"/>：
/// 不存在按 404、不属于当前用户按 403，与对话接口同口径。
/// </remarks>
public sealed class SessionController : Controller
{
    /// <summary>当前用户的会话列表。</summary>
    /// <returns>会话列表。</returns>
    [HttpGet]
    public Task<ActionResult> List() => WebSessionService.ListAsync();

    /// <summary>当前用户的会话摘要列表（含工作区与模型，浏览器端会话列表用）。</summary>
    /// <returns>按更新时间倒序的会话摘要。</returns>
    [HttpGet]
    public Task<ActionResult> ListDetailed() => WebSessionService.ListDetailedAsync();

    /// <summary>指定工作区下的会话列表（更新时间倒序）。</summary>
    /// <param name="workspaceId">工作区标识。</param>
    /// <returns>会话列表。</returns>
    [HttpGet]
    public Task<ActionResult> ListByWorkspace(string? workspaceId) => WebSessionService.ListByWorkspaceAsync(workspaceId);

    /// <summary>批量取各会话首条用户消息预览。</summary>
    /// <param name="sessionIds">逗号分隔的会话标识。</param>
    /// <param name="maxLength">预览最大长度；缺省 60。</param>
    /// <returns>会话标识到预览文本的映射。</returns>
    [HttpGet]
    public Task<ActionResult> Previews(string? sessionIds, string? maxLength)
        => WebSessionService.PreviewsAsync(sessionIds, maxLength);

    /// <summary>按标识取会话。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>会话详情。</returns>
    [HttpGet]
    public Task<ActionResult> Get(string? sessionId) => WebSessionService.GetAsync(sessionId);

    /// <summary>当前活动会话。</summary>
    /// <returns>会话详情；尚未确定活动会话时 data 为 null。</returns>
    /// <remarks>
    /// 工作区切换后服务端会恢复/新建活动会话，客户端据此刷新本地镜像（远程模式下客户端不持有会话数据）。
    /// </remarks>
    [HttpGet]
    public Task<ActionResult> Current() => WebSessionService.CurrentAsync();

    /// <summary>会话绑定的工作区标识。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>工作区标识；会话不存在或未绑定工作区时 data 为 null。</returns>
    [HttpGet]
    public Task<ActionResult> WorkspaceId(string? sessionId) => WebSessionService.WorkspaceIdAsync(sessionId);

    /// <summary>新建会话。</summary>
    /// <param name="payload">包装后的 <see cref="SessionCreateRequest"/> JSON；可为空。</param>
    /// <returns>新会话。</returns>
    [HttpPost]
    public Task<ActionResult> Create(string payload) => WebSessionService.CreateAsync(payload);

    /// <summary>删除会话。</summary>
    /// <param name="payload">包装后的 <see cref="SessionIdRequest"/> JSON。</param>
    /// <returns>删除结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebSessionService.DeleteAsync(payload);

    /// <summary>重命名会话。</summary>
    /// <param name="payload">包装后的 <see cref="SessionRenameRequest"/> JSON。</param>
    /// <returns>重命名结果。</returns>
    [HttpPost]
    public Task<ActionResult> Rename(string payload) => WebSessionService.RenameAsync(payload);

    /// <summary>会话消息。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="limit">限制条数；缺省取全部。</param>
    /// <returns>消息列表。</returns>
    [HttpGet]
    public Task<ActionResult> Messages(string? sessionId, string? limit)
        => WebSessionService.MessagesAsync(sessionId, limit);

    /// <summary>会话最近 N 条消息。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="count">条数；缺省 20。</param>
    /// <returns>消息列表（时间正序）。</returns>
    [HttpGet]
    public Task<ActionResult> LatestMessages(string? sessionId, string? count)
        => WebSessionService.LatestMessagesAsync(sessionId, count);

    /// <summary>会话活跃消息（未压缩）。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>消息列表。</returns>
    [HttpGet]
    public Task<ActionResult> ActiveMessages(string? sessionId) => WebSessionService.ActiveMessagesAsync(sessionId);

    /// <summary>追加一条消息。</summary>
    /// <param name="payload">包装后的 <see cref="SessionAddMessageRequest"/> JSON。</param>
    /// <returns>新消息。</returns>
    [HttpPost]
    public Task<ActionResult> AddMessage(string payload) => WebSessionService.AddMessageAsync(payload);

    /// <summary>标记消息为已压缩。</summary>
    /// <param name="payload">包装后的 <see cref="SessionCompactionRequest"/> JSON。</param>
    /// <returns>标记结果。</returns>
    [HttpPost]
    public Task<ActionResult> MarkCompacted(string payload) => WebSessionService.MarkCompactedAsync(payload);

    /// <summary>会话统计。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>统计信息。</returns>
    [HttpGet]
    public Task<ActionResult> Stats(string? sessionId) => WebSessionService.StatsAsync(sessionId);

    /// <summary>全局统计。</summary>
    /// <param name="days">限定最近 N 天；缺省统计全部。</param>
    /// <returns>全局统计。</returns>
    [HttpGet]
    public Task<ActionResult> GlobalStats(string? days) => WebSessionService.GlobalStatsAsync(days);

    /// <summary>清空会话消息。</summary>
    /// <param name="payload">包装后的 <see cref="SessionIdRequest"/> JSON。</param>
    /// <returns>清空结果。</returns>
    [HttpPost]
    public Task<ActionResult> ClearMessages(string payload) => WebSessionService.ClearMessagesAsync(payload);

    /// <summary>物理删除全部会话（可重建一个空会话）。</summary>
    /// <param name="payload">包装后的 <see cref="SessionCreateRequest"/> JSON；可为空。</param>
    /// <returns>清空结果。</returns>
    [HttpPost]
    public Task<ActionResult> ClearAll(string payload) => WebSessionService.ClearAllAsync(payload);

    /// <summary>切换当前会话（仅影响宿主内的「活动会话」指针）。</summary>
    /// <param name="payload">包装后的 <see cref="SessionIdRequest"/> JSON。</param>
    /// <returns>切换结果。</returns>
    [HttpPost]
    public Task<ActionResult> Switch(string payload) => WebSessionService.SwitchAsync(payload);
}