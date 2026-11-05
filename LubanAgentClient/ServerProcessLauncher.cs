/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Client
*文件名： ServerProcessLauncher
*描述：拉起本地 WebHost 子进程并等待就绪
*
*****************************************************************************/

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace LubanAgent.Client;

/// <summary>
/// WebHost 子进程启动器。
/// </summary>
/// <remarks>
/// <para>
/// 端口每次取空闲端口再以 <c>--port</c> 传给子进程，避免固定端口与既有进程冲突；
/// 自动拉起时默认生成一次性共享令牌（仅本机通信，令牌不落盘、不写日志）。
/// </para>
/// <para>
/// 子进程的工作目录默认沿用客户端当前目录：宿主按相对路径解析工作区根目录等输入，
/// 两边同目录才能保持一致。数据库不受工作目录影响，见 <see cref="AgentClientOptions.WorkingDirectory"/>。
/// </para>
/// </remarks>
public sealed class ServerProcessLauncher : IAsyncDisposable
{
    /// <summary>指定 WebHost 可执行文件的环境变量名（优先于自动探测）。</summary>
    public const string ServerPathVariable = "LUBAN_AGENT_WEBHOST";

    private readonly AgentClientOptions _options;
    private readonly LinkedList<string> _output = new();
    private readonly Lock _outputGate = new();
    private readonly string _token;
    private Process? _process;
    private int _disposed;

    /// <summary>创建启动器。</summary>
    /// <param name="options">客户端参数。</param>
    public ServerProcessLauncher(AgentClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;

        // 未显式给令牌时生成一次性令牌：子进程与客户端同机，令牌只走命令行与 Authorization 头
        _token = string.IsNullOrWhiteSpace(options.Token)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            : options.Token.Trim();
    }

    /// <summary>服务地址；<see cref="StartAsync"/> 成功后可用。</summary>
    public Uri? BaseAddress { get; private set; }

    /// <summary>本次启动使用的共享令牌。</summary>
    public string Token => _token;

    /// <summary>
    /// 启动子进程并等待 <c>/system/health</c> 返回就绪。
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

        var executable = ResolveServerPath()
            ?? throw new AgentServerException(
                503,
                $"未找到 LubanAgentWebHost：请设置环境变量 {ServerPathVariable}，"
                + "或把 WebHost 发布到客户端同目录，或在 LubanAgentClient 中显式指定 BaseAddress。");

        var port = FindFreePort();
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

        foreach (var argument in BuildArguments(_options, port, _token))
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

        var address = new Uri($"http://127.0.0.1:{port}");
        var deadline = DateTime.UtcNow + _options.StartupTimeout;

        using var probe = new AgentServerClient(address, _token);
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new AgentServerException(
                    process.ExitCode,
                    $"LubanAgentWebHost 启动后退出（exit={process.ExitCode}）：{TailOutput()}");
            }

            try
            {
                await probe.GetHealthAsync(ct).ConfigureAwait(false);
                BaseAddress = address;
                return address;
            }
            catch (AgentServerException)
            {
                // 尚未就绪：服务未监听或令牌未生效，继续轮询
            }
            catch (HttpRequestException)
            {
                // 尚未就绪：连接被拒绝
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
    /// 构造子进程参数列表：端口、共享令牌、同步等待上限、插件根目录（存在时）、宿主身份（指定时）、调试开关。
    /// </summary>
    /// <remarks>
    /// 插件只随客户端分发：把客户端的插件根目录交给宿主进程扫描，避免两处各带一份插件。
    /// 目录不存在时不下发，让宿主回落自身默认目录。
    /// </remarks>
    /// <param name="options">客户端参数。</param>
    /// <param name="port">监听端口。</param>
    /// <param name="token">共享令牌。</param>
    /// <returns>参数列表（不含可执行文件名）。</returns>
    internal static List<string> BuildArguments(AgentClientOptions options, int port, string token)
    {
        ArgumentNullException.ThrowIfNull(options);

        var args = new List<string>
        {
            "--port", port.ToString(),
            "--token", token,
            "--timeout", options.SessionTimeoutSeconds.ToString()
        };

        var pluginsDirectory = string.IsNullOrWhiteSpace(options.PluginsDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "plugins")
            : Path.GetFullPath(options.PluginsDirectory!);
        if (Directory.Exists(pluginsDirectory))
        {
            args.Add("--plugins-dir");
            args.Add(pluginsDirectory);
        }

        if (!string.IsNullOrWhiteSpace(options.UserId))
        {
            args.Add("--user");
            args.Add(options.UserId);
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

    /// <summary>取一个空闲的本机端口。</summary>
    /// <returns>端口号。</returns>
    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
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

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"停止 LubanAgentWebHost 失败：{ex.Message}");
        }
        finally
        {
            process.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}