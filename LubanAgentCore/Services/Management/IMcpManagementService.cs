using LuBan.AIAgent.Tools.McpManage;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// MCP 客户端管理（内置 / 配置 / 插件注册）。
/// 方法面 = 现有 <c>/mcp</c> 命令与 Codex 设置页的能力，不新增能力。
/// </summary>
public interface IMcpManagementService
{
    /// <summary>MCP 客户端列表（含启用与连接状态）。</summary>
    Task<IReadOnlyList<McpInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>新增或更新 MCP 服务器配置。</summary>
    Task AddAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>删除 MCP 服务器配置。</summary>
    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>启用/停用 MCP 客户端（内置走禁用表，配置项走启用标记）。</summary>
    Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);

    /// <summary>连接并探测工具数量。</summary>
    Task<McpTestResult> TestAsync(string name, CancellationToken ct = default);
}