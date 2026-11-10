/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： WebHostDiscovery
*描述：独立宿主的自述文件（监听地址、令牌、进程号），供客户端探活与接管
*
*****************************************************************************/

namespace LubanAgentCore.Client;

/// <summary>
/// 宿主自述信息：启动完成后写入发现文件，客户端据此连上已在运行的实例。
/// </summary>
public sealed record WebHostDescriptor
{
    /// <summary>监听地址，形如 <c>http://127.0.0.1:18080</c>。</summary>
    public string BaseAddress { get; init; } = string.Empty;

    /// <summary>共享令牌；宿主未启用令牌时为 null。</summary>
    public string? Token { get; init; }

    /// <summary>宿主进程号，用于判断自述是否已过期。</summary>
    public int ProcessId { get; init; }

    /// <summary>宿主版本（取自程序集版本）。</summary>
    public string? Version { get; init; }

    /// <summary>宿主可执行文件路径，便于排查与停止。</summary>
    public string? ServerPath { get; init; }

    /// <summary>启动时间。</summary>
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// 发现文件的读写。
/// </summary>
/// <remarks>
/// <para>
/// 宿主是独立服务：端口与令牌以宿主自身的配置为准，客户端不再用随机端口和一次性令牌覆盖。
/// 宿主启动成功后把实际监听地址写到 <c>%LOCALAPPDATA%\luban-agent\webhost.json</c>，
/// 正常退出时删除；客户端读取后先用 <c>/system/health</c> 探活，通过则直接接管。
/// </para>
/// <para>
/// 进程号已失效（宿主被强杀、机器重启）视为过期，读取时顺带清理，避免客户端连到幽灵地址。
/// </para>
/// </remarks>
public static class WebHostDiscovery
{
    private const string DirectoryName = "luban-agent";
    private const string FileName = "webhost.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 发现文件路径；默认位于当前用户的应用数据目录。
    /// </summary>
    /// <remarks>测试通过 <see cref="FilePathOverride"/> 指向临时目录，避免污染真实用户目录。</remarks>
    public static string FilePath => FilePathOverride ?? Path.Combine(ResolveDirectory(), FileName);

    /// <summary>测试用的路径覆盖；为 null 时按默认规则解析。</summary>
    internal static string? FilePathOverride { get; set; }

    /// <summary>
    /// 读取发现文件。
    /// </summary>
    /// <returns>可用的自述信息；文件缺失、内容损坏或进程已退出时返回 null。</returns>
    public static WebHostDescriptor? TryRead()
    {
        var path = FilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        WebHostDescriptor? descriptor;
        try
        {
            descriptor = JsonSerializer.Deserialize<WebHostDescriptor>(File.ReadAllText(path), SerializerOptions);
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取宿主发现文件失败，按未启动处理（{path}）：{ex.Message}");
            TryDelete(path);
            return null;
        }

        if (descriptor is null
            || string.IsNullOrWhiteSpace(descriptor.BaseAddress)
            || descriptor.ProcessId <= 0
            || !IsProcessAlive(descriptor.ProcessId))
        {
            TryDelete(path);
            return null;
        }

        return descriptor;
    }

    /// <summary>
    /// 写入发现文件（先写临时文件再替换，避免客户端读到半截内容）。
    /// </summary>
    /// <param name="descriptor">宿主自述信息。</param>
    public static void Write(WebHostDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var path = FilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(descriptor, SerializerOptions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// 删除发现文件。
    /// </summary>
    /// <param name="processId">调用方进程号；文件已属于别的实例时不删，避免误删新宿主的自述。</param>
    public static void Delete(int processId)
    {
        var path = FilePath;
        var existing = TryParse(path);
        if (existing is not null && existing.ProcessId != processId)
        {
            return;
        }

        TryDelete(path);
    }

    /// <summary>仅解析内容，不做存活校验。</summary>
    private static WebHostDescriptor? TryParse(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<WebHostDescriptor>(File.ReadAllText(path), SerializerOptions)
                : null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"解析宿主发现文件失败（{path}）：{ex.Message}");
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"清理宿主发现文件失败（{path}）：{ex.Message}");
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // 进程不存在
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // 权限不足等情况无法确认存活；按不可用处理，由调用方重新拉起
            Logger.Warn($"无法确认宿主进程存活（pid={processId}）：{ex.Message}");
            return false;
        }
    }

    private static string ResolveDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        return Path.Combine(root, DirectoryName);
    }
}