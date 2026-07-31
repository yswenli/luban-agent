/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： ResponseSpinner
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：响应状态指示器，在用户回车后立即显示动画反馈
*
*****************************************************************************/
namespace LubanAgent.Services;

/// <summary>
/// 响应状态指示器，在用户回车后立即显示动画反馈。
/// 使用后台线程渲染旋转动画与状态文本，首个流式 chunk 到达后停止并清理。
/// </summary>
public sealed class ResponseSpinner : IDisposable
{
    private readonly Thread _renderThread;
    private volatile bool _disposed;
    private volatile bool _stopped;
    private string _status;

    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    /// <summary>
    /// 创建响应指示器实例。
    /// </summary>
    /// <param name="initialStatus">初始状态文本。</param>
    public ResponseSpinner(string initialStatus = "正在思考...")
    {
        _status = initialStatus;
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "ResponseSpinner"
        };
    }

    /// <summary>
    /// 启动指示器，立即在控制台显示动画。
    /// </summary>
    public void Start()
    {
        if (_disposed || _stopped) return;
        if (_renderThread.IsAlive) return;
        _renderThread.Start();
    }

    /// <summary>
    /// 更新状态文本（线程安全）。
    /// </summary>
    /// <param name="status">新的状态文本。</param>
    public void UpdateStatus(string status)
    {
        Interlocked.Exchange(ref _status, status);
    }

    /// <summary>
    /// 停止指示器并清理控制台上的动画行。
    /// </summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;

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

        // 清理动画行：用空格覆盖后回退光标
        ClearAnimationLine();
    }

    private void ClearAnimationLine()
    {
        try
        {
            // 回退到行首并覆盖空白
            Console.Write("\r");
            Console.Write(new string(' ', Console.WindowWidth - 1));
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

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
