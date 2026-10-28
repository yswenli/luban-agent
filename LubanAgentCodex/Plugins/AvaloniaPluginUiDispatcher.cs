/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Plugins
*文件名： AvaloniaPluginUiDispatcher
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：插件宿主的 Avalonia 线程封送实现（Dispatcher.UIThread）
*
*****************************************************************************/

namespace LubanAgentCodex.Plugins;

/// <summary>
/// 插件宿主的 Avalonia 线程封送实现。
/// </summary>
/// <remarks>
/// <see cref="Invoke"/> 必须同步执行：面板注销由框架卸载流程在后台线程调用，
/// 返回后框架立即进入 ALC 卸载，若 UI 树仍持有插件程序集定义的面板控件，
/// 弱引用验证会失败并降级为「需重启」。
/// </remarks>
public sealed class AvaloniaPluginUiDispatcher : IPluginUiDispatcher
{
    /// <inheritdoc />
    public bool IsUiThread => Dispatcher.UIThread.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action)
    {
        if (action is null) return;
        Dispatcher.UIThread.Post(action);
    }

    /// <inheritdoc />
    public void Invoke(Action action)
    {
        if (action is null) return;

        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Invoke(action);
    }
}