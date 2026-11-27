/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Infrastructure
*文件名： AgentHome
*描述：用户级数据根目录（~/.luban-agent）与各外部文件落点解析
*
*****************************************************************************/

namespace LubanAgentCore.Infrastructure;

/// <summary>
/// 用户级数据根目录解析：所有跨工作区、跨进程共享的外部文件（数据库、配置、
/// 模型、插件状态、云效任务态、宿主发现文件与证书）统一落在
/// <c>~/.luban-agent</c> 下（Windows 为 <c>%USERPROFILE%\.luban-agent</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 根目录复用框架的 <see cref="GlobalLubanAgentPath.Root"/>（用户主目录 + <c>.luban-agent</c>），
/// 因此 Windows / Linux / macOS 一致，且与框架已使用的 <c>~/.luban-agent/skills</c> 等目录同根。
/// </para>
/// <para>
/// 环境变量 <c>LUBAN_AGENT_HOME</c> 可覆盖根目录，供测试与便携部署隔离真实用户数据。
/// 该覆盖只影响产品侧文件；框架内部（skills/rules/mcps）仍按主目录解析。
/// </para>
/// </remarks>
public static class AgentHome
{
    /// <summary>可用环境变量覆盖根目录的名称。</summary>
    public const string HomeEnvironmentVariable = "LUBAN_AGENT_HOME";

    /// <summary>
    /// 用户级数据根目录：<c>~/.luban-agent</c>。
    /// </summary>
    public static string Root { get; } = ResolveRoot();

    /// <summary>
    /// 历史单用户配置文件：<c>~/.luban-agent/config.json</c>。
    /// </summary>
    public static string ConfigFile => Path.Combine(Root, "config.json");

    /// <summary>
    /// 按用户隔离的配置目录：<c>~/.luban-agent/users/&lt;userId&gt;</c>。
    /// </summary>
    public static string UsersDirectory => Path.Combine(Root, "users");

    /// <summary>
    /// 嵌入模型缓存根：<c>~/.luban-agent/models</c>。
    /// </summary>
    public static string ModelsDirectory => Path.Combine(Root, "models");

    /// <summary>
    /// 主 SQLite 数据库：<c>~/.luban-agent/luban-ai-agent.db</c>。
    /// </summary>
    public static string DatabasePath => Path.Combine(Root, "luban-ai-agent.db");

    /// <summary>
    /// 本地记忆库：<c>~/.luban-agent/localmemory.db</c>。
    /// </summary>
    public static string LocalMemoryDatabasePath => Path.Combine(Root, "localmemory.db");

    /// <summary>
    /// 插件状态目录（桌面端）：<c>~/.luban-agent/plugins</c>。
    /// </summary>
    public static string PluginsDirectory => Path.Combine(Root, "plugins");

    /// <summary>
    /// 插件状态文件（桌面端）：<c>~/.luban-agent/plugins/plugin-states.json</c>。
    /// </summary>
    public static string PluginsStateFile => Path.Combine(PluginsDirectory, "plugin-states.json");

    /// <summary>
    /// 新建项目插件状态目录：<c>~/.luban-agent/plugins/state</c>。
    /// </summary>
    public static string PluginsStateDirectory => Path.Combine(PluginsDirectory, "state");

    /// <summary>
    /// Web 宿主专属目录：<c>~/.luban-agent/webhost</c>。
    /// </summary>
    public static string WebHostDirectory => Path.Combine(Root, "webhost");

    /// <summary>
    /// Web 插件根：<c>~/.luban-agent/webhost/plugins</c>。
    /// </summary>
    public static string WebPluginsDirectory => Path.Combine(WebHostDirectory, "plugins");

    /// <summary>
    /// Web 插件状态文件：<c>~/.luban-agent/webhost/plugin-states.json</c>。
    /// </summary>
    public static string WebPluginStateFile => Path.Combine(WebHostDirectory, "plugin-states.json");

    /// <summary>
    /// 云效 PRD 回执账本目录：<c>~/.luban-agent/webhost/yunxiao-prd</c>。
    /// </summary>
    public static string PrdReceiptDirectory => Path.Combine(WebHostDirectory, "yunxiao-prd");

    /// <summary>
    /// Web 宿主 JWT 签名密钥：<c>~/.luban-agent/webhost/jwt.key</c>。
    /// </summary>
    public static string WebHostJwtKeyFile => Path.Combine(WebHostDirectory, "jwt.key");

    /// <summary>
    /// Web 端从 git 克隆的项目根目录：<c>~/.luban-agent/repositories</c>。
    /// </summary>
    public static string RepositoriesDirectory => Path.Combine(Root, "repositories");

    /// <summary>
    /// 宿主默认工作区根目录：<c>~/.luban-agent/workspaces</c>（Web 首启自动建在 <c>default</c> 子目录）。
    /// </summary>
    public static string WorkspacesDirectory => Path.Combine(Root, "workspaces");

    /// <summary>
    /// 宿主发现文件：<c>~/.luban-agent/webhost.json</c>。
    /// </summary>
    public static string WebHostInfoFile => Path.Combine(Root, "webhost.json");

    /// <summary>
    /// Web 宿主 TLS 证书：<c>~/.luban-agent/webhost-cert.pfx</c>。
    /// </summary>
    public static string WebHostCertificateFile => Path.Combine(Root, "webhost-cert.pfx");

    /// <summary>
    /// 确保根目录存在（SQLite 不会自动创建父目录）。
    /// </summary>
    public static void EnsureRoot()
        => Directory.CreateDirectory(Root);

    /// <summary>
    /// 把 SQLite 连接串中相对的 <c>Data Source</c> 改写到 <see cref="Root"/> 下，
    /// 同名文件保留文件名。已是绝对路径或空连接串时原样返回 <c>false</c>。
    /// </summary>
    /// <param name="connectionString">原始连接串。</param>
    /// <param name="resolved">改写后的连接串；不需要改写时为原值。</param>
    /// <returns>是否发生了改写。</returns>
    public static bool TryResolveSqliteDataSource(string? connectionString, out string? resolved)
    {
        resolved = connectionString;
        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        var parts = connectionString.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var changed = false;
        for (var i = 0; i < parts.Length; i++)
        {
            var kv = parts[i].Split(['='], 2);
            if (kv.Length != 2) continue;

            var key = kv[0].Trim();
            var value = kv[1].Trim();
            if (!key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("DataSource", StringComparison.OrdinalIgnoreCase)) continue;

            if (string.IsNullOrEmpty(value) || Path.IsPathRooted(value) || value.Contains(':')) continue;

            parts[i] = $"{key}={Path.Combine(Root, Path.GetFileName(value))}";
            changed = true;
        }

        if (changed) resolved = string.Join(";", parts);
        return changed;
    }

    private static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured.Trim()));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Logger.Warn($"环境变量 {HomeEnvironmentVariable} 不是有效路径，回退到用户主目录：{ex.Message}");
            }
        }

        return GlobalLubanAgentPath.Root;
    }
}