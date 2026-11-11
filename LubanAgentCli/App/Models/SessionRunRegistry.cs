/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCli.App.Models
*文件名： SessionRunRegistry
*描述：进程级会话运行态登记；供 /session -clear 等全局命令判断是否有会话正在进行中
*
*****************************************************************************/

namespace LubanAgentCli.App.Models;

/// <summary>
/// 进程级会话运行态登记：记录哪些会话正在跑一轮对话。
/// </summary>
/// <remarks>
/// 只登记运行态，不持有轮次本身；会话的 <c>ConversationViewModel</c> 在进入/离开运行态时同步这里。
/// 全局命令据此拒绝「删掉正在跑的会话」这类操作。
/// </remarks>
public sealed class SessionRunRegistry
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    /// <summary>标记会话进入运行态（重复登记无副作用）。</summary>
    /// <param name="sessionId">会话标识。</param>
    public void Enter(string sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            _running[sessionId] = 0;
        }
    }

    /// <summary>标记会话离开运行态（未登记时无副作用）。</summary>
    /// <param name="sessionId">会话标识。</param>
    public void Exit(string sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            _running.TryRemove(sessionId, out _);
        }
    }

    /// <summary>是否有任一会话正在运行。</summary>
    public bool IsAnyRunning => !_running.IsEmpty;

    /// <summary>
    /// 指定工作区下是否有会话正在运行；级联删工作区（连删其下所有会话）前据此拒绝。
    /// </summary>
    /// <param name="query">会话与工作区绑定查询。</param>
    /// <param name="workspaceId">工作区标识。</param>
    /// <param name="userId">归属用户。</param>
    /// <returns>true 表示该工作区下存在运行中的会话。</returns>
    public async Task<bool> IsWorkspaceRunningAsync(ISessionWorkspaceQuery query, string workspaceId, string userId)
    {
        if (_running.IsEmpty || string.IsNullOrEmpty(workspaceId)) return false;

        var sessions = await query.GetByWorkspaceAsync(workspaceId, userId);
        return sessions.Any(session => _running.ContainsKey(session.SessionId));
    }
}