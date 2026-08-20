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
*描述：Avalonia 应用程序，负责初始化 DI、显示工作区选择器和主窗口
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
            // 初始化 DI
            var configuration = AgentHostBuilder.BuildConfiguration(Array.Empty<string>());
            _services = AgentHostBuilder.BuildServiceProvider(configuration);

            // 设置工作区授权回调（GUI 自动授权，用户已在选择器中确认）
            var workspaceManager = _services.GetRequiredService<IWorkspaceManager>();
            if (workspaceManager is LubanAgentCore.Services.WorkspaceManager wm)
            {
                wm.AuthorizationPrompt = _ => Task.FromResult(true);
            }

            // 创建并显示主窗口
            var mainWindow = new MainWindow();
            mainWindow.SetServiceProvider(_services);
            desktop.MainWindow = mainWindow;

            // 在主窗口显示后弹出工作区选择器
            mainWindow.Opened += async (s, e) =>
            {
                var picker = new WorkspacePickerWindow();
                picker.SetServiceProvider(_services);
                var selectedWorkspace = await picker.ShowDialog<WorkspaceInfo?>(mainWindow);

                if (selectedWorkspace != null)
                {
                    // 授权工作区（将 RootPath 加入 PathGuard.AllowedRoots）
                    var wsManager = _services.GetRequiredService<IWorkspaceManager>();
                    await wsManager.EnsureAuthorizedAsync(selectedWorkspace);
                    await wsManager.SetCurrentAsync(selectedWorkspace.WorkspaceId);

                    // 刷新侧边栏
                    mainWindow.SetServiceProvider(_services);
                }
                else
                {
                    // 用户取消，退出应用
                    desktop.Shutdown();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
