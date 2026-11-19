/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost
*文件名： Program
*描述：WebHost 入口：初始化宿主服务，再以 SAEA.MVC 暴露 HTTP/SSE 接口
*
*****************************************************************************/

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
    /// 命令行参数：<c>--token</c>（打印在运行实例的浏览器入口）、<c>--stop</c>（停止在运行实例）、
    /// <c>--port</c> / <c>--host-token</c> / <c>--user</c> / <c>--timeout</c> / <c>--debug</c>。
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
        if (args.Any(static arg => string.Equals(arg, "--token", StringComparison.OrdinalIgnoreCase)))
        {
            return await PrintEntryUrlsAsync().ConfigureAwait(false);
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

            // 宿主固定令牌（只走 Authorization: Bearer，给 CLI / Codex / 插件用）：未显式指定时每次启动随机生成。
            // 浏览器侧的令牌是另一套：由运行时按会话策略轮换，地址栏 ?t= 与 luban_auth Cookie 只认它。
            // 令牌会原样进地址栏与 Set-Cookie，固定的值必须只含 Base64Url 字符，否则直接拒绝启动
            var accessToken = settings.Token ?? WebHostAccess.CreateToken();
            if (!WebHostAccess.IsValidToken(accessToken))
            {
                throw new InvalidOperationException(
                    "访问令牌只允许字母、数字、'-' 与 '_'（Base64Url 字符集）："
                    + "请检查 LubanAgentWeb:Token 配置或 --host-token 参数");
            }

            settings = settings with { Token = accessToken };

            // 插件状态文件与桌面端分开：未显式配置时落到 Web 自己的默认路径，
            // 避免 Web 的启停状态污染客户端（Codex）的插件状态。
            if (string.IsNullOrWhiteSpace(configuration["LuBanAgent:Plugins:StateFile"]))
            {
                configuration = new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["LuBanAgent:Plugins:StateFile"] = WebPluginPaths.DefaultStateFilePath
                    })
                    .Build();
            }

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

                // C28：可写凭据存储覆盖框架默认只读实现（ConfigurationCredentialStore.SetAsync 抛 NotSupportedException）；
                // 云效设置服务供 YunxiaoController 的工作台服务使用。
                services.AddSingleton<LubanAgentCore.Security.CredentialStore>();
                services.ReplaceSingleton<ICredentialStore>(d => d.GetRequiredService<LubanAgentCore.Security.CredentialStore>());
                services.AddSingleton<LubanAgentCore.Connectors.AliyunDevOps.YunxiaoSettingsService>();
                // 测试缝合点接口桥接：仍按具体类注册（消费方不改），接口指向同一单例
                services.TryAddSingleton<LubanAgentCore.Connectors.AliyunDevOps.IYunxiaoSettingsService>(
                    d => d.GetRequiredService<LubanAgentCore.Connectors.AliyunDevOps.YunxiaoSettingsService>());
                // 云效工作台编排（YunxiaoController → WebYunxiaoService → 本服务）
                services.AddSingleton<YunxiaoWorkbenchService>();
                // 云效 S2 项目初始化（YunxiaoController → WebYunxiaoService → 本服务）：
                // 单例口径——YunxiaoInitService 持执行上下文、InitTaskStore 持任务态文件，均不可按请求重建
                services.AddSingleton<LubanAgentWebHost.Services.Init.InitTaskStore>();
                services.AddSingleton<LubanAgentWebHost.Services.Init.ISessionGateway, LubanAgentWebHost.Services.Init.SessionGateway>();
                services.AddSingleton<LubanAgentWebHost.Services.Init.IWorkspaceDirectoryProvider, LubanAgentWebHost.Services.Init.WorkspaceDirectoryProvider>();
                services.AddSingleton<LubanAgentWebHost.Services.Init.YunxiaoInitService>();

                // 编排进度出口：框架默认注册 no-op，这里换成可订阅实现并按请求作用域隔离，
                // 供 /agent/chat 把子代理进度转成 orchestration.progress 帧
                services.AddScoped<HostOrchestrationProgressSink>();
                services.ReplaceScoped<IOrchestrationProgressSink>(
                    sp => sp.GetRequiredService<HostOrchestrationProgressSink>());

                // 取文件链接工具：模型拿到工作区文件后据此给出可点击的宿主绝对地址
                services.AddScoped<ILuBanToolPlugin, WebFileLinkToolPlugin>();
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

            var lanEntryUrl = WebHostAccess.BuildLanEntryUrl(settings.Port, runtime.Sessions.Current);

            Console.WriteLine($"listening on 0.0.0.0:{settings.Port} (user={runtime.UserId})");
            Report("⑥ 就绪（监听全部网卡，已启用访问令牌）");
            Report($"   浏览器入口：{lanEntryUrl}");
            Report($"   本机入口：  {WebHostAccess.BuildEntryUrl($"http://127.0.0.1:{settings.Port}", runtime.Sessions.Current)}");
            Report($"   浏览器令牌每 {settings.SessionTokenMinutes:0.##} 分钟自动换新（活动页面自动续期），"
                + (settings.SessionMaxHours > 0
                    ? $"会话最长 {settings.SessionMaxHours:0.##} 小时；到期后用 LubanAgentWebHost --token 重新取地址。"
                    : "会话不设绝对上限，宿主重启后用 LubanAgentWebHost --token 重新取地址。"));

            // 手机扫码要走的正是局域网地址（127.0.0.1 在手机上指向手机自己），故只印这一个
            PrintEntryQr(lanEntryUrl);

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
    /// 浏览器入口里的令牌是会话令牌，由宿主轮换、超过绝对上限即失效，本地算不出来，
    /// 所以先凭自述文件里的固定令牌请求 <c>/system/entryUrl</c>；宿主未运行时报错，不做任何猜测。
    /// 旧版宿主没有该接口（404），此时地址栏里的固定令牌仍然可用，退回旧拼法。
    /// </remarks>
    /// <returns>退出码：0 已打印；1 没有可用实例或取不到入口。</returns>
    private static async Task<int> PrintEntryUrlsAsync()
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

        var lookup = await RequestEntryUrlAsync(descriptor.BaseAddress, descriptor.Token).ConfigureAwait(false);
        WebEntryUrlDto? entry = lookup.Entry;
        if (entry is null && lookup.Unsupported)
        {
            // 旧版宿主没有会话令牌：地址栏里放固定令牌仍然放行
            Console.Error.WriteLine("[webhost] 运行中的实例版本较旧（没有 /system/entryUrl），改用自述文件里的固定令牌");
            entry = new WebEntryUrlDto(
                WebHostAccess.BuildLanEntryUrl(port, descriptor.Token),
                WebHostAccess.BuildEntryUrl($"http://127.0.0.1:{port}", descriptor.Token),
                DateTimeOffset.MaxValue);
        }

        if (entry is null)
        {
            Console.Error.WriteLine($"[webhost] 取不到入口地址：{lookup.Error}");
            return 1;
        }

        Console.WriteLine($"[webhost] 运行中：pid={descriptor.ProcessId} {descriptor.BaseAddress}");
        Console.WriteLine($"[webhost] 浏览器入口：{entry.LanUrl}");
        Console.WriteLine($"[webhost] 本机入口：  {entry.LocalUrl}");
        if (entry.ExpiresAt != DateTimeOffset.MaxValue)
        {
            Console.WriteLine($"[webhost] 本次会话最长到 {entry.ExpiresAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}，之后需再次执行本命令。");
        }

        PrintEntryQr(entry.LanUrl);
        return 0;
    }

    /// <summary>
    /// 用固定令牌向运行中的宿主取当前入口地址。
    /// </summary>
    /// <param name="baseAddress">宿主监听地址。</param>
    /// <param name="token">自述文件里的固定令牌。</param>
    /// <returns>入口地址；<c>Unsupported</c> 表示宿主没有该接口（旧版本），<c>Error</c> 为失败原因。</returns>
    private static async Task<(WebEntryUrlDto? Entry, bool Unsupported, string? Error)> RequestEntryUrlAsync(
        string baseAddress,
        string token)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseAddress.TrimEnd('/')}/system/entryUrl");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

            using var response = await http.SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (null, true, "宿主没有 /system/entryUrl");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (null, false, $"HTTP {(int)response.StatusCode}");
            }

            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var envelope = AgentApiJson.Deserialize<ApiEnvelope>(text);
            if (envelope is not { Success: true })
            {
                return (null, false, envelope?.Message ?? "宿主未返回入口地址");
            }

            var entry = envelope.GetData<WebEntryUrlDto>();
            return entry is null
                ? (null, false, "响应里没有入口地址")
                : (entry, false, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (null, false, ex.Message);
        }
    }

    /// <summary>
    /// 在终端里打印局域网入口的二维码，手机扫码即可打开本实例。
    /// </summary>
    /// <remarks>
    /// 输出被重定向时（客户端拉起的宿主子进程没有控制台）直接跳过：
    /// 二维码是给人扫的，把几十行色块塞进被捕获的日志没有意义。
    /// </remarks>
    /// <param name="entryUrl">带令牌的局域网入口地址。</param>
    private static void PrintEntryQr(string entryUrl)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            entryUrl.WriteQrCode();
            Console.WriteLine("[webhost] 手机扫上面的二维码即可打开（与电脑处于同一局域网）");
        }
        catch (Exception ex)
        {
            // 二维码只是便利入口，画不出来不能影响启动与地址打印
            Console.Error.WriteLine($"[webhost] 生成二维码失败：{ex.Message}");
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