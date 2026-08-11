/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： TerminalGuiDispatcher
*版本号： V1.0.0.0
*唯一标识：UI 线程调度实现
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：基于 IApplication 的 UI 线程调度实现
*
*****************************************************************************/
using Terminal.Gui.App;

namespace LubanAgent.App;

/// <summary>
/// 基于 <see cref="IApplication"/> 的 UI 线程调度实现。
/// </summary>
/// <param name="application">Terminal.Gui 应用实例。</param>
internal sealed class TerminalGuiDispatcher(IApplication application) : IUiDispatcher
{
    private readonly IApplication _application = application ?? throw new ArgumentNullException(nameof(application));

    /// <summary>
    /// 将委托编组到 UI 线程异步执行。主循环已退出时静默丢弃，避免关闭期回调抛异常。
    /// </summary>
    /// <param name="action">待执行的委托。</param>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            _application.Invoke(action);
        }
        catch (ObjectDisposedException)
        {
            // 主循环已关闭，丢弃这次更新
        }
        catch (InvalidOperationException)
        {
            // 应用未初始化或已结束，丢弃这次更新
        }
    }
}
