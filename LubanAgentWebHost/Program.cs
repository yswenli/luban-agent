/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost
*文件名： Program
*描述：WebHost 入口：初始化宿主服务，再以 SAEA.MVC 暴露 HTTP/SSE 接口
*
*****************************************************************************/

using System.Text;

using LuBan.Common;

using LubanAgentWebHost.Hosting;

using Microsoft.Extensions.Configuration;

using SAEA.MVC;

namespace LubanAgentWebHost;

/// <summary>
/// 程序入口。
/// </summary>
public static class Program
{
    /// <summary>
    /// 启动 WebHost。
    /// </summary>
    /// <param name="args">命令行参数（<c>--port</c> / <c>--token</c> / <c>--user</c> / <c>--timeout</c> / <c>--debug</c>）。</param>
    /// <returns>退出码：0 正常，非 0 表示启动失败。</returns>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Cancel();

        AgentWebRuntime? runtime = null;
        SAEAMvcApplication? application = null;
        try
        {
            var configuration = AgentHostBuilder.BuildConfiguration(args);
            var settings = WebHostSettings.Create(configuration, args);

            Report("① 加载配置...");
            configuration.InitConfigUtil();
            ProviderHelper.Initialize(configuration);

            Report("② 初始化数据库...");
            foreach (var message in DatabaseInitializer.Initialize())
            {
                Report(message);
            }

            Report("③ 准备嵌入模型...");
            var (embedder, modelManager) = await RetrievalBootstrap
                .PrepareAsync(configuration, Report, shutdown.Token)
                .ConfigureAwait(false);

            Report("④ 构建服务容器...");
            var host = AgentHostBuilder.Build(configuration, embedder, modelManager, services =>
            {
                // A2A：注册 SDK 实现与运行时生命周期（实际启动在用户工作区就绪后由 AgentWebRuntime 触发）
                services.AddLubanAgentA2A(configuration);

                // 编排进度出口：框架默认注册 no-op，这里换成可订阅实现并按请求作用域隔离，
                // 供 /agent/chat 把子代理进度转成 orchestration.progress 帧
                services.AddScoped<HostOrchestrationProgressSink>();
                services.ReplaceScoped<IOrchestrationProgressSink>(
                    sp => sp.GetRequiredService<HostOrchestrationProgressSink>());
            });
            runtime = AgentWebRuntime.Create(host, settings);

            Report("⑤ 启动 HTTP 服务...");
            var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            Directory.CreateDirectory(root);

            var mvcConfig = new SAEAMvcApplicationConfig
            {
                Port = settings.Port,
                Root = root,
                // 长回合 SSE 由动作内部流式写出，动作本身要阻塞到流结束，必须远大于默认 180s
                Timeout = settings.TimeoutSeconds,
                IsDebug = settings.Debug
            };

            application = new SAEAMvcApplication(mvcConfig);
            application.Start();

            Console.WriteLine($"listening http://127.0.0.1:{settings.Port} (user={runtime.UserId})");
            Report($"⑥ 就绪（仅回环可访问，令牌{(string.IsNullOrEmpty(settings.Token) ? "未启用" : "已启用")}）");

            await Task.Delay(Timeout.Infinite, shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[webhost] 启动失败：{ex}");
            return 1;
        }
        finally
        {
            try
            {
                application?.Stop();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[webhost] 停止服务失败：{ex.Message}");
            }

            if (runtime is not null)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }

            Report("已退出");
        }
    }

    private static void Report(string message) => AgentWebRuntime.Log(message);
}