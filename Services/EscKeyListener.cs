/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： EscKeyListener
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：ESC 键监听器，在任务执行期间监听 ESC 暂停请求
*
*****************************************************************************/
namespace LubanAgent.Services;

/// <summary>
/// ESC 键监听器，在任务执行期间监听 ESC 暂停请求。
/// 启动后台线程持续轮询键盘缓冲区，检测到 ESC 时触发 CancellationTokenSource.Cancel()。
/// 任务暂停后，向用户提示输入"继续"或回车恢复执行。
/// </summary>
public sealed class EscKeyListener : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Thread _listenerThread;
    private volatile bool _disposed;
    private volatile bool _stopped;

    /// <summary>
    /// 全局标志：主线程正在进行控制台输入（如确认对话框、ReadLine）。
    /// 为 true 时 ESC 监听线程不读取键盘，避免与主线程竞争输入缓冲。
    /// </summary>
    private static volatile bool _mainThreadReadingConsole;

    /// <summary>
    /// 标记主线程开始读取控制台输入。读取完成后必须调用 <see cref="EndConsoleRead"/>。
    /// </summary>
    public static void BeginConsoleRead() => _mainThreadReadingConsole = true;

    /// <summary>
    /// 标记主线程结束控制台输入。
    /// </summary>
    public static void EndConsoleRead() => _mainThreadReadingConsole = false;

    /// <summary>
    /// 获取取消令牌，ESC 触发时将被取消。
    /// </summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// ESC 是否已被触发（暂停状态）。
    /// </summary>
    public bool IsPaused => _cts.IsCancellationRequested;

    /// <summary>
    /// 创建 ESC 键监听器实例。
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
    /// 当前控制台是否支持 ESC 键监听（重定向输入时不支持）。
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            try
            {
                // 重定向输入时 KeyAvailable 不可用
                return !Console.IsInputRedirected;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 启动 ESC 键监听。控制台不支持时向用户输出一次性提示。
    /// </summary>
    public void Start()
    {
        if (_disposed || _stopped) return;
        if (_listenerThread.IsAlive) return;

        if (!IsSupported)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("（当前环境不支持 ESC 键监听，如管道/重定向输入场景）");
            Console.ResetColor();
            return;
        }

        _listenerThread.Start();
    }

    /// <summary>
    /// 停止监听但保持取消状态不变（用于任务正常结束时清理监听）。
    /// </summary>
    public void Stop()
    {
        _stopped = true;
    }

    /// <summary>
    /// 若已暂停，则阻塞等待用户输入"继续"或"终止"。
    /// 注意：由于流式响应已被中断，继续后需由调用方决定是否重新发起请求。
    /// </summary>
    /// <returns>用户是否选择继续。false 表示放弃当前操作。</returns>
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
            // 其他输入继续等待
        }
    }

    private void ListenLoop()
    {
        try
        {
            while (!_disposed && !_stopped && !_cts.IsCancellationRequested)
            {
                // 主线程正在读取控制台时不争抢键盘缓冲，避免输入被截获
                if (_mainThreadReadingConsole)
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
            // 控制台不支持 KeyAvailable（如重定向输入），静默退出
        }
        catch (ThreadInterruptedException)
        {
            // 线程被中断，正常退出
        }
    }

    /// <summary>
    /// 释放监听器资源。
    /// </summary>
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
