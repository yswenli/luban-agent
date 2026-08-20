/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： SpinnerService
*版本号： V1.0.0.0
*唯一标识：加载指示器服务
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：加载指示器服务
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 加载指示器服务。使用 PeriodicTimer 实现高精度定时。
/// </summary>
internal static class SpinnerService
{
    private static readonly string[] Frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private static PeriodicTimer? _timer;
    private static CancellationTokenSource? _cts;
    private static int _frameIndex;
    private static string _status = string.Empty;
    private static readonly object _lock = new();
    private static bool _running;

    /// <summary>当 spinner 状态或帧变更时触发（UI 订阅以重绘）。</summary>
    public static event Action? Changed;

    /// <summary>当前是否在运行（仅在 TUI 模式下生效）。</summary>
    public static bool IsRunning
    {
        get { lock (_lock) return _running; }
    }

    /// <summary>当前帧字符。</summary>
    public static string CurrentFrame
    {
        get { lock (_lock) return Frames[_frameIndex % Frames.Length]; }
    }

    /// <summary>当前状态文本。</summary>
    public static string Status
    {
        get { lock (_lock) return _status; }
    }

    /// <summary>
    /// 在 TUI 模式下启动全局 spinner 服务；非 TUI 模式将不启动。
    /// </summary>
    public static void Start(string? initialStatus = null)
    {
        if (!TerminalGuiApp.CanRunInteractive()) return;

        lock (_lock)
        {
            if (_running) return;
            _running = true;
            if (initialStatus is not null) _status = initialStatus;

            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(15));
            _ = RunAsync(_cts.Token);
        }

        Changed?.Invoke();
    }

    private static async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
    }

    /// <summary>停止并清理。</summary>
    public static void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            _cts?.Cancel();
            _timer?.Dispose();
            _timer = null;
            _cts?.Dispose();
            _cts = null;
            _frameIndex = 0;
            _status = string.Empty;
        }

        Changed?.Invoke();
    }

    /// <summary>更新状态文本（会触发 Changed 并在下一帧可见）。</summary>
    public static void UpdateStatus(string status)
    {
        lock (_lock)
        {
            _status = status ?? string.Empty;
        }
        Changed?.Invoke();
    }

    private static void Tick()
    {
        lock (_lock)
        {
            if (!_running) return;
            _frameIndex = (_frameIndex + 1) % Frames.Length;
        }
        Changed?.Invoke();
    }
}
