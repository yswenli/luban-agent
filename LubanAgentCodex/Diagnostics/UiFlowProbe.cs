/****************************************************************************
*临时诊断探针（方案 A）——只加日志、不改行为。
*用途：定位「流式过程中切换模型后，界面仍显示运行中但文字不再增长」。
*定位完成后：删除本文件 + 全仓搜索 [PROBE] / UiFlowProbe 清掉所有调用点。
*****************************************************************************/
using Avalonia.Threading;
using LuBan.Logging;

namespace LubanAgentCodex.Diagnostics;

/// <summary>
/// 临时诊断探针：给高频日志按 key 做时间窗采样，避免逐 token 刷盘。
/// </summary>
internal static class UiFlowProbe
{
    private sealed class Bucket
    {
        public long LastTick;
        public int Skipped;
    }

    private static readonly Dictionary<string, Bucket> _samples = new(StringComparer.Ordinal);
    private static readonly object _lock = new();

    /// <summary>按 key 采样输出：同一 key 在窗口内只写一条，并带上被合并的条数。</summary>
    public static void Sample(string key, string message, int intervalMs = 500)
    {
        var now = Environment.TickCount64;
        int skipped;
        lock (_lock)
        {
            if (!_samples.TryGetValue(key, out var bucket))
            {
                _samples[key] = new Bucket { LastTick = now };
                skipped = 0;
            }
            else
            {
                if (now - bucket.LastTick < intervalMs)
                {
                    bucket.Skipped++;
                    return;
                }
                skipped = bucket.Skipped;
                bucket.Skipped = 0;
                bucket.LastTick = now;
            }
        }

        Logger.Info($"[PROBE:{key}] {message} (合并{skipped}条)");
    }

    /// <summary>不采样，直接输出（用于状态切换点）。</summary>
    public static void Always(string key, string message)
    {
        Logger.Info($"[PROBE:{key}] {message}");
    }

    /// <summary>截断长文本，避免探测日志刷爆（参数、工具结果摘要用）。</summary>
    public static string Clip(string? text, int max = 100)
    {
        if (string.IsNullOrEmpty(text)) return "<空>";
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "…";
    }

    private static Timer? _uiWatchdog;
    private static int _uiHeartOk;
    private static long _uiLastLag;

    /// <summary>
    /// 启动 UI 线程心跳：后台每 2 秒往 Dispatcher 投递一个标记，标记执行时算排队延迟。
    /// 延迟持续增长 = UI 线程被占住/死锁（此时任何"文字不增长"都是假象）。
    /// </summary>
    public static void StartUiWatchdog()
    {
        if (_uiWatchdog != null) return;
        _uiWatchdog = new Timer(_ =>
        {
            var postedAt = Environment.TickCount64;
            Dispatcher.UIThread.Post(() =>
            {
                var lag = Environment.TickCount64 - postedAt;
                Interlocked.Exchange(ref _uiLastLag, lag);
                if (lag > 1000)
                {
                    Logger.Warn($"[PROBE:UIHEART] UI 线程排队={lag}ms 疑似卡住/死锁");
                }
                else if (Interlocked.Increment(ref _uiHeartOk) % 5 == 0)
                {
                    Logger.Info($"[PROBE:UIHEART] UI 心跳正常 排队={lag}ms");
                }
            }, DispatcherPriority.Background);
        }, null, 2000, 2000);
        Logger.Info("[PROBE:UIHEART] UI 线程心跳已启动（2s 一次）");
    }
}