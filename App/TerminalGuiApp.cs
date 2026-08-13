/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App
*文件名： TerminalGuiApp
*版本号： V1.0.0.0
*唯一标识：TUI 应用启动引导
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Terminal.Gui 应用启动引导，负责初始化驱动、运行顶层视图与优雅关闭
*
*****************************************************************************/
namespace LubanAgentCli.App;

/// <summary>
/// Terminal.Gui 应用启动引导。采用官方推荐的实例式模型
/// （<c>Application.Create().Init()</c>，而非已过时的静态 Application），
/// 保证任何异常路径下终端都能恢复正常状态（退出备用屏幕、恢复光标与回显）。
/// </summary>
internal sealed class TerminalGuiApp : IDisposable
{
    /// <summary>
    /// 依赖注入容器。由启动向导初始化完成后设置。
    /// </summary>
    public IServiceProvider? Services { get; private set; }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// UI 线程调度器。仅在主循环运行期间有效，供 ViewModel 从后台线程编组视图更新。
    /// </summary>
    public IUiDispatcher? Dispatcher { get; private set; }

    /// <summary>
    /// 检测当前进程是否具备可交互终端。重定向输入输出或无终端窗口时不能进入 TUI。
    /// </summary>
    /// <returns>可进入 TUI 返回 true。</returns>
    public static bool CanRunInteractive()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return false;
        }

        try
        {
            return Console.WindowWidth > 0 && Console.WindowHeight > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 启动 TUI 主循环，阻塞直至用户退出。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    public void Run(string[] args)
    {
        IApplication? application = null;
        try
        {
            // 诊断：LUBAN_TUI_DRIVER=ansi|windows|dotnet 可强制指定驱动做 A/B 对比
            var driverName = Environment.GetEnvironmentVariable("LUBAN_TUI_DRIVER");

            application = Application.Create();
            application.Init(driverName);

            ConfigureDriver(application);
            if (TuiDiag.Enabled)
            {
                Logger.Warn($"[TuiDiag] driver={(string.IsNullOrEmpty(driverName) ? "(default)" : driverName)} actual={application.Driver}");
                application.Iteration += (_, _) => TuiDiag.IterationTick();
                application.Keyboard.KeyDown += (_, _) => TuiDiag.KeyArrival();
            }
            Dispatcher = new TerminalGuiDispatcher(application);
            var ui = new TuiUiService(application);

            // 阶段一：运行启动向导（模态对话框），执行配置加载、数据库初始化、嵌入模型准备等。
            // 初始化完成或用户取消后，StartupDialog 内部调用 RequestStop() 关闭此对话框。
            var startup = new StartupDialog(args, Dispatcher, ui);
            application.Run(startup);

            if (!startup.Success || startup.Services == null)
            {
                return;
            }

            Services = startup.Services;
            // 阶段二：启动成功后进入主界面，传入 OnUnhandledException 保持主循环在普通异常下存活。
            using var root = new RootView(Services, Dispatcher, ui, startup.Notices);
            application.Run(root, OnUnhandledException);
        }
        finally
        {
            Dispatcher = null;
            application?.Dispose();
        }
    }

    /// <summary>
    /// 配置驱动：启用鼠标、关闭 16 色降级以保证 24-bit TrueColor 生效。
    /// </summary>
    /// <param name="application">已完成 Init 的应用实例。</param>
    private static void ConfigureDriver(IApplication application)
    {
        if (application.Mouse is { } mouse)
        {
            mouse.IsMouseDisabled = false;
        }

        var driver = application.Driver;
        if (driver is null)
        {
            return;
        }

        if (driver.SupportsTrueColor)
        {
            driver.Force16Colors = false;
        }
    }

    /// <summary>
    /// 主循环未捕获异常处理。普通异常记录日志后返回 true 以维持主循环存活；
    /// 致命异常（OOM 等）返回 false 让其传播，避免无限吞异常导致卡死。
    /// </summary>
    /// <param name="ex">未捕获的异常。</param>
    /// <returns>true 表示已处理（resume），false 表示未处理（传播）。</returns>
    private static bool OnUnhandledException(Exception ex)
    {
        if (ex is OutOfMemoryException or StackOverflowException or ThreadAbortException)
        {
            return false;
        }

        Logger.Error("TUI 主循环未捕获异常", ex);
        return true;
    }
}
