/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： IUiDispatcher
*版本号： V1.0.0.0
*唯一标识：UI 线程调度抽象
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：UI 线程调度抽象，隔离 ViewModel 与 Terminal.Gui 主循环
*
*****************************************************************************/
namespace LubanAgent.App;

/// <summary>
/// UI 线程调度抽象。ViewModel 层从后台线程更新视图状态时必须经由本接口编组到 UI 线程，
/// 抽象化后 ViewModel 可脱离 Terminal.Gui 主循环进行单元测试。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// 将委托编组到 UI 线程异步执行。
    /// </summary>
    /// <param name="action">待执行的委托。</param>
    void Invoke(Action action);
}
