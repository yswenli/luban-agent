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

    // ---- 临时诊断（方案 A）：定位切模型后流式假停，定位后随 PROBE 日志一并删除 ----
    private long _diagScheduleCount;
    private long _diagTickCount;
    private long _diagFlushCount;
    private long _diagLastScheduleTick;
    private long _diagLastTickTick;
    // -------------------------------------------------------------------------

    public FlushThrottle(Action callback, TimeSpan? interval = null)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _interval = interval ?? TimeSpan.FromMilliseconds(16);
        Logger.Info($"[PROBE:THROTTLE] 创建 interval={_interval.TotalMilliseconds}ms 线程={Environment.CurrentManagedThreadId}");
    }

    public void Schedule()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                Logger.Warn("[PROBE:THROTTLE] Schedule 命中已释放的节流器，本次刷新被丢弃");
                return;
            }
            if (_scheduled)
            {
                // 诊断：已排定但长时间没有 Tick，说明定时器或 UI 线程被卡住
                var stuck = Environment.TickCount64 - _diagLastScheduleTick;
                if (stuck > 2000)
                    Logger.Warn($"[PROBE:THROTTLE] Schedule 疑似卡住 已挂起={stuck}ms 距上次Tick={Environment.TickCount64 - _diagLastTickTick}ms schedule={_diagScheduleCount} tick={_diagTickCount}");
                return;
            }
            _scheduled = true;
            _timer ??= new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_interval, Timeout.InfiniteTimeSpan);
            _diagLastScheduleTick = Environment.TickCount64;
            if (++_diagScheduleCount % 60 == 0)
                Logger.Info($"[PROBE:THROTTLE] Schedule 累计={_diagScheduleCount} tick={_diagTickCount} flush={_diagFlushCount}");
        }
    }

    public void Flush()
    {
        long seq;
        lock (_lock)
        {
            if (_disposed || !_scheduled) return;
            _scheduled = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            seq = ++_diagFlushCount;
        }
        Logger.Info($"[PROBE:THROTTLE] Flush 第{seq}次 schedule={_diagScheduleCount} tick={_diagTickCount} 线程={Environment.CurrentManagedThreadId}");
        ExecuteCallback();
    }

    private void Tick()
    {
        _diagLastTickTick = Environment.TickCount64;
        long seq;
        lock (_lock)
        {
            if (_disposed || !_scheduled)
            {
                Logger.Warn($"[PROBE:THROTTLE] Tick 空转 disposed={_disposed} scheduled={_scheduled} 线程={Environment.CurrentManagedThreadId}");
                return;
            }
            _scheduled = false;
            seq = ++_diagTickCount;
        }
        if (seq % 30 == 0)
            Logger.Info($"[PROBE:THROTTLE] Tick 累计={seq} schedule={_diagScheduleCount} 线程={Environment.CurrentManagedThreadId}");
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
        Logger.Info($"[PROBE:THROTTLE] 释放 schedule={_diagScheduleCount} tick={_diagTickCount} flush={_diagFlushCount} scheduled={_scheduled}");
        lock (_lock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
