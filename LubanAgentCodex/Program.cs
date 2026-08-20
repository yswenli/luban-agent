/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex
*文件名： Program
*版本号： V1.0.0.0
*唯一标识：程序主入口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：LubanAgentCodex GUI 应用程序主入口，初始化数据库并启动 Avalonia
*
*****************************************************************************/
namespace LubanAgentCodex;

/// <summary>
/// 程序主入口
/// </summary>
class Program
{
    /// <summary>
    /// 应用程序入口点
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        // 注册全局异常处理
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            // 初始化数据库（LuBanOrm.Init、表结构迁移等）
            DatabaseInitializer.Initialize();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Logger.Error("应用程序启动失败", ex);
            throw;
        }
    }

    /// <summary>
    /// 构建 Avalonia 应用
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// 未处理的后台线程异常
    /// </summary>
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Logger.Error("UnhandledException", ex);

            // 如果是致命异常，记录后退出
            if (e.IsTerminating)
            {
                Logger.Error("应用程序即将终止", ex);
            }
        }
    }

    /// <summary>
    /// 未观察的 Task 异常
    /// </summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.Error("UnobservedTaskException", e.Exception);
        e.SetObserved(); // 防止进程崩溃
    }
}
