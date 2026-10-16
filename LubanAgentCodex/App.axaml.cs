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
using LubanAgentA2A;
using LubanAgentA2A.Runtime;
using LubanAgentCodex.Views;
using LubanAgentCore.Configuration;
using LubanAgentCore.Hosting;
using LubanAgentCore.Retrieval;
using LubanAgentCore.Services;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Plugins;
using LuBan.Logging;
using LubanAgentCore.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LubanAgentCodex;

/// <summary>
/// Avalonia 应用程序
/// </summary>
public class App : Application
{
    private AgentHostServices? _services;

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

                // 3) 准备检索嵌入模型（LLM Wiki 知识库依赖），并初始化核心服务（DI 容器）
                splash.SetStatus("正在准备检索嵌入模型…");
                OnnxEmbeddingGenerator? embedder = null;
                ModelManager? modelManager = null;
                try
                {
                    (embedder, modelManager) = await RetrievalBootstrap.PrepareAsync(configuration, msg => splash.SetStatus(msg));
                }
                catch (Exception ex)
                {
                    Logger.Error("准备检索嵌入模型失败", ex);
                }

                splash.SetStatus("正在初始化核心服务…");
                _services = AgentHostBuilder.BuildServiceProvider(configuration, embedder, modelManager, services =>
                {
                    // A2A：注册 SDK 实现与运行时生命周期（实际启动在进入工作区后触发）
                    services.AddLubanAgentA2A(configuration);

                    // 编排进度出口：框架默认注册 no-op，此处替换为可订阅的宿主实现（供主窗口实时渲染）
                    services.AddSingleton<HostOrchestrationProgressSink>();
                    services.Replace(ServiceDescriptor.Singleton<IOrchestrationProgressSink>(
                        sp => sp.GetRequiredService<HostOrchestrationProgressSink>()));

                    // 插件体系（PRD §3）：宿主实现必须为单例——框架 PluginManager 为单例并经根容器
                    // 延迟解析 IPluginHost，其构造链不得含 scoped 服务（SkillRegistry/MCPRegistry 等
                    // 均按会话 scope 隔离，只能经 PluginHostContext 的会话 scope 触达）。
                    services.AddSingleton<Plugins.PluginHostRegistry>();
                    services.AddSingleton<Plugins.PluginHostContext>();
                    services.AddSingleton<Plugins.CodexPluginHost>();
                    services.AddSingleton<IPluginHost>(sp => sp.GetRequiredService<Plugins.CodexPluginHost>());
                    services.AddSingleton<IPluginSessionContributor>(sp => sp.GetRequiredService<Plugins.CodexPluginHost>());
                });

                // 初始化 ProviderHelper（使 GetEndpoints 可用，SettingsWindow 新建供应商时预填 BaseUrl 依赖）
                ProviderHelper.Initialize(configuration);

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
                    .Where(w => w.Type != WorkspaceTypes.LlmWiki)
                    .OrderByDescending(w => w.LastActiveAt)
                    .FirstOrDefault();
                if (target == null)
                {
                    target = await wsManager.CreateWorkspaceAsync(
                        Path.GetFullPath(Directory.GetCurrentDirectory()), type: WorkspaceTypes.Normal);
                }
                await wsManager.EnsureAuthorizedAsync(target);
                await wsManager.SetCurrentAsync(target.WorkspaceId);

                // 插件宿主与状态登记必须在「根容器」首次创建：这些单例在构造期注入的 IServiceProvider
                // 会被长期持有（PluginManager 延迟解析 IPluginHost、PluginHostContext 借此定位会话 scope），
                // 若首次创建发生在用户/会话 scope，会形成 captive dependency（该 scope 释放后即不可用）。
                _ = _services.Root.GetRequiredService<IPluginHost>();

                // 插件体系启动引导：扫描 <BaseDirectory>/plugins 并恢复启用状态
                // （放在工作区就绪之后，插件贡献的工作区级 Skill 可直接落到当前工作区）
                await Plugins.PluginBootstrap.InitializeAsync(_services.Root, msg => splash.SetStatus(msg));

                // worktree 启动对账：登记与磁盘不一致时记录提示（不阻塞启动）
                try
                {
                    var reconcileMessages = await _services.GetRequiredService<WorktreeReconciler>().RunAsync();
                    foreach (var message in reconcileMessages)
                        Logger.Warn("worktree 对账: " + message);
                }
                catch (Exception ex)
                {
                    Logger.Error("worktree 启动对账失败", ex);
                }

                // A2A：工作区确定后按工作区并集配置启动服务端/客户端角色（未启用时为 no-op）
                splash.SetStatus("正在启动 A2A…");
                try
                {
                    await _services.GetRequiredService<IA2ALifecycle>().StartAsync(target.RootPath);
                }
                catch (Exception ex)
                {
                    Logger.Error("启动 A2A 失败", ex);
                }

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
