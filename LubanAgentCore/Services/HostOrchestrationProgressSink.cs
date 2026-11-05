/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services
*文件名： HostOrchestrationProgressSink
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/23
*描述：宿主编排进度出口：把框架编排进度转成 UI 无关的 OrchestrationProgressEvent 供宿主订阅。
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 宿主编排进度出口。编排由工具调用触发、执行在 <c>FunctionInvokingChatClient</c> 内部，
/// 进度不经对话流，故宿主每轮对话期间订阅 <see cref="Progress"/> 直接接收。
/// </summary>
public sealed class HostOrchestrationProgressSink : IOrchestrationProgressSink
{
    /// <summary>编排进度事件（可能来自并行节点线程）。</summary>
    public event Action<OrchestrationProgressEvent>? Progress;

    /// <inheritdoc/>
    public void Publish(OrchestrationProgress progress)
        => Dispatch(OrchestrationProgressEvent.From(progress));

    /// <summary>
    /// 由传输层补投一次进度事件。
    /// </summary>
    /// <remarks>
    /// 远程模式下编排在服务端执行，客户端本进程没有 <see cref="IOrchestrationProgressSink"/> 的生产者，
    /// 由 HTTP/SSE 客户端收到进度帧后经本方法转成本地 <see cref="Progress"/> 事件。
    /// </remarks>
    /// <param name="progress">已还原的进度事件。</param>
    public void Raise(OrchestrationProgressEvent progress) => Dispatch(progress);

    private void Dispatch(OrchestrationProgressEvent progress)
    {
        var handler = Progress;
        if (handler is null) return;

        try
        {
            handler(progress);
        }
        catch (Exception ex)
        {
            // 订阅方（UI）异常不应影响编排执行
            Logger.Warn($"编排进度事件处理失败: {ex.Message}");
        }
    }
}