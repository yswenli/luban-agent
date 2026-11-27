/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services
*文件名： IWorkspaceSessionCleaner
*描述：按工作区清理会话的扩展能力
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 按工作区物理删除会话的扩展能力。
/// </summary>
/// <remarks>
/// <para>
/// 框架的 <see cref="LuBan.AIAgent.Sessions.ISessionManager.ClearAllSessionsAsync"/> 语义是
/// 「清空该用户全部会话」，CLI 等调用方依赖该语义，故不在其上叠加工作区过滤；
/// 需要「只清某个工作区」的宿主改用本接口。<see cref="SessionManager"/> 实现本接口。
/// </para>
/// </remarks>
public interface IWorkspaceSessionCleaner
{
    /// <summary>
    /// 物理删除指定工作区下当前用户的会话及其消息、worktree 登记，不重建活动会话。
    /// </summary>
    /// <param name="workspaceId">目标工作区。</param>
    Task ClearWorkspaceSessionsAsync(string workspaceId);
}