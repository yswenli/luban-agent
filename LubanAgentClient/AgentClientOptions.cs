/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Client
*文件名： AgentClientOptions
*描述：客户端接入 WebHost 的参数
*
*****************************************************************************/

namespace LubanAgent.Client;

/// <summary>
/// 客户端接入参数。
/// </summary>
/// <remarks>
/// <see cref="BaseAddress"/> 有值时直连已有服务、不拉子进程；为空时按
/// <see cref="AutoLaunch"/> 决定是否自动拉起 WebHost。
/// </remarks>
public sealed record AgentClientOptions
{
    /// <summary>服务地址（如 <c>http://127.0.0.1:18080</c>）；null 表示自动拉起子进程。</summary>
    public Uri? BaseAddress { get; init; }

    /// <summary>共享令牌；服务端未启用令牌时可为 null。</summary>
    public string? Token { get; init; }

    /// <summary>未指定 <see cref="BaseAddress"/> 时是否自动拉起 WebHost 子进程。</summary>
    public bool AutoLaunch { get; init; } = true;

    /// <summary>WebHost 可执行文件路径；null 时按下列顺序解析：本参数 → 环境变量 → 客户端输出目录 → 向上逐级查找。</summary>
    public string? ServerPath { get; init; }

    /// <summary>子进程工作目录；默认当前目录，用于对齐相对路径类输入（数据库由宿主按程序目录固定，不受此值影响）。</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 插件根目录；null 时取客户端程序目录下的 <c>plugins</c>。
    /// </summary>
    /// <remarks>
    /// 插件只随客户端分发，宿主进程按本参数扫描该目录，避免两处各带一份插件而状态分叉。
    /// </remarks>
    public string? PluginsDirectory { get; init; }

    /// <summary>服务端用户标识；null 沿用服务端默认（<c>LUBAN_USER_ID</c> 或本地默认用户）。</summary>
    public string? UserId { get; init; }

    /// <summary>服务端未指定令牌时使用的端口（自动拉起时始终用空闲端口，此值仅手工启动参考）。</summary>
    public int DefaultPort { get; init; } = 18080;

    /// <summary>等待服务就绪的超时时间。</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>服务端单回合同步等待上限（秒）；必须远大于最长回合，与宿主的 TimeoutSeconds 同口径。</summary>
    public int SessionTimeoutSeconds { get; init; } = 86400;

    /// <summary>是否让宿主输出框架调试日志（透传 <c>--debug</c>）。</summary>
    public bool Debug { get; init; }
}