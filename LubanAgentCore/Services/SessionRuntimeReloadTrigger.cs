/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： SessionRuntimeReloadTrigger
*版本号： V1.0.0.0
*唯一标识：会话级 Agent 重载触发器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/14
*描述：实现 IAgentReloadTrigger，在工具修改工作区级配置（Skill/MCP）后触发延迟重载。
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCore.Services;

/// <summary>
/// 会话级 Agent 重载触发器：实现 <see cref="IAgentReloadTrigger"/>，
/// 通过调用当前会话的 <see cref="SessionRuntime.RequestReload"/> 标记延迟重载。
/// </summary>
/// <remarks>
/// 此类为 Scoped 服务，在会话作用域内解析，依赖 <see cref="ISessionContext"/> 获取当前会话标识。
/// </remarks>
public sealed class SessionRuntimeReloadTrigger : IAgentReloadTrigger
{
    private readonly SessionRuntimeManager _runtimeManager;
    private readonly ISessionContext _sessionContext;

    /// <summary>
    /// 创建会话级重载触发器。
    /// </summary>
    /// <param name="runtimeManager">会话运行时管理器（root 单例）。</param>
    /// <param name="sessionContext">当前会话上下文（session-scoped）。</param>
    public SessionRuntimeReloadTrigger(SessionRuntimeManager runtimeManager, ISessionContext sessionContext)
    {
        _runtimeManager = runtimeManager;
        _sessionContext = sessionContext;
    }

    /// <inheritdoc />
    public void RequestReload()
    {
        var sessionId = _sessionContext.SessionId;
        if (string.IsNullOrEmpty(sessionId)) return;

        var runtime = _runtimeManager.Get(sessionId);
        runtime?.RequestReload();
    }
}