/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebHostSettings
*描述：WebHost 的启动参数（命令行优先于配置文件）
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// WebHost 启动参数。
/// </summary>
public sealed record WebHostSettings
{
    /// <summary>监听端口；客户端只探活/拉起，不再用随机端口覆盖（可被命令行 <c>--port</c> 覆盖）。</summary>
    public int Port { get; init; } = 18080;

    /// <summary>访问令牌；为空表示启动时随机生成，仅本次运行有效（重启即换）。只用于 <c>Authorization: Bearer</c>。</summary>
    public string? Token { get; init; }

    /// <summary>浏览器会话令牌的有效期（分钟）；到期后的下一次请求自动换新。</summary>
    public double SessionTokenMinutes { get; init; } = 15;

    /// <summary>浏览器会话令牌换新后旧值的宽限期（分钟）；覆盖轮换瞬间还在路上的并发请求。</summary>
    public double SessionGraceMinutes { get; init; } = 5;

    /// <summary>浏览器会话的绝对上限（小时）；<c>0</c> 表示不设上限，会话随宿主进程存活。</summary>
    public double SessionMaxHours { get; init; } = 0;

    /// <summary>SAEA 的同步等待上限（秒）；必须远大于最长回合（SSE 与同步索引都可能跑很久）。</summary>
    public double TimeoutSeconds { get; init; } = 86400;

    /// <summary>是否以 HTTPS 提供服务（自签证书）；默认开启。</summary>
    public bool UseHttps { get; init; } = true;

    /// <summary>自签证书的 pfx 路径；为空时取当前用户本地应用数据目录下的 <c>webhost-cert.pfx</c>。</summary>
    public string? CertificatePath { get; init; }

    /// <summary>
    /// 是否把宿主证书装入当前用户「受信任的根证书颁发机构」；默认开启。
    /// </summary>
    /// <remarks>
    /// 只影响浏览器是否显示证书警告，不参与鉴权：客户端的指纹绑定独立于此开关。
    /// 未启用 HTTPS 时本项无效。
    /// </remarks>
    public bool TrustCertificate { get; init; } = true;

    /// <summary>SSE 心跳间隔（秒）。</summary>
    public int HeartbeatSeconds { get; init; } = 15;

    /// <summary>宿主身份；为空时取 <c>LocalUserDefaults.DefaultUserId</c>。</summary>
    public string? UserId { get; init; }

    /// <summary>
    /// 插件根目录；为空时取宿主程序目录下的 <c>plugins</c>。
    /// </summary>
    /// <remarks>
    /// 插件只随客户端分发，自动拉起时由客户端启动器用 <c>--plugins-dir</c> 指过来。
    /// </remarks>
    public string? PluginsDir { get; init; }

    /// <summary>
    /// Web 插件根目录；为空时取 <c>%LocalAppData%/LuBan/AIAgent/webhost/plugins</c>。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PluginsDir"/> 分开：Web 端自己安装、自己启停的插件落在这里，
    /// 桌面端（Codex）的插件目录不受影响。两个根都会被扫描，但管理页只呈现本根下的插件。
    /// </remarks>
    public string? WebPluginsDir { get; init; }

    /// <summary>是否输出框架调试日志。</summary>
    public bool Debug { get; init; }

    /// <summary>
    /// 从配置文件与命令行构造；命令行
    /// <c>--port/--host-token/--user/--plugins-dir/--web-plugins-dir/--timeout/--cert-path/--http/--no-trust-cert/--debug</c> 优先。
    /// </summary>
    /// <param name="configuration">应用配置。</param>
    /// <param name="args">命令行参数。</param>
    /// <returns>启动参数。</returns>
    public static WebHostSettings Create(IConfiguration configuration, string[] args)
    {
        var section = configuration.GetSection("LubanAgentWeb");
        var settings = new WebHostSettings
        {
            Port = section.GetValue("Port", 18080),
            Token = NullIfBlank(section["Token"]),
            SessionTokenMinutes = section.GetValue("SessionTokenMinutes", 15d),
            SessionGraceMinutes = section.GetValue("SessionGraceMinutes", 5d),
            SessionMaxHours = section.GetValue("SessionMaxHours", 0d),
            TimeoutSeconds = section.GetValue("TimeoutSeconds", 86400d),
            HeartbeatSeconds = section.GetValue("HeartbeatSeconds", 15),
            UseHttps = section.GetValue("UseHttps", true),
            CertificatePath = NullIfBlank(section["CertificatePath"]),
            TrustCertificate = section.GetValue("TrustCertificate", true),
            UserId = NullIfBlank(section["UserId"]),
            PluginsDir = NullIfBlank(section["PluginsDir"]),
            WebPluginsDir = NullIfBlank(section["WebPluginsDir"]),
            Debug = section.GetValue("Debug", false)
        };

        for (var i = 0; i < args.Length; i++)
        {
            var (key, inlineValue) = SplitArgument(args[i]);
            switch (key)
            {
                case "--port":
                    settings = settings with { Port = ParseInt(TakeValue(args, ref i, inlineValue), settings.Port) };
                    break;
                case "--host-token":
                    settings = settings with { Token = NullIfBlank(TakeValue(args, ref i, inlineValue)) };
                    break;
                case "--user":
                    settings = settings with { UserId = NullIfBlank(TakeValue(args, ref i, inlineValue)) };
                    break;
                case "--plugins-dir":
                    settings = settings with { PluginsDir = NullIfBlank(TakeValue(args, ref i, inlineValue)) };
                    break;
                case "--web-plugins-dir":
                    settings = settings with { WebPluginsDir = NullIfBlank(TakeValue(args, ref i, inlineValue)) };
                    break;
                case "--timeout":
                    settings = settings with { TimeoutSeconds = ParseDouble(TakeValue(args, ref i, inlineValue), settings.TimeoutSeconds) };
                    break;
                case "--http":
                    settings = settings with { UseHttps = false };
                    break;
                case "--cert-path":
                    settings = settings with { CertificatePath = NullIfBlank(TakeValue(args, ref i, inlineValue)) };
                    break;
                case "--no-trust-cert":
                    settings = settings with { TrustCertificate = false };
                    break;
                case "--debug":
                    settings = settings with { Debug = true };
                    break;
            }
        }

        return settings;
    }

    private static (string Key, string? InlineValue) SplitArgument(string arg)
    {
        var index = arg.IndexOf('=');
        return index < 0 ? (arg, null) : (arg[..index], arg[(index + 1)..]);
    }

    private static string? TakeValue(string[] args, ref int index, string? inlineValue)
    {
        if (inlineValue is not null) return inlineValue;
        if (index + 1 >= args.Length) return null;
        index++;
        return args[index];
    }

    private static int ParseInt(string? text, int fallback)
        => int.TryParse(text, out var value) && value > 0 ? value : fallback;

    private static double ParseDouble(string? text, double fallback)
        => double.TryParse(text, out var value) && value > 0 ? value : fallback;

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}