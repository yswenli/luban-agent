/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： SsePump
*描述：SSE 响应头/帧/心跳的写出（单写者，避免并发写同一 socket）
*
*****************************************************************************/

using System.Threading.Channels;

using SAEA.Sockets.Interface;

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// SSE 写出器。
/// </summary>
/// <remarks>
/// <c>HttpResponse.SendData</c> 直接落 socket 且无内部锁，心跳与数据帧必须由同一循环串行写出。
/// 对端断开时它既不抛异常也不写出，只在内部对每帧调用 <c>OnError</c> 记一条 “Sending data timeout”，
/// 故每次写出前先用连接状态判定，断连后立即结束该订阅者，否则一次断连会按帧刷出成百条错误。
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
    /// <param name="connection">本次请求的连接载体；为 null 时不判定连接状态。</param>
    /// <param name="frame">帧。</param>
    /// <returns>连接是否仍可写。</returns>
    public static bool TryWrite(HttpResponse response, IUserToken? connection, AgentSseFrame frame)
        => TrySend(response, connection, Utf8.GetBytes(frame.ToText()));

    /// <summary>写出心跳注释行。</summary>
    /// <param name="response">当前响应。</param>
    /// <param name="connection">本次请求的连接载体；为 null 时不判定连接状态。</param>
    /// <returns>连接是否仍可写。</returns>
    public static bool TryWriteHeartbeat(HttpResponse response, IUserToken? connection)
        => TrySend(response, connection, Encoding.ASCII.GetBytes(AgentSseFrame.HeartbeatText));

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
    /// <param name="connection">本次请求的连接载体；为 null 时不判定连接状态。</param>
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
        IUserToken? connection,
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

                if (!TryWriteHeartbeat(response, connection))
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
                if (!TryWrite(response, connection, frame))
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

    /// <summary>连接是否仍可写。</summary>
    /// <remarks>
    /// 载体的 socket 已在断开时被回收（<c>Socket</c> 置空）或标记为未连接，
    /// 这两种情况 SAEA 都会静默丢弃数据并在内部记一条错误，故这里直接判定为不可写。
    /// 载体为 null 表示调用方没提供连接信息，按旧行为尝试写出。
    /// </remarks>
    /// <param name="connection">本次请求的连接载体。</param>
    /// <returns>是否可写。</returns>
    private static bool IsWritable(IUserToken? connection)
        => connection is null || connection.Socket is { Connected: true };

    private static bool TrySend(HttpResponse response, IUserToken? connection, byte[] data)
    {
        if (!IsWritable(connection))
        {
            return false;
        }

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