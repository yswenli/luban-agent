using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Lifecycle;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 插件管理（安装、启停、权限快照）。
/// 方法面 = 现有 Codex 插件管理窗口的能力，不新增能力。
/// </summary>
public interface IPluginManagementService
{
    /// <summary>已安装插件（含状态与权限）。</summary>
    Task<IReadOnlyList<PluginDescriptor>> ListAsync(CancellationToken ct = default);

    /// <summary>从源路径安装插件（<paramref name="sourcePath"/> 为解包后的插件目录）。</summary>
    Task InstallAsync(string sourcePath, CancellationToken ct = default);

    /// <summary>卸载插件。</summary>
    Task UninstallAsync(string pluginId, CancellationToken ct = default);

    /// <summary>启用/停用插件。</summary>
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default);

    /// <summary>插件声明的权限快照；插件不存在时返回 null。</summary>
    Task<PluginPermissions?> GetPermissionsAsync(string pluginId, CancellationToken ct = default);

    /// <summary>插件状态变化（安装/卸载/启停/升级）。</summary>
    event EventHandler<PluginChangedEventArgs>? Changed;
}