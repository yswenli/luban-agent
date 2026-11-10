/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost
*文件名： Program
*描述：WebHost 入口：初始化宿主服务，再以 SAEA.MVC 暴露 HTTP/SSE 接口
*
*****************************************************************************/

using System.Diagnostics;

namespace LubanAgentWebHost;

/// <summary>
/// 程序入口。
/// </summary>
public static class Program
{
    /// <summary>
    /// 启动 WebHost。
    /// </summary>
    /// <param name="args">命令行参数（<c>--stop</c> / <c>--port</c> / <c>--token</c> / <c>--user</c> / <c>--timeout</c> / <c>--debug</c>）。</param>
    /// <returns>退出码：0 正常，非 0 表示启动失败。</returns>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 停止命令：按发现文件找到正在运行的宿主并结束它
        if (args.Any(static arg => string.Equals(arg, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            return StopRunningHost();
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            CancelQuietly(shutdown);
        };
        // 进程退出时 CTS 已释放（using 在 Main 返回时执行），这里必须吞掉 ObjectDisposedException，
        // 否则启动失败等提前返回的路径会在退出阶段被未处理异常掩盖真正的失败原因
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CancelQuietly(shutdown);

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
            WriteRuntimeConfig(root, runtime);

            var mvcConfig = new SAEAMvcApplicationConfig
            {
                Port = settings.Port,
                // SAEA 会把 Root 里的路径分隔符全部去掉再拼到 exe 目录的父级下，
                // 所以这里只能给相对单段目录名，绝对路径必然 404
                Root = "wwwroot",
                // 长回合 SSE 由动作内部流式写出，动作本身要阻塞到流结束，必须远大于默认 180s
                Timeout = settings.TimeoutSeconds,
                IsDebug = settings.Debug
            };

            application = new SAEAMvcApplication(mvcConfig);
            try
            {
                application.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"启动 HTTP 服务失败，端口 {settings.Port} 可能已被占用（{ex.Message}）", ex);
            }

            // 自述文件：客户端与浏览器据此找到本实例（端口与令牌以本进程配置为准）
            WebHostDiscovery.Write(new WebHostDescriptor
            {
                BaseAddress = $"http://127.0.0.1:{settings.Port}",
                Token = settings.Token,
                ProcessId = Environment.ProcessId,
                Version = typeof(Program).Assembly.GetName().Version?.ToString(),
                ServerPath = Environment.ProcessPath,
                StartedAt = DateTimeOffset.Now
            });

            Console.WriteLine($"listening on 0.0.0.0:{settings.Port} (user={runtime.UserId})");
            Report($"⑥ 就绪（监听全部网卡，令牌{(string.IsNullOrEmpty(settings.Token) ? "未启用" : "已启用")}）");
            Report($"   浏览器入口：http://127.0.0.1:{settings.Port}/　停止：LubanAgentWebHost --stop");
            if (string.IsNullOrEmpty(settings.Token))
            {
                Report("   提示：未启用令牌，任何能访问该端口的人都可以操作本机（读写工作区文件、执行命令）；对公网暴露务必加 --token 并在反向代理层再做一次鉴权。");
            }

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
            // 先撤自述文件，避免客户端在服务停止过程中仍把请求打到本进程
            WebHostDiscovery.Delete(Environment.ProcessId);

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

    /// <summary>
    /// 停掉正在运行的宿主实例。
    /// </summary>
    /// <remarks>
    /// 只处理发现文件指向、且进程名确为 <c>LubanAgentWebHost</c> 的实例，
    /// 避免进程号被复用时误杀无关进程。
    /// </remarks>
    /// <returns>退出码：0 已停止或本来就没在运行；1 无法停止。</returns>
    private static int StopRunningHost()
    {
        var descriptor = WebHostDiscovery.TryRead();
        if (descriptor is null)
        {
            Console.WriteLine("[webhost] 没有发现运行中的实例");
            return 0;
        }

        try
        {
            using var process = Process.GetProcessById(descriptor.ProcessId);
            if (!process.ProcessName.Contains("LubanAgentWebHost", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"[webhost] 发现文件指向的进程不是宿主（pid={descriptor.ProcessId}, name={process.ProcessName}），已忽略");
                return 1;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            WebHostDiscovery.Delete(descriptor.ProcessId);
            Console.WriteLine($"[webhost] 已停止 pid={descriptor.ProcessId}（{descriptor.BaseAddress}）");
            return 0;
        }
        catch (ArgumentException)
        {
            // 进程已退出：清理陈旧的自述文件即可
            WebHostDiscovery.Delete(descriptor.ProcessId);
            Console.WriteLine($"[webhost] 实例已不在运行（pid={descriptor.ProcessId}），已清理自述文件");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[webhost] 停止失败：{ex.Message}");
            return 1;
        }
    }

    private static void Report(string message) => AgentWebRuntime.Log(message);

    /// <summary>请求关闭；令牌源已释放（Main 已返回）时忽略。</summary>
    private static void CancelQuietly(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 进程已在退出流程中，无需再请求关闭
        }
    }

    /// <summary>
    /// 在静态根下生成 <c>runtime-config.js</c>（页面启动时读取令牌与身份）。
    /// </summary>
    /// <remarks>
    /// 每次启动重写；该文件不进仓库（见 .gitignore）。必须在创建 <c>SAEAMvcApplication</c> 之前写盘，
    /// 避免首个请求落在文件索引建立之前。
    /// </remarks>
    /// <param name="root">静态根目录绝对路径。</param>
    /// <param name="runtime">运行时（取用户标识）。</param>
    private static void WriteRuntimeConfig(string root, AgentWebRuntime runtime)
    {
        var config = new Dictionary<string, object?>
        {
            ["token"] = runtime.Settings.Token ?? string.Empty,
            ["userId"] = runtime.UserId,
            ["port"] = runtime.Settings.Port
        };

        var content = $"window.__LUBAN__ = {AgentApiJson.Serialize(config)};\n";
        File.WriteAllText(Path.Combine(root, "runtime-config.js"), content, new UTF8Encoding(false));
    }
}