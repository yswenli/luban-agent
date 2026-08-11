/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*Author：GitHub Copilot
*文件名： EscKeyListener
*描述：ESC 键监听器。对 TUI 模式（Terminal.Gui）下禁用后台轮询，避免抢占键盘缓冲区。
*****************************************************************************/

using System;
using System.Threading;
using LubanAgent.App;

namespace LubanAgent.UI;

public sealed class EscKeyListener : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Thread _listenerThread;
    private volatile bool _disposed;
    private volatile bool _stopped;

    private static int _mainThreadReadingConsole;
    public static readonly object ConsoleReadLock = new();
    private static bool _unsupportedPromptShown;

    public static void BeginConsoleRead() => Interlocked.Increment(ref _mainThreadReadingConsole);
    public static void EndConsoleRead() => Interlocked.Decrement(ref _mainThreadReadingConsole);

    public CancellationToken Token => _cts.Token;
    public bool IsPaused => _cts.IsCancellationRequested;

    public EscKeyListener()
    {
        _cts = new CancellationTokenSource();
        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "EscKeyListener"
        };
    }

    public static bool IsSupported
    {
        get
        {
            try
            {
                return !Console.IsInputRedirected;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Start()
    {
        // 在 TUI 模式下禁用后台轮询以避免与 Terminal.Gui 抢占输入
        if (TerminalGuiApp.CanRunInteractive())
        {
            return;
        }

        if (_disposed || _stopped) return;
        if (_listenerThread.IsAlive) return;

        if (!IsSupported)
        {
            if (!_unsupportedPromptShown)
            {
                _unsupportedPromptShown = true;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("（当前环境不支持 ESC 键监听，如管道/重定向输入场景）");
                Console.ResetColor();
            }
            return;
        }

        _listenerThread.Start();
    }

    public void Stop()
    {
        _stopped = true;
    }

    public bool WaitForResumeOrCancel()
    {
        if (!IsPaused) return true;

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("⏸  任务已暂停，输入 'c' 继续，输入 'q' 终止当前操作: ");
            Console.ResetColor();

            var input = Console.ReadLine()?.Trim().ToLower();
            if (input == "q")
            {
                return false;
            }
            if (input == "c" || input == "continue" || input == "继续")
            {
                return true;
            }
        }
    }

    private void ListenLoop()
    {
        try
        {
            while (!_disposed && !_stopped && !_cts.IsCancellationRequested)
            {
                if (Volatile.Read(ref _mainThreadReadingConsole) > 0)
                {
                    Thread.Sleep(50);
                    continue;
                }

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        _cts.Cancel();
                        break;
                    }
                }
                Thread.Sleep(50);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (ThreadInterruptedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopped = true;

        try
        {
            if (_listenerThread.IsAlive && !_listenerThread.Join(200))
            {
                _listenerThread.Interrupt();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("EscKeyListener.Dispose: 线程中断异常", ex);
        }

        _cts.Dispose();
    }
}
