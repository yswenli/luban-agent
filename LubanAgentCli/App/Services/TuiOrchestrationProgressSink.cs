/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCli.App.Services
*文件名： TuiOrchestrationProgressSink
*版本号： V1.0.0.0
*唯一标识：TUI 编排进度出口
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/23
*描述：TUI 的编排进度出口实现。编排由工具调用触发，执行发生在
*      FunctionInvokingChatClient 内部，外层流拿不到编排 update；
*      本类由 DI 单例持有，ConversationViewModel 每轮订阅/退订，
*      把进度事件转成文档追加，保证规划/节点执行有实时反馈。
*
*****************************************************************************/
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;

namespace LubanAgentCli.App.Services;

/// <summary>
/// TUI 编排进度出口。仅暴露事件，由 <c>ConversationViewModel</c> 在每轮对话期间订阅，
/// UI 线程编组由订阅方负责（事件可能由并行节点线程触发）。
/// </summary>
internal sealed class TuiOrchestrationProgressSink : IOrchestrationProgressSink
{
    /// <summary>编排进度事件（可能来自并行节点线程），与 TUI 既有消费类型一致。</summary>
    public event Action<OrchestrationProgressContent>? Progress;

    /// <inheritdoc/>
    public void Publish(OrchestrationProgress progress)
    {
        var handler = Progress;
        if (handler is null) return;

        try
        {
            handler(OrchestrationProgressContent.From(progress));
        }
        catch
        {
            // UI 侧异常不得影响编排执行
        }
    }
}