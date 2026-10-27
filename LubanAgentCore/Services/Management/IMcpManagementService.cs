using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.MCP;
using LuBan.AIAgent.Tools.McpManage;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// MCP 客户端管理（内置 / 配置 / 插件注册）。
/// 方法面 = 现有 <c>/mcp</c> 命令与 Codex 设置页的能力，不新增能力。
/// </summary>
/// <remarks>
/// 三个来源对应三个写回后端：
/// <list type="bullet">
/// <item><see cref="ManagedOrigin.Builtin"/>：硬编码，只可启停（写 <c>DisabledBuiltinMcpClients</c>）。</item>
/// <item><see cref="ManagedOrigin.Config"/>：config.json 的 <c>McpServers</c>，可增删改启停。</item>
/// <item><see cref="ManagedOrigin.File"/>：<c>mcps/&lt;name&gt;.json</c> 文件（全局 <c>~/.luban-agent/mcps</c> 或工作区 <c>.luban-agent/mcps</c>）。</item>
/// </list>
/// 文件后端方法为同步方法：调用方（Codex 设置页）本就跑在 UI 线程的同步文件 IO 上，返回 Task 只会引出
/// <c>GetAwaiter().GetResult()</c> 或死锁风险，故如实声明为同步。
/// </remarks>
public interface IMcpManagementService
{
    /// <summary>三个来源合并后的 MCP 客户端列表（含「已禁用的内置 / 配置」条目）。</summary>
    Task<IReadOnlyList<McpInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>新增 config.json 外部 MCP 服务器；重名或名称为空时抛异常（消息面向用户）。</summary>
    Task AddAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>更新 config.json 外部 MCP 服务器的可编辑字段。</summary>
    Task UpdateAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>按来源删除（内置不可删；<paramref name="workspaceRoot"/> 为 null 表示全局目录）。</summary>
    Task DeleteAsync(string name, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default);

    /// <summary>启用/停用（内置走禁用表，配置项走启用标记；文件型不支持）。</summary>
    Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);

    /// <summary>连接指定客户端；未找到时抛异常（消息面向用户）。</summary>
    Task<bool> ConnectAsync(string name, CancellationToken ct = default);

    /// <summary>列出指定客户端可用工具；未找到 / 未连接时抛异常（消息面向用户）。</summary>
    Task<IReadOnlyList<MCPTool>> ListToolsAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// 连接并探测工具数量（不持久化、不影响注册表中的客户端）。
    /// 未找到配置时若为注册表中的进程内客户端，只回报其当前连接状态。
    /// </summary>
    Task<McpTestResult> TestAsync(string name, CancellationToken ct = default);

    /// <summary>列出文件型 MCP 配置名（文件名去 .json），按名称排序。</summary>
    IReadOnlyList<string> ListFileIds(string? workspaceRoot);

    /// <summary>读取文件型 MCP 配置；文件不存在时返回 null（JSON 非法时抛异常，与旧实现一致）。</summary>
    McpServerConfig? ReadFile(string id, string? workspaceRoot);

    /// <summary>保存文件型 MCP 配置；<paramref name="originalId"/> 与名称不同时视为改名（删除旧文件）。</summary>
    void SaveFile(string? originalId, McpServerConfig config, string? workspaceRoot);
}