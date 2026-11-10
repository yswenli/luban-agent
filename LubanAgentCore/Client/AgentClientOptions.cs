/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： AgentClientOptions
*描述：客户端接入 WebHost 的参数
*
*****************************************************************************/

namespace LubanAgentCore.Client;

/// <summary>
/// 客户端接入参数。
/// </summary>
/// <remarks>
/// <see cref="BaseAddress"/> 有值时直连该地址、不拉子进程；为空时按
/// <see cref="AutoLaunch"/> 决定是否自动接管或拉起本地 WebHost。
/// 宿主自身的监听端口、令牌、超时与用户身份一律以宿主配置为准，客户端不再下发。
/// </remarks>
public sealed record AgentClientOptions
{
    /// <summary>服务地址（如 <c>http://127.0.0.1:18080</c>）；null 表示自动接管或拉起本地宿主。</summary>
    public Uri? BaseAddress { get; init; }

    /// <summary>共享令牌；仅在指定 <see cref="BaseAddress"/> 直连时使用。自动接管时取宿主发现文件里的令牌。</summary>
    public string? Token { get; init; }

    /// <summary>未指定 <see cref="BaseAddress"/> 时，是否自动接管已运行的宿主、未运行时再拉起。</summary>
    public bool AutoLaunch { get; init; } = true;

    /// <summary>WebHost 可执行文件路径；null 时按下列顺序解析：本参数 → 环境变量 → 客户端输出目录 → 向上逐级查找。</summary>
    public string? ServerPath { get; init; }

    /// <summary>子进程工作目录；默认当前目录，用于对齐相对路径类输入（数据库由宿主按程序目录固定，不受此值影响）。</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 插件根目录；null 时取客户端程序目录下的 <c>plugins</c>。
    /// </summary>
    /// <remarks>
    /// 插件只随客户端分发：拉起宿主时把客户端的插件根目录交给宿主进程扫描，避免两处各带一份插件。
    /// 目录不存在时不下发，让宿主回落自身默认目录。
    /// </remarks>
    public string? PluginsDirectory { get; init; }

    /// <summary>等待服务就绪的超时时间。</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>是否让宿主输出框架调试日志（透传 <c>--debug</c>）。</summary>
    public bool Debug { get; init; }
}