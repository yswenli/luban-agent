/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： AgentRunHub
*描述：进行中轮次的登记中心：后台跑完、可被重新接入、同会话新轮次抢占、宿主关闭时统一取消
*
*****************************************************************************/

using System.Collections.Concurrent;

namespace LubanAgentCore.Hosting;

/// <summary>轮次中心参数。</summary>
public sealed record AgentRunHubOptions
{
    /// <summary>尾帧环形缓冲容量（帧数）；负数按 0 处理（不保留任何尾帧）。</summary>
    public int BacklogCapacity { get; init; } = 2000;

    /// <summary>单个订阅者的有界队列容量（帧数）。</summary>
    public int SubscriberCapacity { get; init; } = 512;

    /// <summary>抢占旧轮次时的等待上限。</summary>
    public TimeSpan PreemptTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>宿主关闭时等待各轮次收尾的上限。</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// 进行中轮次的登记中心。
/// </summary>
/// <remarks>
/// <para>
/// 轮次归属会话而非发起请求的客户端：断开连接只摘订阅者，不取消轮次。
/// 轮次只由四件事终止——跑完、显式取消、同会话新轮次抢占、宿主关闭。
/// </para>
/// <para>
/// 每个轮次独占一个作用域（<see cref="DiContainer"/>），因为 <c>IAgentHost</c> 是 Scoped。
/// 作用域在后台任务收尾时释放，先完成订阅者通道再释放，保证订阅者拿到的是「结束」而不是「异常断开」。
/// </para>
/// </remarks>
public sealed class AgentRunHub : IAsyncDisposable
{
    private readonly Func<string, DiContainer> _scopeFactory;
    private readonly AgentRunHubOptions _options;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<string, AgentRunHandle> _runs = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;

    /// <summary>构造轮次中心。</summary>
    /// <param name="scopeFactory">按用户派生作用域的工厂。</param>
    /// <param name="options">参数；null 用默认值。</param>
    /// <param name="log">日志出口；null 丢弃。</param>
    public AgentRunHub(Func<string, DiContainer> scopeFactory, AgentRunHubOptions? options = null, Action<string>? log = null)
    {
        _scopeFactory = scopeFactory;
        _options = options ?? new AgentRunHubOptions();
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// 开始一轮对话。同会话已有轮次时先取消它并等其收尾。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="body">对话体；收到轮次句柄与取消令牌。</param>
    /// <returns>本轮句柄。</returns>
    /// <exception cref="AgentRunConflictException">同会话上一轮在 <see cref="AgentRunHubOptions.PreemptTimeout"/> 内未收尾，本轮未能登记。</exception>
    public async Task<AgentRunHandle> StartAsync(
        string userId,
        string sessionId,
        Func<AgentRunHandle, CancellationToken, Task> body)
    {
        for (var attempt = 0; ; attempt++)
        {
            var key = RunKey(userId, sessionId);

            // 等旧轮次真正收尾再开新的：旧轮次仍在写库时会与新轮次抢同一会话
            // 不做主动移除——旧轮次收尾时自己摘登记，摘除前 Find 仍能命中它，重连方看到的是它的终态
            if (_runs.TryGetValue(key, out var previous))
            {
                previous.Cancel();
                try
                {
                    await previous.Completion.WaitAsync(_options.PreemptTimeout).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _log($"抢占等待旧轮次收尾超时：session={sessionId} run={previous.RunId}");
                }
                catch (Exception ex)
                {
                    _log($"抢占等待旧轮次异常：session={sessionId} run={previous.RunId} {ex.Message}");
                }
            }

            var scope = _scopeFactory(userId);
            if (scope.GetService(typeof(IAgentHost)) is not IAgentHost host)
            {
                await scope.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException("当前作用域解析不到 IAgentHost");
            }

            var runCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            var runId = Guid.NewGuid().ToString("N");
            var handle = new AgentRunHandle(runId, sessionId, userId, host, scope, runCts, _options, _log);

            // 并发 StartAsync 可能互相覆盖：只有值确实是自己的才继续
            if (!_runs.TryAdd(key, handle))
            {
                handle.Cancel();
                runCts.Dispose();
                await scope.DisposeAsync().ConfigureAwait(false);

                if (attempt >= 2)
                {
                    throw new AgentRunConflictException("同会话上一轮尚未结束，请先停止后再发送");
                }

                // 让出一次后重试：下一轮循环会取消当前登记者并按抢占流程等待它收尾
                await Task.Yield();
                continue;
            }

            // 后台跑本轮；Completion 在构造时就存在，收尾末尾才置位，抢占方不会读到「假完成」
            _ = Task.Run(async () =>
            {
                var stage = AgentRunStage.Done;
                try
                {
                    await body(handle, handle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (handle.Token.IsCancellationRequested)
                {
                    stage = AgentRunStage.Cancelled;
                }
                catch (Exception ex)
                {
                    stage = AgentRunStage.Failed;
                    _log($"轮次失败：session={sessionId} run={handle.RunId} {ex.Message}");
                }
                finally
                {
                    // 顺序固定：完成订阅者通道 → 释放作用域（含宿主实例） → 移除登记 → 置位 Completion
                    handle.Finish(stage);
                    await handle.DisposeAsync().ConfigureAwait(false);
                    _runs.TryRemove(new KeyValuePair<string, AgentRunHandle>(key, handle));
                    handle.MarkCompleted();
                }
            }, CancellationToken.None);

            return handle;
        }
    }

    /// <summary>查指定会话的进行中轮次。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>进行中的轮次；无则 <c>null</c>。</returns>
    public AgentRunHandle? Find(string userId, string sessionId)
        => _runs.TryGetValue(RunKey(userId, sessionId), out var handle) && !handle.IsCompleted ? handle : null;

    /// <summary>指定会话是否有轮次在跑。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>是否有轮次在跑。</returns>
    public bool IsRunning(string userId, string sessionId) => Find(userId, sessionId) is not null;

    /// <summary>给定会话中是否有任一在跑（工作区连带删除判定用）。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionIds">会话集合。</param>
    /// <returns>是否有任一在跑。</returns>
    public bool IsAnyRunning(string userId, IEnumerable<string> sessionIds)
        => sessionIds.Any(sessionId => IsRunning(userId, sessionId));

    /// <summary>取消指定会话的轮次。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="runId">轮次标识；非空时只有登记的仍是本轮才取消，避免迟到取消命中新轮次。</param>
    /// <returns>是否确有轮次被取消。</returns>
    public bool Cancel(string userId, string sessionId, string? runId = null)
    {
        if (!_runs.TryGetValue(RunKey(userId, sessionId), out var handle) || handle.IsCompleted)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(runId) && !string.Equals(handle.RunId, runId, StringComparison.Ordinal))
        {
            return false;
        }

        handle.Cancel();
        return true;
    }

    /// <summary>取消并等待轮次收尾。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="runId">轮次标识；语义同 <see cref="Cancel"/>。</param>
    /// <returns>轮次是否已确认收尾；超时或无需取消返回 <c>false</c>。</returns>
    public async Task<bool> CancelAndWaitAsync(string userId, string sessionId, string? runId = null)
    {
        if (!_runs.TryGetValue(RunKey(userId, sessionId), out var handle) || handle.IsCompleted)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(runId) && !string.Equals(handle.RunId, runId, StringComparison.Ordinal))
        {
            return false;
        }

        handle.Cancel();
        try
        {
            await handle.Completion.WaitAsync(_options.PreemptTimeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            _log($"取消等待轮次收尾超时：session={sessionId} run={handle.RunId}");
            return false;
        }
        catch (Exception ex)
        {
            _log($"取消等待轮次异常：session={sessionId} run={handle.RunId} {ex.Message}");
            return false;
        }
    }

    /// <summary>取消并等待全部轮次收尾；重复调用幂等。</summary>
    /// <returns>关闭任务。</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已释放，忽略
        }

        foreach (var handle in _runs.Values.ToList())
        {
            try
            {
                await handle.Completion.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _log($"关闭等待轮次收尾超时：session={handle.SessionId} run={handle.RunId}");
            }
            catch (Exception ex)
            {
                _log($"关闭等待轮次异常：session={handle.SessionId} run={handle.RunId} {ex.Message}");
            }
        }

        _shutdown.Dispose();
    }

    private static string RunKey(string userId, string sessionId) => $"{userId}\u0001{sessionId}";
}