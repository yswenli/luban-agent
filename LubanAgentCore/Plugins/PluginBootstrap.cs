/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Plugins
*文件名： PluginBootstrap
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：插件启动引导：扫描插件根目录并恢复上次的启用状态
*
*****************************************************************************/

namespace LubanAgentCore.Plugins;

/// <summary>
/// 插件启动引导。
/// </summary>
/// <remarks>
/// 框架 <c>PluginManager.ScanAsync</c> / <c>RestoreEnabledAsync</c> 不会自动触发，
/// 由宿主在启动时（工作区就绪之后，便于插件技能直接落到当前工作区）显式调用。
/// 插件根目录默认为 <c>&lt;AppContext.BaseDirectory&gt;/plugins</c>，可由调用方指定
/// （WebHost 经 <c>--plugins-dir</c> 指向客户端的插件目录）。
/// </remarks>
public static class PluginBootstrap
{
    /// <summary>
    /// 扫描插件并恢复启用状态。
    /// </summary>
    /// <param name="services">宿主服务提供者。</param>
    /// <param name="status">状态回调（用于闪屏/日志展示）。</param>
    /// <param name="pluginsRoot">
    /// 插件根目录；为空时按框架默认（<c>&lt;AppContext.BaseDirectory&gt;/plugins</c>）。
    /// 插件只随客户端分发，宿主进程由 <c>--plugins-dir</c> 指过来。
    /// </param>
    public static async Task InitializeAsync(
        DiContainer services, Action<string>? status = null, string? pluginsRoot = null)
    {
        var manager = services.GetService<PluginManager>();
        if (manager is null)
        {
            Logger.Warn("插件体系未注册：跳过插件扫描。");
            return;
        }

        // 路径归一化也可能失败（非法字符/超长）：与扫描失败同口径，只记日志、不阻断宿主启动
        string root;
        try
        {
            root = string.IsNullOrWhiteSpace(pluginsRoot)
                ? PluginLocation.BuiltinRoot
                : Path.GetFullPath(pluginsRoot.Trim());
        }
        catch (Exception ex)
        {
            Logger.Error($"插件根目录无效（{pluginsRoot}），跳过插件扫描", ex);
            return;
        }

        try
        {
            status?.Invoke("正在扫描插件…");
            // 刻意不使用 ConfigureAwait(false)：status 回调会直接触碰 UI（闪屏），
            // 必须留在调用方的同步上下文（宿主 UI 线程）上执行。
            var scanned = await manager.ScanAsync(string.IsNullOrWhiteSpace(pluginsRoot) ? null : new[] { root });
            Logger.Info($"插件扫描完成：共登记 {scanned} 个插件（根目录 {root}）。");
        }
        catch (Exception ex)
        {
            Logger.Error("扫描插件失败", ex);
            return;
        }

        try
        {
            status?.Invoke("正在恢复插件状态…");
            var restored = await manager.RestoreEnabledAsync();
            if (restored > 0)
                Logger.Info($"已恢复 {restored} 个插件的启用状态。");
        }
        catch (Exception ex)
        {
            Logger.Error("恢复插件启用状态失败", ex);
        }
    }
}