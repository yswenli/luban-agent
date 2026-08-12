/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
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
using LubanAgent.Views;
using Terminal.Gui.App;

namespace LubanAgent.App;

/// <summary>
/// Terminal.Gui 应用启动引导。采用官方推荐的实例式模型
/// （<c>Application.Create().Init()</c>，而非已过时的静态 Application），
/// 保证任何异常路径下终端都能恢复正常状态（退出备用屏幕、恢复光标与回显）。
/// </summary>
internal sealed class TerminalGuiApp
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// 初始化启动引导。
    /// </summary>
    /// <param name="services">根级依赖注入容器，供后续 ViewModel 解析服务使用。</param>
    public TerminalGuiApp(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// 依赖注入容器。骨架阶段暂未使用，迁移步骤 4 起由 ViewModel 层消费。
    /// </summary>
    public IServiceProvider Services => _services;

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
            // 无真实终端时读取窗口尺寸会抛异常或返回 0
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
    /// <param name="startupNotices">进入 TUI 前 产生的启动提示，渲染在会话区顶部。</param>
    public void Run(IReadOnlyList<string>? startupNotices = null)
    {
        // Create 必须早于任何静态 Application 访问，否则会因"legacy static model already used"抛异常。
        // 用显式 try-finally 而非 using，保证 Create 成功后任何后续步骤异常都能 Dispose 还原终端。
        IApplication? application = null;
        try
        {
            application = Application.Create();
            application.Init();

            ConfigureDriver(application);
            Dispatcher = new TerminalGuiDispatcher(application);

            using var root = new RootView(_services, Dispatcher, startupNotices);
            application.Run(root, OnUnhandledException);
        }
        finally
        {
            Dispatcher = null;
            // Dispose 完成 Shutdown 与终端状态还原（退出 alt-screen、恢复光标与回显）
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

        // 终端支持真彩时强制关闭 16 色降级；不支持则保持驱动默认行为
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
        // 致命异常不吞，传播出去让进程崩溃，避免状态损坏后无限重绘
        if (ex is OutOfMemoryException or StackOverflowException or ThreadAbortException)
        {
            return false;
        }

        Logger.Error("TUI 主循环未捕获异常", ex);
        return true;
    }
}
