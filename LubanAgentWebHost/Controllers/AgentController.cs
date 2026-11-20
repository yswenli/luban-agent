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

    /// <summary>探活：该会话是否有正在进行的回合。</summary>
    /// <param name="payload">包装后的 <see cref="AgentActiveRequest"/> JSON。</param>
    /// <returns><c>{ active, runId, startedAt }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> Active(string payload) => WebAgentService.ActiveAsync(payload);

    /// <summary>接上该会话正在进行的回合（SSE）；无活跃回合返回 204。</summary>
    /// <param name="payload">包装后的 <see cref="AgentAttachRequest"/> JSON。</param>
    /// <returns>SSE 标记结果。</returns>
    [HttpPost]
    public Task<ActionResult> Attach(string payload) => WebAgentService.AttachAsync(payload);

    /// <summary>回传工具确认结果。</summary>
    /// <param name="payload">包装后的 <see cref="AgentConfirmRequest"/> JSON。</param>
    /// <returns>是否写入成功。</returns>
    [HttpPost]
    public Task<ActionResult> Confirm(string payload) => WebAgentService.ConfirmAsync(payload);

    /// <summary>读取会话当前生效的思考档位与上下文预算档位。</summary>
    /// <param name="sessionId">会话标识；缺省返回全局默认与全局模型的档位。</param>
    /// <returns>生效值、可选档位与模型窗口。</returns>
    [HttpGet]
    public Task<ActionResult> SessionSettings(string? sessionId) => WebAgentService.SessionSettingsAsync(sessionId);

    /// <summary>写入思考档位与上下文预算档位：给定会话写会话覆盖，缺省会话写全局默认。</summary>
    /// <param name="payload">包装后的 <see cref="SessionGenerationSettingsRequest"/> JSON。</param>
    /// <returns>保存结果。</returns>
    [HttpPost]
    public Task<ActionResult> SaveSessionSettings(string payload)
        => WebAgentService.SaveSessionSettingsAsync(payload);

    /// <summary>取消指定会话正在进行的回合。</summary>
    /// <param name="payload">包装后的 <see cref="AgentCancelRequest"/> JSON。</param>
    /// <returns>是否确有回合被取消。</returns>
    [HttpPost]
    public Task<ActionResult> Cancel(string payload) => WebAgentService.CancelAsync(payload);

    /// <summary>把输入框内容改写成更清晰的需求表述（不装配工具、不读会话历史）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentOptimizeRequest"/> JSON。</param>
    /// <returns>改写结果。</returns>
    [HttpPost]
    public Task<ActionResult> Optimize(string payload) => WebAgentService.OptimizeAsync(payload);

    /// <summary>把一段对话内容概括成不超过 15 字的会话主题（不装配工具、不读会话历史）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentAutoSessionNameRequest"/> JSON。</param>
    /// <returns>主题（标题）。</returns>
    [HttpPost]
    public Task<ActionResult> AutoSessionName(string payload) => WebAgentService.AutoSessionNameAsync(payload);

    /// <summary>丢弃该会话服务端缓存的 Agent（配置变更后按最新配置重建）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentResetRequest"/> JSON；<c>payload</c> 可为空。</param>
    /// <returns>固定成功。</returns>
    [HttpPost]
    public Task<ActionResult> Reset(string payload) => WebAgentService.ResetAsync(payload);
}