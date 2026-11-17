/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： PluginController
*描述：插件管理接口（浏览器端，只管理 Web 插件根）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 插件管理接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebPluginService"/>。
/// 与桌面端隔离：只列出并管理 Web 插件根下的插件，桌面端插件目录下的插件不可见也不可操作。
/// </remarks>
public sealed class PluginController : Controller
{
    /// <summary>列出 Web 插件根下的插件。</summary>
    /// <returns>插件根与插件项（含页面入口与访问地址）。</returns>
    [HttpGet]
    public Task<ActionResult> List() => WebPluginService.ListAsync();

    /// <summary>安装插件（成功后自动启用）。</summary>
    /// <param name="payload">包装后的 <see cref="PluginInstallRequest"/> JSON。</param>
    /// <returns>插件标识、状态、警告与最新插件项。</returns>
    [HttpPost]
    public Task<ActionResult> Install(string payload) => WebPluginService.InstallAsync(payload);

    /// <summary>升级插件。</summary>
    /// <param name="payload">包装后的 <see cref="PluginUpgradeRequest"/> JSON。</param>
    /// <returns>插件标识、状态、警告与最新插件项。</returns>
    [HttpPost]
    public Task<ActionResult> Upgrade(string payload) => WebPluginService.UpgradeAsync(payload);

    /// <summary>启用插件。</summary>
    /// <param name="payload">包装后的 <see cref="PluginIdRequest"/> JSON。</param>
    /// <returns>操作后的插件项。</returns>
    [HttpPost]
    public Task<ActionResult> Enable(string payload) => WebPluginService.SetEnabledAsync(payload, enabled: true);

    /// <summary>停用插件。</summary>
    /// <param name="payload">包装后的 <see cref="PluginIdRequest"/> JSON。</param>
    /// <returns>操作后的插件项。</returns>
    [HttpPost]
    public Task<ActionResult> Disable(string payload) => WebPluginService.SetEnabledAsync(payload, enabled: false);

    /// <summary>卸载插件。</summary>
    /// <param name="payload">包装后的 <see cref="PluginIdRequest"/> JSON。</param>
    /// <returns>卸载结果。</returns>
    [HttpPost]
    public Task<ActionResult> Uninstall(string payload) => WebPluginService.UninstallAsync(payload);
}