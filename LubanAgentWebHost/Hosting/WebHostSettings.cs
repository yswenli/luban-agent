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
    /// <summary>监听端口；客户端启动器会传入空闲端口。</summary>
    public int Port { get; init; } = 18080;

    /// <summary>可选的共享令牌；为空表示不校验 <c>Authorization</c> 头。</summary>
    public string? Token { get; init; }

    /// <summary>SAEA 的同步等待上限（秒）；必须远大于最长回合（SSE 与同步索引都可能跑很久）。</summary>
    public double TimeoutSeconds { get; init; } = 86400;

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

    /// <summary>是否输出框架调试日志。</summary>
    public bool Debug { get; init; }

    /// <summary>从配置文件与命令行构造；命令行 <c>--port/--token/--user/--plugins-dir/--debug</c> 优先。</summary>
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
            TimeoutSeconds = section.GetValue("TimeoutSeconds", 86400d),
            HeartbeatSeconds = section.GetValue("HeartbeatSeconds", 15),
            UserId = NullIfBlank(section["UserId"]),
            PluginsDir = NullIfBlank(section["PluginsDir"]),
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
                case "--token":
                    settings = settings with { Token = NullIfBlank(TakeValue(args, ref i, inlineValue)) };
                    break;
                case "--user":
                    settings = settings with { UserId = NullIfBlank(TakeValue(args, ref i, inlineValue)) };
                    break;
                case "--plugins-dir":
                    settings = settings with { PluginsDir = NullIfBlank(TakeValue(args, ref i, inlineValue)) };
                    break;
                case "--timeout":
                    settings = settings with { TimeoutSeconds = ParseDouble(TakeValue(args, ref i, inlineValue), settings.TimeoutSeconds) };
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