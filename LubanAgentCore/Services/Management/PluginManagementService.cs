/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： PluginManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：插件管理实现：严格转发框架 <see cref="PluginManager"/>。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：插件管理实现：严格转发框架 <see cref="PluginManager"/>。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 插件管理实现：严格转发框架 <see cref="PluginManager"/>。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PluginManager"/> 由框架注册为<b>单例</b>，本服务按用户 scope 注册，
/// 故订阅其 <c>Changed</c> 事件时必须在 scope 释放时解绑（实现 <see cref="IDisposable"/>），
/// 否则每个用户作用域都会在单例上留一份订阅。
/// </para>
/// <para>
/// 本服务不承载文案与进度：失败原因由调用方按 <see cref="GetAsync"/> 的 <c>LastError</c> 组织，
/// 与搬迁前的窗口行为一致。
/// </para>
/// </remarks>
public sealed class PluginManagementService : IPluginManagementService, IDisposable
{
    private readonly PluginManager _manager;

    private EventHandler<PluginChangedEventArgs>? _changed;
    private bool _disposed;

    /// <summary>创建插件管理服务。</summary>
    /// <param name="manager">框架插件管理器（单例）。</param>
    public PluginManagementService(PluginManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _manager.Changed += OnManagerChanged;
    }

    /// <inheritdoc />
    public event EventHandler<PluginChangedEventArgs>? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginDescriptor>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PluginDescriptor>>(_manager.GetPlugins());

    /// <inheritdoc />
    public Task<PluginDescriptor?> GetAsync(string pluginId, CancellationToken ct = default)
        => Task.FromResult(_manager.GetPlugin(pluginId));

    /// <inheritdoc />
    public Task<string?> InstallAsync(string sourcePath, CancellationToken ct = default)
        => _manager.InstallAsync(sourcePath, ct);

    /// <inheritdoc />
    public Task<bool> UpgradeAsync(string pluginId, string sourcePath, CancellationToken ct = default)
        => _manager.UpgradeAsync(pluginId, sourcePath, ct);

    /// <inheritdoc />
    public Task<bool> UninstallAsync(string pluginId, CancellationToken ct = default)
        => _manager.UninstallAsync(pluginId, ct);

    /// <inheritdoc />
    public Task<bool> SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
        => enabled
            ? _manager.EnableAsync(pluginId, ct)
            : _manager.DisableAsync(pluginId, ct);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.Changed -= OnManagerChanged;
    }

    private void OnManagerChanged(object? sender, PluginChangedEventArgs e)
        => _changed?.Invoke(this, e);
}