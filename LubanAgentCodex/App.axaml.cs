/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex
*文件名： App
*版本号： V1.0.0.0
*唯一标识：应用程序入口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：Avalonia 应用程序，负责初始化 DI、自动恢复工作区、显示启动闪屏与主窗口
*
*****************************************************************************/
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LubanAgentCodex.Views;
using LubanAgentCore.Hosting;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LubanAgentCodex;

/// <summary>
/// Avalonia 应用程序
/// </summary>
public class App : Application
{
    private IServiceProvider? _services;

    /// <summary>
    /// 初始化应用程序
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // 注册 Avalonia UI 线程异常处理
        Dispatcher.UIThread.UnhandledException += OnUIThreadUnhandledException;
    }

    /// <summary>
    /// UI 线程未处理异常处理
    /// </summary>
    private void OnUIThreadUnhandledException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // 记录日志
        Logger.Error("UIThread", e.Exception);

        // 标记为已处理，防止应用崩溃
        e.Handled = true;
    }

    /// <summary>
    /// 框架初始化完成后的处理
    /// </summary>
    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 1) 立即显示启动闪屏，居中展示启动图
            var splash = new SplashWindow();
            splash.Show();

            // 让闪屏有机会完成首帧渲染，再开始耗时初始化
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            try
            {
                // 2) 加载应用配置
                splash.SetStatus("正在加载应用配置…");
                var configuration = AgentHostBuilder.BuildConfiguration(Array.Empty<string>());

                // 3) 初始化核心服务（DI 容器）
                splash.SetStatus("正在初始化核心服务…");
                _services = AgentHostBuilder.BuildServiceProvider(configuration);

                // 4) 设置工作区授权回调（GUI 自动授权）
                var workspaceManager = _services.GetRequiredService<IWorkspaceManager>();
                if (workspaceManager is LubanAgentCore.Services.WorkspaceManager wm)
                {
                    wm.AuthorizationPrompt = _ => Task.FromResult(true);
                }

                // 5) 自动恢复/初始化当前工作区
                splash.SetStatus("正在准备知识库与工作区…");
                var wsManager = _services.GetRequiredService<IWorkspaceManager>();
                var workspaces = await wsManager.GetUserWorkspacesAsync();
                var target = workspaces
                    .Where(w => w.Type != "Rag")
                    .OrderByDescending(w => w.LastActiveAt)
                    .FirstOrDefault();
                if (target == null)
                {
                    target = await wsManager.CreateWorkspaceAsync(
                        Path.GetFullPath(Directory.GetCurrentDirectory()), type: "Normal");
                }
                await wsManager.EnsureAuthorizedAsync(target);
                await wsManager.SetCurrentAsync(target.WorkspaceId);

                // 6) 构建并显示主窗口
                splash.SetStatus("正在构建主界面…");
                var mainWindow = new MainWindow();
                mainWindow.SetServiceProvider(_services);
                desktop.MainWindow = mainWindow;
                mainWindow.Show();

                // 7) 主窗口就绪：标记完成，等待 1 秒后关闭闪屏
                splash.MarkReady();
                await Task.Delay(1000);
                splash.Close();
            }
            catch (System.Exception ex)
            {
                // 初始化失败：记录日志，关闭闪屏，避免应用卡在启动画面
                Logger.Error("Startup", ex);
                splash.SetStatus("初始化失败：" + ex.Message);
                await Task.Delay(2000);
                splash.Close();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
