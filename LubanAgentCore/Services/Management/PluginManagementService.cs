using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Lifecycle;

namespace LubanAgentCore.Services.Management;

/// <summary>插件管理实现（安装、启停、权限快照）。</summary>
public sealed class PluginManagementService : IPluginManagementService
{
    private const string Source = "Codex PluginManageWindow / PluginHostContext";

    private EventHandler<PluginChangedEventArgs>? _changed;

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginDescriptor>> ListAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 18", Source);

    /// <inheritdoc />
    public Task InstallAsync(string sourcePath, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 18", Source);

    /// <inheritdoc />
    public Task UninstallAsync(string pluginId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 18", Source);

    /// <inheritdoc />
    public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 18", Source);

    /// <inheritdoc />
    public Task<PluginPermissions?> GetPermissionsAsync(string pluginId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 18", Source);

    /// <inheritdoc />
    public event EventHandler<PluginChangedEventArgs>? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }
}