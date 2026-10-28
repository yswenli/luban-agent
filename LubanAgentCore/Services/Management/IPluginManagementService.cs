/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： IPluginManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：插件管理（安装、升级、启停、卸载）。 方法面 = 现有 Codex 插件管理窗口的能力，不新增能力。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：插件管理（安装、升级、启停、卸载）。 方法面 = 现有 Codex 插件管理窗口的能力，不新增能力。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 插件管理（安装、升级、启停、卸载）。
/// 方法面 = 现有 Codex 插件管理窗口的能力，不新增能力。
/// </summary>
/// <remarks>
/// 严格转发框架 <c>PluginManager</c> 的返回值语义：安装返回插件标识（失败为 <c>null</c>），
/// 升级 / 卸载 / 启停返回成功标志。失败原因仍由调用方按 <see cref="GetAsync"/> 的
/// <c>LastError</c> 组织文案，服务不做文案拼装。
/// </remarks>
public interface IPluginManagementService
{
    /// <summary>已安装插件（含状态与权限）。</summary>
    Task<IReadOnlyList<PluginDescriptor>> ListAsync(CancellationToken ct = default);

    /// <summary>取单个插件描述；未登记时返回 <c>null</c>（调用方据此读取 <c>LastError</c>）。</summary>
    Task<PluginDescriptor?> GetAsync(string pluginId, CancellationToken ct = default);

    /// <summary>从源路径安装插件（<paramref name="sourcePath"/> 为解包后的插件目录）。</summary>
    /// <returns>安装成功的插件标识；清单无效或校验未通过时为 <c>null</c>。</returns>
    Task<string?> InstallAsync(string sourcePath, CancellationToken ct = default);

    /// <summary>升级（热替换）插件：卸载旧版 → 校验新版 → 安装 → 恢复原启用状态。</summary>
    /// <returns>是否升级成功；卸载验证未通过时中止并返回 <c>false</c>。</returns>
    Task<bool> UpgradeAsync(string pluginId, string sourcePath, CancellationToken ct = default);

    /// <summary>卸载插件（含停用与删除插件目录）。</summary>
    /// <returns>是否卸载成功。</returns>
    Task<bool> UninstallAsync(string pluginId, CancellationToken ct = default);

    /// <summary>启用/停用插件。</summary>
    /// <returns>是否操作成功；失败原因见 <see cref="GetAsync"/> 的 <c>LastError</c>。</returns>
    Task<bool> SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default);

    /// <summary>插件状态变化（安装/卸载/启停/升级），转发自框架 <c>PluginManager.Changed</c>。</summary>
    event EventHandler<PluginChangedEventArgs>? Changed;
}