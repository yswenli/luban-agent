/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services
*文件名： HostOrchestrationProgressSink
*版本号： V1.0.0.0
*唯一标识：HostOrchestrationProgressSink
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/23
*描述：宿主编排进度出口：把框架编排进度转成 UI 无关的 OrchestrationProgressEvent 供宿主订阅。
*
*****************************************************************************/
using LuBan.AIAgent.Orchestration;

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
    {
        var handler = Progress;
        if (handler is null) return;

        try
        {
            handler(OrchestrationProgressEvent.From(progress));
        }
        catch (Exception ex)
        {
            // 订阅方（UI）异常不应影响编排执行
            Logger.Warn($"编排进度事件处理失败: {ex.Message}");
        }
    }
}