/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services
*文件名： SessionBootstrap
*描述：宿主侧会话引导——切换工作区只恢复最近会话，需要会话载体的宿主在此显式补建
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 宿主侧会话引导。
/// </summary>
/// <remarks>
/// <see cref="IWorkspaceManager.SwitchWorkspaceAsync"/> 只恢复该工作区最近活跃的会话，不隐式新建。
/// CLI / 桌面端进入工作区后立刻需要会话载体（对话区、页脚统计、会话槽位绑定），
/// 因此在切换之后调用本类补建；Web 宿主不需要——未指定会话的对话请求由服务端按需新建。
/// </remarks>
public static class SessionBootstrap
{
    /// <summary>补建会话时使用的默认标题。</summary>
    public const string DefaultSessionTitle = "新会话";

    /// <summary>
    /// 确保当前活动会话存在：已有则原样返回，没有则为指定工作区新建一条。
    /// </summary>
    /// <param name="sessions">会话管理器（本地实现或远程客户端实现）。</param>
    /// <param name="workspaceId">新会话归属的工作区标识。</param>
    /// <param name="title">新会话标题；空白时取 <see cref="DefaultSessionTitle"/>。</param>
    /// <returns>当前活动会话。</returns>
    public static async Task<SessionInfo> EnsureAsync(
        ISessionManager sessions, string workspaceId, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var current = sessions.CurrentSession;
        if (current is not null && !string.IsNullOrWhiteSpace(current.SessionId))
        {
            return current;
        }

        return await sessions.CreateSessionAsync(
            string.IsNullOrWhiteSpace(title) ? DefaultSessionTitle : title,
            workspaceId).ConfigureAwait(false);
    }
}
