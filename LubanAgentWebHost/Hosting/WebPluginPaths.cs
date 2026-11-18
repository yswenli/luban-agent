/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebPluginPaths
*描述：Web 插件相关路径解析与越界校验（纯函数，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// Web 插件路径：插件根、状态文件、页面镜像根，以及路径归一等纯函数。
/// </summary>
/// <remarks>
/// <para>
/// 插件根与状态文件都必须与桌面端分开：桌面端的插件根经 <c>--plugins-dir</c> 下发，
/// 本类给出的是 Web 自己的根（<c>--web-plugins-dir</c> 或 <c>%LocalAppData%/LuBan/AIAgent/webhost/plugins</c>）。
/// </para>
/// <para>
/// 页面镜像必须落在静态根（<c>AppContext.BaseDirectory/wwwroot</c>）之下，
/// 静态通道按物理路径缓存字节，故镜像目录带一次性的 <c>stamp</c>，每次重建都换新路径。
/// </para>
/// </remarks>
public static class WebPluginPaths
{
    /// <summary>插件清单文件名。</summary>
    public const string ManifestFileName = "plugin.json";

    /// <summary>插件自带页面的目录名（页面资源必须位于其下）。</summary>
    public const string WebFolderName = "web";

    /// <summary>页面镜像在静态根下的目录名。</summary>
    public const string MirrorFolderName = "plugin-web";

    /// <summary>页面镜像 URL 前缀。</summary>
    public const string MirrorUrlPrefix = "/" + MirrorFolderName;

    /// <summary>
    /// Web 插件根默认值：<c>%LocalAppData%/LuBan/AIAgent/webhost/plugins</c>。
    /// </summary>
    public static string DefaultWebPluginsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuBan",
        "AIAgent",
        "webhost",
        "plugins");

    /// <summary>
    /// 插件状态文件默认值：<c>%LocalAppData%/LuBan/AIAgent/webhost/plugin-states.json</c>。
    /// </summary>
    public static string DefaultStateFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuBan",
        "AIAgent",
        "webhost",
        "plugin-states.json");

    /// <summary>
    /// 桌面端插件根默认值：宿主程序目录下的 <c>plugins</c>（框架的默认根）。
    /// </summary>
    public static string DefaultDesktopPluginsRoot => Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// 页面镜像根：<c>AppContext.BaseDirectory/wwwroot/plugin-web</c>。
    /// </summary>
    /// <remarks>
    /// 静态根不可配，运行时只服务输出目录里的 <c>wwwroot</c>，因此镜像只写这一份，
    /// 不写源码树。
    /// </remarks>
    public static string MirrorRoot => Path.Combine(AppContext.BaseDirectory, "wwwroot", MirrorFolderName);

    /// <summary>
    /// 解析 Web 插件根：显式配置优先，否则用默认值。
    /// </summary>
    /// <param name="configured">配置或命令行给出的路径。</param>
    /// <param name="fallback">缺省根；为空时用 <see cref="DefaultWebPluginsRoot"/>。</param>
    /// <returns>绝对路径。</returns>
    public static string ResolveWebPluginsRoot(string? configured, string? fallback = null)
        => ResolveRoot(configured, string.IsNullOrWhiteSpace(fallback) ? DefaultWebPluginsRoot : fallback!);

    /// <summary>
    /// 解析桌面端插件根：显式配置优先，否则用宿主程序目录下的 <c>plugins</c>。
    /// </summary>
    /// <param name="configured">配置或命令行给出的路径。</param>
    /// <returns>绝对路径。</returns>
    public static string ResolveDesktopPluginsRoot(string? configured)
        => ResolveRoot(configured, DefaultDesktopPluginsRoot);

    /// <summary>
    /// 生成页面镜像用的 <c>stamp</c>（8 位小写十六进制）。
    /// </summary>
    /// <returns>随机标识。</returns>
    public static string CreateStamp()
        => System.Security.Cryptography.RandomNumberGenerator.GetHexString(8).ToLowerInvariant();

    /// <summary>
    /// 把相对路径归一化为以 <c>/</c> 分隔的形式；非法（绝对路径、<c>..</c>、盘符、空段）返回 <c>null</c>。
    /// </summary>
    /// <param name="relative">原始相对路径。</param>
    /// <returns>归一化路径；非法时 <c>null</c>。</returns>
    public static string? NormalizeRelative(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var value = relative.Trim().Replace('\\', '/');
        if (value.Length == 0 || value.StartsWith('/') || value.Contains(':'))
        {
            return null;
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                return null;
            }
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// 判断路径是否位于给定根之内（含根本身）；两边先取绝对路径。
    /// </summary>
    /// <param name="path">待判定路径。</param>
    /// <param name="root">根目录。</param>
    /// <returns>在根之内返回 true。</returns>
    public static bool IsUnder(string? path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string fullPath;
        string fullRoot;
        try
        {
            fullPath = Path.GetFullPath(path);
            fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断相对路径是否位于 <c>web/</c> 之下。
    /// </summary>
    /// <param name="normalized">已归一化的相对路径。</param>
    /// <returns>位于 <c>web/</c> 之下返回 true。</returns>
    public static bool IsUnderWebFolder(string? normalized)
        => normalized is not null
            && normalized.StartsWith(WebFolderName + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 判断扩展名能否作为页面入口（只接受 <c>.html/.htm</c>）。
    /// </summary>
    /// <param name="normalized">已归一化的相对路径。</param>
    /// <returns>是页面文件返回 true。</returns>
    public static bool IsHtmlPage(string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var extension = Path.GetExtension(normalized);
        return extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 页面资源白名单：白名单外的扩展名在静态通道里拿不到可用 MIME，只能提示、不阻断安装。
    /// </summary>
    private static readonly HashSet<string> MirrorAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".css", ".js", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".txt"
    };

    /// <summary>
    /// 判断扩展名是否在页面资源白名单内。
    /// </summary>
    /// <param name="normalized">已归一化的相对路径。</param>
    /// <returns>在白名单内返回 true。</returns>
    public static bool IsMirrorAsset(string? normalized)
        => normalized is not null && MirrorAssetExtensions.Contains(Path.GetExtension(normalized));

    /// <summary>
    /// 宿主控制器名集合：页面目录名与之同名时，该目录下的文件会先被当路由，安装期必须拒绝。
    /// </summary>
    private static readonly HashSet<string> HostControllerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "plugin", "session", "agent", "attachment", "mcp", "model", "llmwiki",
        "a2a", "agentsmd", "system", "provider", "skill", "rule", "workspace",
        "yunxiao"
    };

    /// <summary>
    /// 判断目录名是否与宿主控制器同名。
    /// </summary>
    /// <param name="directoryName">目录名。</param>
    /// <returns>同名返回 true。</returns>
    public static bool IsHostControllerDirectoryName(string? directoryName)
        => !string.IsNullOrWhiteSpace(directoryName) && HostControllerNames.Contains(directoryName.Trim());

    /// <summary>
    /// 页面镜像目录：<c>wwwroot/plugin-web/{pluginId}/{stamp}</c>。
    /// </summary>
    /// <param name="pluginId">插件标识。</param>
    /// <param name="stamp">镜像戳。</param>
    /// <returns>绝对路径。</returns>
    public static string MirrorDirectory(string pluginId, string stamp)
        => Path.Combine(MirrorRoot, pluginId, stamp);

    /// <summary>
    /// 页面资源 URL：<c>/plugin-web/{pluginId}/{stamp}/{relative}</c>。
    /// </summary>
    /// <param name="pluginId">插件标识。</param>
    /// <param name="stamp">镜像戳。</param>
    /// <param name="relative">已归一化的包内相对路径（如 <c>web/index.html</c>）。</param>
    /// <returns>URL 路径。</returns>
    /// <remarks>
    /// 逐段转义：包内文件名允许空格、<c>#</c>、<c>?</c> 等字符，
    /// 原样拼进 URL 会让 iframe 地址被截断（<c>#</c> 之后变成 fragment）。
    /// </remarks>
    public static string PageUrl(string pluginId, string stamp, string relative)
    {
        var segments = relative
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"{MirrorUrlPrefix}/{Uri.EscapeDataString(pluginId)}/{Uri.EscapeDataString(stamp)}"
            + $"/{string.Join('/', segments)}";
    }

    private static string ResolveRoot(string? configured, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured!.Trim();
        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.GetFullPath(fallback);
        }
    }
}