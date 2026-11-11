/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： AgentRunConflictException
*描述：同会话轮次登记冲突：上一轮在抢占等待上限内未收尾，本轮未能登记
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 轮次登记冲突。
/// </summary>
/// <remarks>
/// <see cref="AgentRunHub.StartAsync"/> 抢占同会话旧轮次后，若旧轮次在
/// <see cref="AgentRunHubOptions.PreemptTimeout"/> 内仍未收尾（未释放作用域、未摘除登记），
/// 新一轮无法登记即抛本异常；调用方可据此回 409（冲突、稍后重试），而非笼统的 5xx。
/// </remarks>
public sealed class AgentRunConflictException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="message">冲突原因。</param>
    public AgentRunConflictException(string message)
        : base(message)
    {
    }

    /// <summary>创建异常。</summary>
    /// <param name="message">冲突原因。</param>
    /// <param name="inner">内部异常。</param>
    public AgentRunConflictException(string message, Exception? inner)
        : base(message, inner)
    {
    }
}