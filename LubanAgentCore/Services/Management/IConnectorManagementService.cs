/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： IConnectorManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：连接器管理（厂商实现的声明、启用与探测）。 方法面 = 现有 Codex 设置页的能力，不新增能力。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：连接器管理（厂商实现的声明、启用与探测）。 方法面 = 现有 Codex 设置页的能力，不新增能力。
*
*****************************************************************************/

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