/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Infrastructure
*文件名： FlushThrottle
*版本号： V1.0.0.0
*唯一标识：流式刷新节流器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：合并高频 SetNeedsDisplay() 调用，窗口内只触发一次回调
*
*****************************************************************************/
namespace LubanAgent.Infrastructure;

/// <summary>
/// 流式刷新节流器。将高频回调合并为固定窗口（默认 16ms ~60fps）内的一次触发，
/// 用于流式 token 追加场景减少终端重绘频率。
/// </summary>
public sealed class FlushThrottle : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Action _callback;
    private Timer? _timer;
    private bool _scheduled;
    private readonly object _lock = new();

    /// <summary>
    /// 初始化节流器。
    /// </summary>
    /// <param name="interval">合并窗口时长，默认 16ms（~60fps）。</param>
    /// <param name="callback">节流后执行的刷新回调。</param>
    public FlushThrottle(Action callback, TimeSpan? interval = null)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _interval = interval ?? TimeSpan.FromMilliseconds(16);
    }

    /// <summary>
    /// 调度一次刷新。在 <paramref name="interval"/> 内的多次调用只触发一次回调。
    /// 线程安全：可从任意线程调用（Timer 回调在 ThreadPool 线程执行，
    /// 实际 View 更新由 callback 负责 marshal 到 UI 线程）。
    /// </summary>
    public void Schedule()
    {
        lock (_lock)
        {
            if (_disposed) return;

            if (_scheduled) return;
            _scheduled = true;
            _timer ??= new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_interval, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// 立即执行挂起的刷新并取消定时器。
    /// </summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_disposed || !_scheduled) return;
            _scheduled = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        ExecuteCallback();
    }

    private void Tick()
    {
        lock (_lock)
        {
            if (_disposed || !_scheduled) return;
            _scheduled = false;
        }

        ExecuteCallback();
    }

    private void ExecuteCallback()
    {
        try { _callback(); } catch { /* 回调异常由上层 errorHandler 处理 */ }
    }

    private bool _disposed;
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
