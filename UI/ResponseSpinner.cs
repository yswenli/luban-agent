/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.UI
*文件名： ResponseSpinner
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：兼容 TUI 的响应状态指示器，TUI 模式下委托 SpinnerService 渲染，非 TUI 模式保持控制台动画
*
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

    /// <summary>
    /// Spinner 动画帧序列（Braille 字符循环）
    /// </summary>
    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    /// <summary>
    /// 创建响应状态指示器实例，初始化渲染线程并检测是否处于 TUI 模式
    /// </summary>
    /// <param name="initialStatus">初始状态文本，默认"正在思考..."</param>
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

    /// <summary>
    /// 启动 spinner 动画。TUI 模式下委托给全局 SpinnerService，非 TUI 模式启动后台渲染线程。
    /// </summary>
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

    /// <summary>
    /// 更新状态文本（线程安全），TUI 模式下同步通知 SpinnerService
    /// </summary>
    /// <param name="status">新的状态文本</param>
    public void UpdateStatus(string status)
    {
        // 使用 Interlocked 保证多线程下状态文本的原子更新
        Interlocked.Exchange(ref _status, status);
        if (_usingTui)
        {
            SpinnerService.UpdateStatus(status);
        }
    }

    /// <summary>
    /// 停止 spinner 动画并清理渲染线程
    /// </summary>
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
            // 等待渲染线程退出，超时 150ms 则强制中断
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

    /// <summary>
    /// 清除控制台当前行的动画内容（用空格覆盖）
    /// </summary>
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

    /// <summary>
    /// 后台渲染循环：循环输出 spinner 帧和状态文本，直到停止或释放
    /// </summary>
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
    /// 释放资源：停止动画并标记为已释放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
