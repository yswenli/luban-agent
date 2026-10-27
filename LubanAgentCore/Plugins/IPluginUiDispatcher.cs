/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Plugins
*文件名： IPluginUiDispatcher
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：插件宿主的 UI 线程封送接缝（Core 不引用任何 UI 框架）
*
*****************************************************************************/

namespace LubanAgentCore.Plugins;

/// <summary>
/// 插件宿主的 UI 线程封送接缝。
/// </summary>
/// <remarks>
/// <para>
/// 插件宿主本身与 UI 无关，但两处必须落在 UI 线程：
/// 侧栏贡献变化通知（可异步投递）与面板注销前的同步解绑（必须同步，否则 UI 树残留引用会钉住
/// 插件程序集，令 ALC 卸载降级为 <c>RequiresRestart</c>）。
/// </para>
/// <para>
/// 宿主程序（如 LubanAgentCodex）注册平台实现（<c>Dispatcher.UIThread</c>）；
/// 无 UI 的宿主使用默认 <see cref="ImmediatePluginUiDispatcher"/>（就地执行）。
/// </para>
/// </remarks>
public interface IPluginUiDispatcher
{
    /// <summary>当前是否位于 UI 线程。</summary>
    bool IsUiThread { get; }

    /// <summary>把回调投递到 UI 线程（不等待执行）。</summary>
    /// <param name="action">回调。</param>
    void Post(Action action);

    /// <summary>在 UI 线程<b>同步</b>执行回调（已在 UI 线程时直接执行）。</summary>
    /// <param name="action">回调。</param>
    void Invoke(Action action);
}