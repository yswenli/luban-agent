/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： AgentController
*描述：对话接口：非流式 / SSE 流式 / 确认 / 取消 / 重建
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 对话接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebAgentService"/>。
/// </remarks>
public sealed class AgentController : Controller
{
    /// <summary>非流式对话（不装配工具，返回正文 + 思考 + 附件摘要）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentChatRequest"/> JSON。</param>
    /// <returns>对话结果。</returns>
    [HttpPost]
    public Task<ActionResult> Send(string payload) => WebAgentService.SendAsync(payload);

    /// <summary>流式对话（SSE，装配工具与确认）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentChatRequest"/> JSON。</param>
    /// <returns>SSE 标记结果。</returns>
    [HttpPost]
    public Task<ActionResult> Chat(string payload) => WebAgentService.ChatAsync(payload);

    /// <summary>回传工具确认结果。</summary>
    /// <param name="payload">包装后的 <see cref="AgentConfirmRequest"/> JSON。</param>
    /// <returns>是否写入成功。</returns>
    [HttpPost]
    public Task<ActionResult> Confirm(string payload) => WebAgentService.ConfirmAsync(payload);

    /// <summary>取消指定会话正在进行的回合。</summary>
    /// <param name="payload">包装后的 <see cref="AgentCancelRequest"/> JSON。</param>
    /// <returns>是否确有回合被取消。</returns>
    [HttpPost]
    public Task<ActionResult> Cancel(string payload) => WebAgentService.CancelAsync(payload);

    /// <summary>丢弃该会话服务端缓存的 Agent（配置变更后按最新配置重建）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentResetRequest"/> JSON；<c>payload</c> 可为空。</param>
    /// <returns>固定成功。</returns>
    [HttpPost]
    public Task<ActionResult> Reset(string payload) => WebAgentService.ResetAsync(payload);
}