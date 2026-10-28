using LuBan.Logging;
using Timeout = System.Threading.Timeout;

namespace LubanAgentCore.Utils;

/// <summary>
/// 流式刷新节流器。将高频回调合并为固定窗口（默认 16ms ~60fps）内的一次触发，
/// 用于流式 token 追加场景减少重绘频率。
/// </summary>
public sealed class FlushThrottle : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Action _callback;
    private Timer? _timer;
    private bool _scheduled;
    private readonly object _lock = new();

    public FlushThrottle(Action callback, TimeSpan? interval = null)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _interval = interval ?? TimeSpan.FromMilliseconds(16);
    }

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
        try { _callback(); }
        catch (Exception ex) { Logger.Error("FlushThrottle 回调异常", ex); }
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
