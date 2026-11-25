/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： ApiRetryScope
*描述：把框架的自动重试通知路由到当前回合的 SSE 旁路队列
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 一轮对话内的自动重试提示作用域。
/// </summary>
/// <remarks>
/// <para>
/// 重试回调挂在 <see cref="LuBanAgentOptions.OnApiRetry"/> 上，而配置实例是按用户共享的单例；
/// 同一用户可能同时跑多个会话回合，直接把回调写成「投递到我的队列」会串台。
/// 因此回调固定为 <see cref="Publish"/>，真正的出口用 <see cref="AsyncLocal{T}"/> 挂在
/// 发起重试的那条异步链上：谁在重试，提示就进谁的回合流。
/// </para>
/// <para>
/// 提示只是旁路信息：不进 <c>AgentRunHandle</c> 快照，重连后不会重放。
/// </para>
/// </remarks>
public static class ApiRetryScope
{
    private static readonly AsyncLocal<Action<AgentRetryNotice>?> Sink = new();

    /// <summary>
    /// 进入作用域并绑定本回合的重试提示出口；释放时恢复进入前的绑定。
    /// </summary>
    /// <param name="sink">提示出口。</param>
    /// <returns>用于退出作用域的令牌。</returns>
    public static IDisposable Begin(Action<AgentRetryNotice> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var previous = Sink.Value;
        Sink.Value = sink;
        return new RestoreToken(previous);
    }

    /// <summary>把重试通知投递给当前回合；无绑定时静默丢弃（框架已自行写日志）。</summary>
    /// <param name="notice">重试通知。</param>
    public static void Publish(AgentRetryNotice notice)
        => Sink.Value?.Invoke(notice);

    /// <summary>把重试通知转成 <c>api.retry</c> 帧。</summary>
    /// <param name="notice">重试通知。</param>
    /// <returns>SSE 帧。</returns>
    public static AgentSseFrame CreateFrame(AgentRetryNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return AgentSseFrame.Create(AgentSseEventNames.ApiRetry, new
        {
            category = notice.Category.ToString(),
            label = ApiErrorClassifier.GetCategoryLabel(notice.Category),
            attempt = notice.Attempt,
            maxAttempts = notice.MaxAttempts,
            delayMs = (int)Math.Round(notice.Delay.TotalMilliseconds)
        });
    }

    /// <summary>恢复进入作用域前的出口。</summary>
    private sealed class RestoreToken(Action<AgentRetryNotice>? previous) : IDisposable
    {
        private int _disposed;

        /// <summary>还原上一个出口；重复调用无副作用。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            Sink.Value = previous;
        }
    }
}