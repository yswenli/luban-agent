/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Utils
*文件名： FlushThrottle
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：流式刷新节流器。将高频回调合并为固定窗口（默认 16ms ~60fps）内的一次触发， 用于流式 token 追加场景减少重绘频率。
*
*=================================================
*修改标记
*修改时间：2026/8/11
*修改人： yswenli
*版本号： V1.0.0.0
*描述：流式刷新节流器。将高频回调合并为固定窗口（默认 16ms ~60fps）内的一次触发， 用于流式 token 追加场景减少重绘频率。
*
*****************************************************************************/

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

    /// <summary>
    /// 初始化流式刷新节流器。
    /// </summary>
    /// <param name="callback">合并窗口到期时执行一次的刷新回调。</param>
    /// <param name="interval">合并窗口时长，默认 16ms（约 60fps）。</param>
    public FlushThrottle(Action callback, TimeSpan? interval = null)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _interval = interval ?? TimeSpan.FromMilliseconds(16);
    }

    /// <summary>
    /// 请求一次刷新：窗口内重复调用只会合并为窗口到期时的一次回调；已释放时忽略。
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
    /// 立即执行挂起的刷新并取消尚未到期的窗口；没有挂起刷新或已释放时不做任何事。
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
        try { _callback(); }
        catch (Exception ex) { Logger.Error("FlushThrottle 回调异常", ex); }
    }

    private bool _disposed;

    /// <summary>
    /// 释放内部计时器并标记已释放，之后调度与刷新均不再触发回调。
    /// </summary>
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
