/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Executor
*文件名： CompositeSubAgentExecutor
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：组合子代理执行器，按角色在本机进程内执行器与远端执行器之间分发
*
*****************************************************************************/
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;

namespace LubanAgentA2A.Executor;

/// <summary>
/// 组合子代理执行器。持有本机进程内执行器与「角色名 → 远端执行器」映射，
/// 按 <see cref="SubAgentSpec.Role"/> 分发：命中远端 client 角色走远端执行器，
/// 否则走本机进程内执行器（Rev.7 M6）。
/// <para>远端映射由运行时状态提供（实时只读视图），以便工作区确定后再注册远端角色，
/// 无需在 DI 期固定。</para>
/// </summary>
public sealed class CompositeSubAgentExecutor : ISubAgentExecutor
{
    private readonly ISubAgentExecutor _inProcess;
    private readonly IReadOnlyDictionary<string, ISubAgentExecutor> _remoteByRole;

    /// <summary>
    /// 创建组合执行器实例。
    /// </summary>
    /// <param name="inProcess">本机进程内执行器。</param>
    /// <param name="remoteByRole">远端执行器映射（键为角色名，即客户端 <c>name</c>）。</param>
    public CompositeSubAgentExecutor(
        ISubAgentExecutor inProcess,
        IReadOnlyDictionary<string, ISubAgentExecutor> remoteByRole)
    {
        _inProcess = inProcess ?? throw new ArgumentNullException(nameof(inProcess));
        _remoteByRole = remoteByRole ?? throw new ArgumentNullException(nameof(remoteByRole));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SubAgentExecutionEvent> ExecuteAsync(
        SubAgentSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!string.IsNullOrEmpty(spec.Role) && _remoteByRole.TryGetValue(spec.Role!, out var remote))
        {
            return remote.ExecuteAsync(spec, ct);
        }

        return _inProcess.ExecuteAsync(spec, ct);
    }
}