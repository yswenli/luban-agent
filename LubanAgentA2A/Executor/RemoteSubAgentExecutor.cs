/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Executor
*文件名： RemoteSubAgentExecutor
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：远程编排子代理执行器，经 IA2AClient 调用远端 Agent 并映射事件
*
*****************************************************************************/
using System.Runtime.CompilerServices;

using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;

namespace LubanAgentA2A.Executor;

/// <summary>
/// 远程子代理执行器（client 角色）。把 <see cref="SubAgentSpec"/> 编为 A2A 消息，
/// 经 <see cref="IA2AClient"/> 调用远端 Agent，再把远端 A2A 事件流反译为
/// <see cref="SubAgentExecutionEvent"/> 流。
/// <para>远端自行执行，本机 **不设置** 工具确认作用域（Rev.4 A3）。</para>
/// </summary>
public sealed class RemoteSubAgentExecutor : ISubAgentExecutor
{
    private readonly IA2AClient _client;

    /// <summary>
    /// 创建远程子代理执行器实例。
    /// </summary>
    /// <param name="client">远端 A2A 客户端。</param>
    public RemoteSubAgentExecutor(IA2AClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SubAgentExecutionEvent> ExecuteAsync(
        SubAgentSpec spec, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var message = SubAgentA2ABridge.BuildMessage(spec);

        await foreach (var evt in SubAgentA2ABridge
            .DecodeAsync(_client.SendStreamingAsync(message, ct), spec.SessionId, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}