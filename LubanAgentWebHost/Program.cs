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
    /// <param name="args">
    /// 命令行参数：<c>token</c>（打印在运行实例的浏览器入口）、<c>--stop</c>（停止在运行实例）、
    /// <c>--port</c> / <c>--token</c> / <c>--user</c> / <c>--timeout</c> / <c>--debug</c>。
    /// </param>
    /// <returns>退出码：0 正常，非 0 表示启动失败或命令失败。</returns>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 停止命令：按发现文件找到正在运行的宿主并结束它
        if (args.Any(static arg => string.Equals(arg, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            return StopRunningHost();
        }

        // 入口命令：宿主已在运行，按其自述文件重新打印带令牌的地址
        if (args.Length > 0 && string.Equals(args[0], "token", StringComparison.OrdinalIgnoreCase))
        {
            return PrintEntryUrls();
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

            // 访问令牌：未显式指定时每次启动随机生成，进程退出即失效（不做长期登录态）。
            // 令牌会原样进地址栏与 Set-Cookie，固定的值必须只含 Base64Url 字符，否则直接拒绝启动
            var accessToken = settings.Token ?? WebHostAccess.CreateToken();
            if (!WebHostAccess.IsValidToken(accessToken))
            {
                throw new InvalidOperationException(
                    "访问令牌只允许字母、数字、'-' 与 '_'（Base64Url 字符集）："
                    + "请检查 LubanAgentWeb:Token 配置或 --token 参数");
            }

            settings = settings with { Token = accessToken };

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

            // 访问门禁：把 MVC 的请求上下文换成带令牌判定的版本（覆盖接口、SSE、附件与静态资源）
            WebHostGate.Install(application);

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
            Report("⑥ 就绪（监听全部网卡，已启用访问令牌）");
            Report($"   浏览器入口：{WebHostAccess.BuildEntryUrl($"http://{WebHostAccess.GetPreferredLanHost()}:{settings.Port}", accessToken)}");
            Report($"   本机入口：  {WebHostAccess.BuildEntryUrl($"http://127.0.0.1:{settings.Port}", accessToken)}");
            Report("   令牌随本次运行有效，重启后换成新地址；随时用 LubanAgentWebHost token 重新打印。");

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

    /// <summary>
    /// 打印在运行实例的浏览器入口。
    /// </summary>
    /// <remarks>
    /// 令牌随进程生成、退出即失效，故一律读自述文件里的当前值；宿主未运行时报错，不做任何猜测。
    /// </remarks>
    /// <returns>退出码：0 已打印；1 没有可用实例。</returns>
    private static int PrintEntryUrls()
    {
        var descriptor = WebHostDiscovery.TryRead();
        if (descriptor is null)
        {
            Console.Error.WriteLine("[webhost] 没有发现运行中的实例；先启动 LubanAgentWebHost 再执行本命令");
            return 1;
        }

        var port = Uri.TryCreate(descriptor.BaseAddress, UriKind.Absolute, out var address) && address.Port > 0
            ? address.Port
            : 18080;

        if (string.IsNullOrEmpty(descriptor.Token))
        {
            Console.WriteLine($"[webhost] 运行中的实例未启用访问令牌（旧版本）：{descriptor.BaseAddress.TrimEnd('/')}/");
            return 0;
        }

        Console.WriteLine($"[webhost] 运行中：pid={descriptor.ProcessId} {descriptor.BaseAddress}");
        Console.WriteLine($"[webhost] 浏览器入口：{WebHostAccess.BuildEntryUrl($"http://{WebHostAccess.GetPreferredLanHost()}:{port}", descriptor.Token)}");
        Console.WriteLine($"[webhost] 本机入口：  {WebHostAccess.BuildEntryUrl($"http://127.0.0.1:{port}", descriptor.Token)}");
        return 0;
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
    /// 在静态根下生成 <c>runtime-config.js</c>（页面启动时读取身份与端口）。
    /// </summary>
    /// <remarks>
    /// 每次启动重写；该文件不进仓库（见 .gitignore）。必须在创建 <c>SAEAMvcApplication</c> 之前写盘，
    /// 避免首个请求落在文件索引建立之前。访问令牌不写这里：它只经地址栏换成 HttpOnly Cookie，
    /// 任何能被 JS 读到的地方都不放。
    /// </remarks>
    /// <param name="root">静态根目录绝对路径。</param>
    /// <param name="runtime">运行时（取用户标识）。</param>
    private static void WriteRuntimeConfig(string root, AgentWebRuntime runtime)
    {
        var config = new Dictionary<string, object?>
        {
            ["userId"] = runtime.UserId,
            ["port"] = runtime.Settings.Port
        };

        var content = $"window.__LUBAN__ = {AgentApiJson.Serialize(config)};\n";
        File.WriteAllText(Path.Combine(root, "runtime-config.js"), content, new UTF8Encoding(false));
    }
}