/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*Author：GitHub Copilot
*文件名： ResponseSpinner
*描述：兼容 TUI 的响应状态指示器；在 TUI 模式下委托给 SpinnerService 渲染，
*在非 TUI（传统控制台）模式保持原有行为。
*****************************************************************************/

using System;
using System.Threading;
using LubanAgent.App;
using LubanAgent.Services;

namespace LubanAgent.UI;

/// <summary>
/// 响应状态指示器。在 TUI 模式下通过 SpinnerService 渲染（由 Terminal.Gui 负责绘制），
/// 在非 TUI 模式仍使用后台线程直接写控制台动画。
/// </summary>
public sealed class ResponseSpinner : IDisposable
{
    private readonly Thread _renderThread;
    private volatile bool _disposed;
    private volatile bool _stopped;
    private string _status;
    private bool _usingTui;

    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    public ResponseSpinner(string initialStatus = "正在思考...")
    {
        _status = initialStatus;
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "ResponseSpinner"
        };
        _usingTui = TerminalGuiApp.CanRunInteractive();
    }

    public void Start()
    {
        if (_disposed || _stopped) return;

        if (_usingTui)
        {
            // 在 TUI 下使用全局 SpinnerService
            SpinnerService.Start(_status);
            return;
        }

        if (_renderThread.IsAlive) return;
        _renderThread.Start();
    }

    public void UpdateStatus(string status)
    {
        Interlocked.Exchange(ref _status, status);
        if (_usingTui)
        {
            SpinnerService.UpdateStatus(status);
        }
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;

        if (_usingTui)
        {
            SpinnerService.Stop();
            return;
        }

        try
        {
            if (_renderThread.IsAlive && !_renderThread.Join(150))
            {
                _renderThread.Interrupt();
            }
        }
        catch
        {
            // 忽略线程清理异常
        }

        ClearAnimationLine();
    }

    private void ClearAnimationLine()
    {
        try
        {
            Console.Write("\r");
            Console.Write(new string(' ', Math.Max(1, Console.WindowWidth - 1)));
            Console.Write("\r");
        }
        catch
        {
            // 控制台不可用时忽略
        }
    }

    private void RenderLoop()
    {
        try
        {
            var frameIdx = 0;
            while (!_disposed && !_stopped)
            {
                var frame = SpinnerFrames[frameIdx % SpinnerFrames.Length];
                var status = _status;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"\r{frame} {status}");
                Console.ResetColor();

                frameIdx++;
                Thread.Sleep(80);
            }
        }
        catch (ThreadInterruptedException)
        {
            // 正常退出
        }
        catch (InvalidOperationException)
        {
            // 控制台不可用，静默退出
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
