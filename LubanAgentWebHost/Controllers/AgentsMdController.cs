/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： AgentsMdController
*描述：工作区 AGENTS.md 初始化接口（现状 / 生成 / 保存）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 工作区 AGENTS.md 初始化接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebAgentsMdService"/>。
/// </remarks>
public sealed class AgentsMdController : Controller
{
    /// <summary>读取目标工作区的 AGENTS.md 现状。</summary>
    /// <param name="payload">包装后的 <see cref="AgentsMdInfoRequest"/> JSON。</param>
    /// <returns>现状。</returns>
    [HttpPost]
    public Task<ActionResult> Info(string payload) => WebAgentsMdService.InfoAsync(payload);

    /// <summary>调用模型生成并落盘。</summary>
    /// <param name="payload">包装后的 <see cref="AgentsMdGenerateRequest"/> JSON。</param>
    /// <returns>落盘结果。</returns>
    [HttpPost]
    public Task<ActionResult> Generate(string payload) => WebAgentsMdService.GenerateAsync(payload);

    /// <summary>保存用户编辑后的正文。</summary>
    /// <param name="payload">包装后的 <see cref="AgentsMdSaveRequest"/> JSON。</param>
    /// <returns>落盘结果。</returns>
    [HttpPost]
    public Task<ActionResult> Save(string payload) => WebAgentsMdService.SaveAsync(payload);
}