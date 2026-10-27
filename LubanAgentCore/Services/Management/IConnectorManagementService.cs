using LuBan.AIAgent.Connectors;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 连接器管理（厂商实现的声明、启用与探测）。
/// 方法面 = 现有 Codex 设置页的能力，不新增能力。
/// </summary>
public interface IConnectorManagementService
{
    /// <summary>已声明的连接器及状态。</summary>
    Task<IReadOnlyList<ConnectorStatus>> ListAsync(CancellationToken ct = default);

    /// <summary>新增或更新连接器配置。</summary>
    Task AddAsync(ConnectorConfig config, CancellationToken ct = default);

    /// <summary>删除连接器配置。</summary>
    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>启用/停用连接器。</summary>
    Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);

    /// <summary>探测连接器暴露的 MCP 服务器与凭据是否可用。</summary>
    Task<ConnectorTestResult> TestAsync(string name, CancellationToken ct = default);
}