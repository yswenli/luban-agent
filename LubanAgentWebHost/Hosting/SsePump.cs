/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： SsePump
*描述：SSE 响应头/帧/心跳的写出（单写者，避免并发写同一 socket）
*
*****************************************************************************/

using System.Threading.Channels;

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

    /// <summary>
    /// 订阅者 pump：从轮次通道取帧写 socket，空闲时按心跳间隔补 <c>: ping</c>。
    /// </summary>
    /// <param name="response">当前响应。</param>
    /// <param name="reader">该订阅者的帧通道。</param>
    /// <param name="heartbeat">心跳间隔。</param>
    /// <param name="ct">退出令牌（宿主关闭时用）。</param>
    /// <returns>通道完成或写失败时返回。</returns>
    /// <remarks>
    /// 心跳由每个订阅者自己的 pump 发出，不进轮次编号、不进重放缓冲：
    /// 否则重连拿到的序号里会混进无人消费的占位帧。
    /// </remarks>
    public static async Task RunAsync(
        HttpResponse response,
        ChannelReader<AgentSseFrame> reader,
        TimeSpan heartbeat,
        CancellationToken ct = default)
    {
        using var timer = new PeriodicTimer(heartbeat);
        var tick = timer.WaitForNextTickAsync(ct).AsTask();
        // 读任务只在被消费后重建：心跳分支继续等待同一个 read，
        // 否则每个心跳周期都会丢掉一个没人 await 的 WaitToReadAsync
        var read = reader.WaitToReadAsync(ct).AsTask();
        while (true)
        {
            var winner = await Task.WhenAny(read, tick).ConfigureAwait(false);

            if (ReferenceEquals(winner, tick))
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (!TryWriteHeartbeat(response))
                {
                    return;
                }

                tick = timer.WaitForNextTickAsync(ct).AsTask();
                continue;
            }

            bool more;
            try
            {
                more = await read.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (reader.TryRead(out var frame))
            {
                if (!TryWrite(response, frame))
                {
                    return;
                }
            }

            if (!more)
            {
                // 轮次收尾：通道已完成，帧已排空
                return;
            }

            read = reader.WaitToReadAsync(ct).AsTask();
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