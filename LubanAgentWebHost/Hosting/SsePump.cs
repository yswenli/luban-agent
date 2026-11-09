/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： SsePump
*描述：SSE 响应头/帧/心跳的写出（单写者，避免并发写同一 socket）
*
*****************************************************************************/

using System.Text;

using LubanAgentCore.Protocol.Events;

using SAEA.Http;
using SAEA.Http.Model;

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// SSE 写出器。
/// </summary>
/// <remarks>
/// <c>HttpResponse.SendData</c> 直接落 socket 且无内部锁，心跳与数据帧必须由同一循环串行写出；
/// 连接断开后写入会抛异常，调用方据此结束回合。
/// </remarks>
public static class SsePump
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>写出 SSE 响应头（无 Content-Length，保持长连接）。</summary>
    /// <param name="response">当前响应。</param>
    public static void Begin(HttpResponse response)
    {
        response.ContentType = "text/event-stream; charset=utf-8";
        response.SetHeader(ResponseHeaderType.CacheControl, "no-cache");
        response.SendHeader(-1);
    }

    /// <summary>写出一帧。</summary>
    /// <param name="response">当前响应。</param>
    /// <param name="frame">帧。</param>
    /// <returns>连接是否仍可写。</returns>
    public static bool TryWrite(HttpResponse response, AgentSseFrame frame)
        => TrySend(response, Utf8.GetBytes(frame.ToText()));

    /// <summary>写出心跳注释行。</summary>
    /// <param name="response">当前响应。</param>
    /// <returns>连接是否仍可写。</returns>
    public static bool TryWriteHeartbeat(HttpResponse response)
        => TrySend(response, Encoding.ASCII.GetBytes(AgentSseFrame.HeartbeatText));

    /// <summary>结束响应（关闭连接）。</summary>
    /// <param name="response">当前响应。</param>
    public static void Complete(HttpResponse response)
    {
        try
        {
            response.SendEnd();
        }
        catch (Exception)
        {
            // 连接已断：无需再处理
        }
    }

    private static bool TrySend(HttpResponse response, byte[] data)
    {
        try
        {
            response.SendData(data);
            return true;
        }
        catch (Exception)
        {
            // 对端已断开；由调用方结束回合
            return false;
        }
    }
}