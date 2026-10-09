/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Executor
*文件名： InProcessSubAgentExecutor
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：本机编排子代理执行器，经进程内 A2A 传输执行，A3 唯一开启点
*
*****************************************************************************/
using System.Runtime.CompilerServices;

using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;

namespace LubanAgentA2A.Executor;

/// <summary>
/// 本机编排子代理执行器。把 <see cref="SubAgentSpec"/> 编为 A2A 消息，经
/// <see cref="IA2ATransport"/> 进程内直连执行，再把 A2A 事件流反译为
/// <see cref="SubAgentExecutionEvent"/> 流。
/// <para>A3 唯一开启点：以 <c>EnterSubAgentScope(allowAll: true)</c> 包裹执行，
/// 使内部子代理「全放行但守硬约束」（删除类/工作区外仍硬拒）。</para>
/// <para>目标 Agent 名由提供者在每次执行时求值，以便工作区确定后再定名。</para>
/// </summary>
public sealed class InProcessSubAgentExecutor : ISubAgentExecutor
{
    private readonly IA2ATransport _transport;
    private readonly Func<string> _agentNameProvider;

    /// <summary>
    /// 创建进程内子代理执行器实例。
    /// </summary>
    /// <param name="transport">进程内 A2A 传输。</param>
    /// <param name="agentNameProvider">目标 Agent 名称提供者（取服务端 <c>AgentName</c>）。</param>
    public InProcessSubAgentExecutor(IA2ATransport transport, Func<string> agentNameProvider)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _agentNameProvider = agentNameProvider ?? throw new ArgumentNullException(nameof(agentNameProvider));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SubAgentExecutionEvent> ExecuteAsync(
        SubAgentSpec spec, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var message = SubAgentA2ABridge.BuildMessage(spec);

        using (ToolConfirmationContext.EnterSubAgentScope(allowAll: true))
        {
            await foreach (var evt in SubAgentA2ABridge
                .DecodeAsync(_transport.SendStreamingAsync(_agentNameProvider(), message, ct), spec.SessionId, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }
}