/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.UI
*文件名： EscKeyListener
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：ESC 键监听器，TUI 模式下禁用后台轮询以避免抢占键盘缓冲区
*
*****************************************************************************/

using System;
using System.Threading;
using LubanAgent.App;

namespace LubanAgent.UI;

/// <summary>
/// ESC 键监听器。在后台线程轮询键盘输入，当检测到 ESC 键时取消当前操作。
/// TUI 模式（Terminal.Gui）下自动禁用，避免与 Terminal.Gui 抢占输入缓冲区。
/// </summary>
public sealed class EscKeyListener : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Thread _listenerThread;
    private volatile bool _disposed;
    private volatile bool _stopped;

    /// <summary>
    /// 主线程正在读取控制台输入的引用计数（>0 时后台线程暂停轮询）
    /// </summary>
    private static int _mainThreadReadingConsole;

    /// <summary>
    /// 控制台读取锁，用于协调主线程与后台监听线程
    /// </summary>
    public static readonly object ConsoleReadLock = new();

    /// <summary>
    /// 是否已显示过"不支持 ESC 键监听"提示（避免重复输出）
    /// </summary>
    private static bool _unsupportedPromptShown;

    /// <summary>
    /// 标记主线程开始读取控制台输入（增加引用计数）
    /// </summary>
    public static void BeginConsoleRead() => Interlocked.Increment(ref _mainThreadReadingConsole);

    /// <summary>
    /// 标记主线程结束读取控制台输入（减少引用计数）
    /// </summary>
    public static void EndConsoleRead() => Interlocked.Decrement(ref _mainThreadReadingConsole);

    /// <summary>
    /// 获取取消令牌，外部可订阅以响应 ESC 键取消
    /// </summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// 当前是否已暂停（ESC 已按下，操作已取消）
    /// </summary>
    public bool IsPaused => _cts.IsCancellationRequested;

    /// <summary>
    /// 创建 ESC 键监听器实例，初始化取消令牌源和后台监听线程
    /// </summary>
    public EscKeyListener()
    {
        _cts = new CancellationTokenSource();
        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "EscKeyListener"
        };
    }

    /// <summary>
    /// 当前环境是否支持 ESC 键监听（非重定向输入时可用）
    /// </summary>
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

    /// <summary>
    /// 启动后台监听线程。TUI 模式下自动跳过，不支持的环境仅提示一次。
    /// </summary>
    public void Start()
    {
        // 在 TUI 模式下禁用后台轮询以避免与 Terminal.Gui 抢占输入
        if (TerminalGuiApp.CanRunInteractive())
        {
            return;
        }

        if (_disposed || _stopped) return;
        if (_listenerThread.IsAlive) return;

        // 不支持的环境（如管道/重定向）仅提示一次
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

    /// <summary>
    /// 停止监听线程（不立即释放资源，可由 Dispose 完成）
    /// </summary>
    public void Stop()
    {
        _stopped = true;
    }

    /// <summary>
    /// 阻塞等待用户选择继续或终止。返回 true 表示继续，false 表示终止当前操作。
    /// </summary>
    /// <returns>是否继续执行</returns>
    public bool WaitForResumeOrCancel()
    {
        if (!IsPaused) return true;

        // 循环等待用户输入，直到选择继续或终止
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

    /// <summary>
    /// 后台监听循环：轮询键盘输入，检测到 ESC 键时触发取消
    /// </summary>
    private void ListenLoop()
    {
        try
        {
            while (!_disposed && !_stopped && !_cts.IsCancellationRequested)
            {
                // 主线程正在读取控制台时暂停轮询，避免抢占输入
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

    /// <summary>
    /// 释放资源：停止监听线程并等待退出，超时则中断线程
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopped = true;

        try
        {
            // 等待线程退出，超时 200ms 则强制中断
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
