/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Protocol
*文件名： IA2AStatusProvider
*描述：A2A 运行状态的只读查询契约（本机实现与 HTTP 客户端共用）
*
*****************************************************************************/

using LubanAgent.Protocol.Dtos;

namespace LubanAgent.Protocol;

/// <summary>
/// A2A 运行状态查询。
/// </summary>
/// <remarks>
/// A2A 运行时只在承载回合的进程里启动（当前是 WebHost），随工作区就绪启动、随进程退出停止；
/// 客户端只读状态，界面不提供启停入口。
/// </remarks>
public interface IA2AStatusProvider
{
    /// <summary>读取当前 A2A 状态。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>状态快照。</returns>
    Task<A2AStatusDto> GetStatusAsync(CancellationToken ct = default);
}