/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Plugins
*文件名： PluginBootstrap
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：插件启动引导：扫描插件根目录并恢复上次的启用状态
*
*****************************************************************************/
using LuBan.AIAgent.Plugins.Loading;
using LuBan.AIAgent.Plugins.Lifecycle;

namespace LubanAgentCodex.Plugins;

/// <summary>
/// 插件启动引导。
/// </summary>
/// <remarks>
/// 框架 <c>PluginManager.ScanAsync</c> / <c>RestoreEnabledAsync</c> 不会自动触发，
/// 由宿主在启动时（工作区就绪之后，便于插件技能直接落到当前工作区）显式调用。
/// 插件根目录固定为 <c>&lt;AppContext.BaseDirectory&gt;/plugins</c>（应用级，PRD §3.3）。
/// </remarks>
internal static class PluginBootstrap
{
    /// <summary>
    /// 扫描插件并恢复启用状态。
    /// </summary>
    /// <param name="services">宿主服务提供者。</param>
    /// <param name="status">状态回调（用于闪屏/日志展示）。</param>
    public static async Task InitializeAsync(IServiceProvider services, Action<string>? status = null)
    {
        var manager = services.GetService<PluginManager>();
        if (manager is null)
        {
            Logger.Warn("插件体系未注册：跳过插件扫描。");
            return;
        }

        try
        {
            status?.Invoke("正在扫描插件…");
            // 刻意不使用 ConfigureAwait(false)：status 回调会直接触碰 UI（闪屏），
            // 必须留在调用方的同步上下文（Avalonia UI 线程）上执行。
            var scanned = await manager.ScanAsync();
            Logger.Info($"插件扫描完成：共登记 {scanned} 个插件（根目录 {PluginLocation.BuiltinRoot}）。");
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