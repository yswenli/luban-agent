/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： ServerProcessLauncher
*描述：接管已运行的 WebHost，未运行时按宿主自身配置拉起
*
*****************************************************************************/

namespace LubanAgentCore.Client;

/// <summary>
/// WebHost 生命周期入口：先接管，后拉起。
/// </summary>
/// <remarks>
/// <para>
/// 宿主是独立服务：监听端口、共享令牌、超时与用户身份都以宿主自己的配置为准。
/// 客户端启动时先读发现文件并对 <c>/system/health</c> 探活，通过即直接接管；
/// 只有宿主确实不在运行时才启动子进程，且不传 <c>--port</c>/<c>--token</c> 这类连接参数。
/// </para>
/// <para>
/// 拉起后宿主常驻：客户端退出不再结束它（用 <c>LubanAgentWebHost --stop</c> 显式停止），
/// 这样浏览器、另一个客户端都能继续连同一实例。
/// </para>
/// <para>
/// 子进程的工作目录默认沿用客户端当前目录：宿主按相对路径解析工作区根目录等输入，
/// 两边同目录才能保持一致。
/// </para>
/// </remarks>
public sealed class ServerProcessLauncher : IAsyncDisposable
{
    /// <summary>指定 WebHost 可执行文件的环境变量名（优先于自动探测）。</summary>
    public const string ServerPathVariable = "LUBAN_AGENT_WEBHOST";

    private readonly AgentClientOptions _options;
    private readonly LinkedList<string> _output = new();
    private readonly Lock _outputGate = new();
    private Process? _process;
    private int _disposed;

    /// <summary>创建启动器。</summary>
    /// <param name="options">客户端参数。</param>
    public ServerProcessLauncher(AgentClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>服务地址；<see cref="StartAsync"/> 成功后可用。</summary>
    public Uri? BaseAddress { get; private set; }

    /// <summary>宿主本次运行的访问令牌；旧版本宿主未启用令牌时为 null。</summary>
    public string? Token { get; private set; }

    /// <summary>本次拉起的子进程号；接管已有宿主时为 null。</summary>
    public int? LaunchedProcessId => _process?.Id;

    /// <summary>
    /// 接管已运行的宿主；没有可用实例时拉起子进程并等待其自述文件就绪。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>服务地址。</returns>
    /// <exception cref="AgentServerException">启动失败或就绪超时。</exception>
    public async Task<Uri> StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        if (BaseAddress is not null)
        {
            return BaseAddress;
        }

        // 1) 已有宿主在跑：直接接管，不重复拉起
        if (await TryAdoptAsync(ct).ConfigureAwait(false) is { } adopted)
        {
            return adopted;
        }

        var executable = ResolveServerPath()
            ?? throw new AgentServerException(
                503,
                $"未找到 LubanAgentWebHost：请设置环境变量 {ServerPathVariable}，"
                + "或把 WebHost 发布到客户端同目录，或在 AgentClientOptions 中显式指定 BaseAddress。");

        var workingDirectory = string.IsNullOrWhiteSpace(_options.WorkingDirectory)
            ? Environment.CurrentDirectory
            : _options.WorkingDirectory!;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (string.Equals(Path.GetExtension(executable), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(executable);
        }

        foreach (var argument in BuildArguments(_options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => AppendOutput(e.Data);
        process.ErrorDataReceived += (_, e) => AppendOutput(e.Data);

        try
        {
            if (!process.Start())
            {
                throw new AgentServerException(503, "无法启动 LubanAgentWebHost 进程");
            }
        }
        catch (Exception ex) when (ex is not AgentServerException)
        {
            process.Dispose();
            throw new AgentServerException(503, $"启动 LubanAgentWebHost 失败：{ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        var deadline = DateTime.UtcNow + _options.StartupTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // 2) 等它写下自述文件；另一客户端抢先拉起时这里也会接管成功
            if (await TryAdoptAsync(ct).ConfigureAwait(false) is { } ready)
            {
                return ready;
            }

            if (process.HasExited)
            {
                throw new AgentServerException(
                    process.ExitCode,
                    $"LubanAgentWebHost 启动后退出（exit={process.ExitCode}）：{TailOutput()}");
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new AgentServerException(
                    503,
                    $"等待 LubanAgentWebHost 就绪超时（{_options.StartupTimeout.TotalSeconds:0}s）：{TailOutput()}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 构造子进程参数列表：只下发插件根目录与调试开关，端口/令牌/超时由宿主配置决定。
    /// </summary>
    /// <remarks>
    /// 插件只随客户端分发：把客户端的插件根目录交给宿主进程扫描，避免两处各带一份插件。
    /// 目录不存在时不下发，让宿主回落自身默认目录。
    /// </remarks>
    /// <param name="options">客户端参数。</param>
    /// <returns>参数列表（不含可执行文件名）。</returns>
    internal static List<string> BuildArguments(AgentClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var args = new List<string>();
        var pluginsDirectory = string.IsNullOrWhiteSpace(options.PluginsDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "plugins")
            : Path.GetFullPath(options.PluginsDirectory!);
        if (Directory.Exists(pluginsDirectory))
        {
            args.Add("--plugins-dir");
            args.Add(pluginsDirectory);
        }

        if (options.Debug)
        {
            args.Add("--debug");
        }

        return args;
    }

    /// <summary>
    /// 解析 WebHost 可执行文件路径。
    /// </summary>
    /// <remarks>
    /// 查找顺序：环境变量 → 客户端输出目录（同目录发布）→ 自输出目录向上逐级查找
    /// <c>LubanAgentWebHost/bin/**/LubanAgentWebHost(.exe|.dll)</c>（本仓库的项目输出布局）。
    /// </remarks>
    /// <returns>可执行文件或 dll 的完整路径；找不到返回 null。</returns>
    public string? ResolveServerPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.ServerPath) && File.Exists(_options.ServerPath))
        {
            return Path.GetFullPath(_options.ServerPath);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(ServerPathVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var local = Probe(Path.Combine(AppContext.BaseDirectory, "LubanAgentWebHost"))
            ?? Probe(Path.Combine(AppContext.BaseDirectory, "LubanAgentWebHost.exe"))
            ?? Probe(Path.Combine(AppContext.BaseDirectory, "LubanAgentWebHost.dll"));
        if (local is not null)
        {
            return local;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 6 && directory is not null; level++, directory = directory.Parent)
        {
            var bin = Path.Combine(directory.FullName, "LubanAgentWebHost", "bin");
            if (!Directory.Exists(bin))
            {
                continue;
            }

            var found = Directory
                .EnumerateFiles(bin, "LubanAgentWebHost*", SearchOption.AllDirectories)
                .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), "LubanAgentWebHost", StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (found is not null)
            {
                return Path.GetFullPath(found);
            }
        }

        return null;
    }

    /// <summary>
    /// 按发现文件接管已运行的宿主；探活失败视为不可用。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>服务地址；不可用时为 null。</returns>
    private async Task<Uri?> TryAdoptAsync(CancellationToken ct)
    {
        var descriptor = WebHostDiscovery.TryRead();
        if (descriptor is null)
        {
            return null;
        }

        if (!Uri.TryCreate(descriptor.BaseAddress, UriKind.Absolute, out var address))
        {
            WebHostDiscovery.Delete(descriptor.ProcessId);
            return null;
        }

        using var probe = new AgentServerClient(address, descriptor.Token);
        try
        {
            await probe.GetHealthAsync(ct).ConfigureAwait(false);
        }
        catch (AgentServerException)
        {
            // 端口上另有服务或宿主尚未就绪：不接管，交给拉起分支处理
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }

        BaseAddress = address;
        Token = descriptor.Token;
        Logger.Info($"已接管运行中的 LubanAgentWebHost（{address}，pid={descriptor.ProcessId}）。");
        return address;
    }

    private static string? Probe(string path) => File.Exists(path) ? Path.GetFullPath(path) : null;

    private void AppendOutput(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_outputGate)
        {
            _output.AddLast(line);
            while (_output.Count > 40)
            {
                _output.RemoveFirst();
            }
        }
    }

    private string TailOutput()
    {
        lock (_outputGate)
        {
            return _output.Count == 0 ? "（无输出）" : string.Join(" | ", _output);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        var process = _process;
        _process = null;
        if (process is null)
        {
            return ValueTask.CompletedTask;
        }

        // 宿主独立运行：只释放进程句柄，不结束进程；需要停止时用 LubanAgentWebHost --stop
        try
        {
            if (!process.HasExited)
            {
                Logger.Info($"LubanAgentWebHost 已常驻（pid={process.Id}），可用 LubanAgentWebHost --stop 停止。");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取 LubanAgentWebHost 进程状态失败：{ex.Message}");
        }
        finally
        {
            process.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}