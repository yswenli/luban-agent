/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： GitProcess
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：git 命令薄封装，统一参数拼接、编码与错误处理
*
*****************************************************************************/

namespace LubanAgentCore.Services;

using System.ComponentModel;

/// <summary>
/// git 命令薄封装：基于 <see cref="ProcessRunner"/> 执行 git，统一 UTF-8 编码与失败抛错。
/// </summary>
public sealed class GitProcess(ProcessRunner runner)
{
    private readonly ProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>
    /// 在指定目录执行 git，返回原始结果（不因非零退出码抛错）。
    /// </summary>
    /// <param name="workingDir">工作目录。</param>
    /// <param name="args">git 子命令与参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进程执行结果。</returns>
    public Task<ProcessResult> RunRawAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct = default)
        => _runner.RunAsync(
            "git",
            arguments: "",
            workingDir: workingDir,
            stdin: null,
            timeoutMs: 120000,
            cancellationToken: ct,
            argumentList: args,
            outputEncoding: Encoding.UTF8);

    /// <summary>
    /// 在指定目录执行 git，非零退出码时抛 <see cref="InvalidOperationException"/>，返回标准输出。
    /// </summary>
    /// <param name="workingDir">工作目录。</param>
    /// <param name="args">git 子命令与参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>标准输出（未裁剪）。</returns>
    public async Task<string> RunAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var result = await RunRawAsync(workingDir, args, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} 失败（exit={result.ExitCode}）：{detail.Trim()}");
        }
        return result.StandardOutput;
    }

    /// <summary>
    /// 执行 git，并按需注入 HTTPS 凭据。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 凭据不拼进 URL、不进命令行参数、不写日志：只写进一次性临时 git 配置的
    /// <c>http.&lt;url&gt;.extraHeader</c>（Basic 认证头），配置路径通过
    /// <c>GIT_CONFIG_GLOBAL</c> 交给子进程，命令结束后删除临时目录。
    /// </para>
    /// <para>
    /// 注入凭据时同时用 <c>-c credential.helper=</c> 清空凭据助手，并设
    /// <c>GIT_TERMINAL_PROMPT=0</c>，保证认证失败立刻返回而不是弹窗或等待输入。
    /// </para>
    /// </remarks>
    /// <param name="workingDir">工作目录（必须已存在）。</param>
    /// <param name="args">git 子命令与参数。</param>
    /// <param name="url">仓库地址解析结果；为空表示不注入凭据。</param>
    /// <param name="userName">凭据用户名。</param>
    /// <param name="secret">凭据密码 / 令牌。</param>
    /// <param name="timeoutMs">超时时间（毫秒）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>执行结果（不因非零退出码抛错）。</returns>
    public async Task<GitExecResult> RunWithCredentialAsync(
        string workingDir,
        IReadOnlyList<string> args,
        GitUrlInfo? url,
        string? userName,
        string? secret,
        int timeoutMs,
        CancellationToken ct = default)
    {
        var effectiveArgs = new List<string>(args);
        string? tempDirectory = null;
        try
        {
            var withCredential = url is not null
                                 && url.SupportsCredential
                                 && !string.IsNullOrEmpty(userName)
                                 && !string.IsNullOrEmpty(secret);

            if (withCredential)
            {
                tempDirectory = Path.Combine(Path.GetTempPath(), "luban-git-" + Guid.NewGuid().ToString("N"));
                CreatePrivateDirectory(tempDirectory);

                var configPath = Path.Combine(tempDirectory, "gitconfig");
                await WriteCredentialConfigAsync(
                    configPath,
                    BuildCredentialConfig(url!, userName!, secret!),
                    ct).ConfigureAwait(false);

                effectiveArgs.InsertRange(0, ["-c", "credential.helper="]);
            }

            return await ExecuteAsync(
                    workingDir,
                    effectiveArgs,
                    tempDirectory is null ? null : tempDirectory,
                    userName,
                    secret,
                    timeoutMs,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            if (tempDirectory is not null)
            {
                TryDeleteDirectory(tempDirectory);
            }
        }
    }

    /// <summary>
    /// 一次性 git 配置：包含用户原有全局配置，再叠加本次请求的 Basic 认证头。
    /// </summary>
    /// <param name="url">仓库地址。</param>
    /// <param name="userName">用户名。</param>
    /// <param name="secret">密码 / 令牌。</param>
    /// <returns>配置文本。</returns>
    private static string BuildCredentialConfig(GitUrlInfo url, string userName, string secret)
    {
        var builder = new StringBuilder();

        // GIT_CONFIG_GLOBAL 覆盖默认全局配置：显式 include 回来，保留代理 / 证书 / insteadOf 等设置
        foreach (var globalPath in EnumerateGlobalConfigPaths())
        {
            builder.Append("[include]\n\tpath = \"")
                .Append(globalPath.Replace("\\", "/").Replace("\"", "\\\""))
                .Append("\"\n");
        }

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{secret}"));
        var origin = url.Port > 0 && !IsDefaultPort(url)
            ? $"{url.Scheme}://{url.Host}:{url.Port}/"
            : $"{url.Scheme}://{url.Host}/";

        builder.Append("[http \"")
            .Append(origin.Replace("\"", "\\\""))
            .Append("\"]\n\textraHeader = Authorization: Basic ")
            .Append(token)
            .Append('\n');
        return builder.ToString();
    }

    /// <summary>是否为协议默认端口（默认端口不写进 URL 匹配串）。</summary>
    private static bool IsDefaultPort(GitUrlInfo url)
        => (url.Scheme == "https" && url.Port == 443) || (url.Scheme == "http" && url.Port == 80);

    /// <summary>枚举需要 include 回来的全局配置文件（存在的才返回）。</summary>
    private static IEnumerable<string> EnumerateGlobalConfigPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var configured = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured) && seen.Add(configured))
        {
            yield return configured;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            var userConfig = Path.Combine(home, ".gitconfig");
            if (File.Exists(userConfig) && seen.Add(userConfig))
            {
                yield return userConfig;
            }
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(xdg) && home.Length > 0)
        {
            xdg = Path.Combine(home, ".config");
        }

        if (!string.IsNullOrWhiteSpace(xdg))
        {
            var xdgConfig = Path.Combine(xdg, "git", "config");
            if (File.Exists(xdgConfig) && seen.Add(xdgConfig))
            {
                yield return xdgConfig;
            }
        }
    }

    /// <summary>执行子进程并收集输出；超时/取消时杀掉整棵进程树。</summary>
    private static async Task<GitExecResult> ExecuteAsync(
        string workingDir,
        IReadOnlyList<string> args,
        string? configDirectory,
        string? userName,
        string? secret,
        int timeoutMs,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (configDirectory is not null)
        {
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(configDirectory, "gitconfig");
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new GitExecResult(-1, string.Empty, $"git 无法启动：{ex.Message}", false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            KillProcessTree(process);
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        var stdout = Sanitize(await SafeReadAsync(stdoutTask), userName, secret);
        var stderr = Sanitize(await SafeReadAsync(stderrTask), userName, secret);
        return new GitExecResult(process.ExitCode, stdout, stderr, timedOut);
    }

    /// <summary>读取输出流；进程被杀导致的读取异常按空串处理。</summary>
    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    /// <summary>错误输出脱敏：令牌、Basic 认证头与 URL 内嵌凭据一律替换为 ***。</summary>
    /// <param name="text">原始输出。</param>
    /// <param name="userName">凭据用户名。</param>
    /// <param name="secret">凭据密码 / 令牌。</param>
    /// <returns>脱敏后的文本。</returns>
    private static string Sanitize(string text, string? userName, string? secret)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;
        if (!string.IsNullOrEmpty(secret))
        {
            result = result.Replace(secret, "***", StringComparison.Ordinal);
            var encoded = Uri.EscapeDataString(secret);
            if (encoded.Length > 0 && encoded != secret)
            {
                result = result.Replace(encoded, "***", StringComparison.Ordinal);
            }

            // extraHeader 是 base64(user:secret)：万一被上游回显，同样不能原样留下
            if (!string.IsNullOrEmpty(userName))
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{secret}"));
                result = result.Replace(basic, "***", StringComparison.Ordinal);
            }
        }

        // https://user:pass@host → https://***@host
        return Regex.Replace(
            result,
            "([a-zA-Z][a-zA-Z0-9+.-]*://)[^/@\\s]+@",
            "$1***@",
            RegexOptions.CultureInvariant);
    }

    /// <summary>杀掉进程树；失败忽略。</summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 进程已退出或无权终止：忽略
        }
    }

    /// <summary>创建仅当前用户可访问的临时目录（Unix 0700；Windows 依赖用户目录 ACL）。</summary>
    /// <param name="path">目录路径。</param>
    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// 写入一次性 git 配置：Unix 下创建即 0600，避免「先普通权限写入、再收紧」的窗口。
    /// </summary>
    /// <param name="path">配置文件路径。</param>
    /// <param name="content">配置内容（含 Basic 认证头）。</param>
    /// <param name="ct">取消令牌。</param>
    private static async Task WriteCredentialConfigAsync(string path, string content, CancellationToken ct)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(path, options);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>删除临时目录；失败只忽略（系统临时目录会被定期清理）。</summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }

            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 忽略
        }
    }
}

/// <summary>
/// git 执行结果（含是否超时），供调用方做错误归类。
/// </summary>
/// <param name="ExitCode">退出码。</param>
/// <param name="StandardOutput">标准输出（已脱敏）。</param>
/// <param name="StandardError">标准错误（已脱敏）。</param>
/// <param name="TimedOut">是否因超时被终止。</param>
public sealed record GitExecResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    /// <summary>是否执行成功。</summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}