/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.Infrastructure
*文件名： TuiDiag
*版本号： V1.0.0.0
*唯一标识：TUI 性能诊断埋点
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/13
*描述：主循环迭代与各渲染环节的耗时诊断，LuBanAgent:DebugMode=true 时生效
*
*****************************************************************************/
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LubanAgentCli.Infrastructure;

/// <summary>
/// TUI 性能诊断埋点。在 appsettings.json 中设置 <c>LuBanAgent:DebugMode=true</c> 后生效：
/// 主循环每次迭代检查周期（&gt;150ms 视为慢迭代），并聚合输出各埋点的
/// 次数/总耗时/最大耗时到 <c>Logger.Warn</c>。未启用时所有调用为零分配空操作。
/// </summary>
internal static class TuiDiag
{
    /// <summary>诊断是否启用。由启动流程从配置 <c>LuBanAgent:DebugMode</c> 赋值。</summary>
    public static bool Enabled { get; set; }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _lastTickMs = -1;
    private static readonly ConcurrentDictionary<string, Stat> _stats = new(StringComparer.Ordinal);

    /// <summary>Agent 是否正在流式输出（由 ConversationViewModel 维护）。</summary>
    public static volatile bool AgentRunning;

    private static long _lastKeyMs = -1;

    /// <summary>
    /// 按键到达视图时调用：记录相邻按键到达间隔，成簇到达（大间隔后紧跟小间隔）说明输入投递延迟。
    /// </summary>
    public static void KeyArrival()
    {
        if (!Enabled) return;

        var now = Clock.ElapsedMilliseconds;
        var prev = Interlocked.Exchange(ref _lastKeyMs, now);
        if (prev < 0) return;

        var gap = now - prev;
        if (gap >= 250)
        {
            Logger.Warn($"[TuiDiag] key gap={gap}ms");
        }
    }

    private sealed class Stat
    {
        public long Count;
        public long TotalMs;
        public long MaxMs;
        public string? LastNote;
    }

    /// <summary>
    /// 主循环迭代钩子（订阅 IApplication.Iteration）。周期超过 150ms 时输出窗口统计。
    /// </summary>
    public static void IterationTick()
    {
        if (!Enabled) return;

        var now = Clock.ElapsedMilliseconds;
        var prev = Interlocked.Exchange(ref _lastTickMs, now);
        if (prev < 0) return;

        var period = now - prev;
        if (period < 150) return;

        var parts = _stats.IsEmpty
            ? "(no instrumented ops)"
            : string.Join(" ", _stats.Select(kv =>
            {
                var s = kv.Value;
                lock (s)
                {
                    return $"{kv.Key}:n={s.Count},tot={s.TotalMs}ms,max={s.MaxMs}ms" +
                           (s.LastNote is null ? "" : $"[{s.LastNote}]");
                }
            }));

        Logger.Warn($"[TuiDiag] slow iteration period={period}ms agentRunning={AgentRunning} | {parts}");

        _stats.Clear();
    }

    /// <summary>
    /// 记录一次操作耗时（超过 thresholdMs 才计入窗口统计）。
    /// </summary>
    public static void Record(string name, long elapsedMs, string? note = null, long thresholdMs = 20)
    {
        if (!Enabled || elapsedMs < thresholdMs) return;

        var s = _stats.GetOrAdd(name, _ => new Stat());
        lock (s)
        {
            s.Count++;
            s.TotalMs += elapsedMs;
            if (elapsedMs > s.MaxMs) s.MaxMs = elapsedMs;
            if (note is not null) s.LastNote = note;
        }
    }

    /// <summary>
    /// 测量一个代码块耗时，Dispose 时计入统计。
    /// </summary>
    public static Scope Measure(string name, string? note = null, long thresholdMs = 20)
        => new(name, note, thresholdMs);

    /// <summary>测量作用域。</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string _name;
        private readonly string? _note;
        private readonly long _threshold;
        private readonly long _start;

        public Scope(string name, string? note, long thresholdMs)
        {
            _name = name;
            _note = note;
            _threshold = thresholdMs;
            _start = Enabled ? Stopwatch.GetTimestamp() : 0;
        }

        public void Dispose()
        {
            if (_start == 0) return;
            var ms = (long)((Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency);
            Record(_name, ms, _note, _threshold);
        }
    }
}
