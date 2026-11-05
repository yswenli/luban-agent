/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： AgentEventStreamResult
*描述：SSE 动作的返回值标记（数据已在 action 内直写响应流）
*
*****************************************************************************/

using SAEA.Http.Model;
using SAEA.MVC;

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// SSE 动作的返回值。
/// </summary>
/// <remarks>
/// SAEA 对 <see cref="IEventStream"/> 结果不再补写响应体；帧由动作内部经
/// <see cref="SsePump"/> 直接写入连接，故本类型只作标记。
/// </remarks>
public sealed class AgentEventStreamResult : ActionResult, IEventStream
{
}