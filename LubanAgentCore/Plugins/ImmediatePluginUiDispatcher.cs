/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Plugins
*文件名： ImmediatePluginUiDispatcher
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：无 UI 宿主的默认封送实现（就地执行）
*
*****************************************************************************/

namespace LubanAgentCore.Plugins;

/// <summary>
/// 无 UI 宿主的默认封送实现：所有回调就地执行。
/// </summary>
/// <remarks>
/// 供 CLI / A2A 等无 UI 宿主与单元测试使用；桌面端必须注册覆盖实现，
/// 否则面板注销会落在后台线程，插件程序集将无法被 ALC 回收。
/// </remarks>
public sealed class ImmediatePluginUiDispatcher : IPluginUiDispatcher
{
    /// <inheritdoc />
    public bool IsUiThread => true;

    /// <inheritdoc />
    public void Post(Action action) => action?.Invoke();

    /// <inheritdoc />
    public void Invoke(Action action) => action?.Invoke();
}