/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentA2A.Runtime
*文件名： A2AStatusService
*描述：A2A 运行状态的本机实现（直接读运行时对象）
*
*****************************************************************************/

using LubanAgent.Protocol;
using LubanAgent.Protocol.Dtos;

namespace LubanAgentA2A.Runtime;

/// <summary>
/// A2A 运行状态的本机实现：直接读取生命周期 / 运行时状态 / 服务端宿主。
/// </summary>
public sealed class A2AStatusService : IA2AStatusProvider
{
    private readonly IA2ALifecycle _lifecycle;
    private readonly A2ARuntimeState _state;
    private readonly IA2AServerHost _serverHost;
    private readonly IOptions<A2AOptions> _options;

    /// <summary>创建状态实现。</summary>
    /// <param name="lifecycle">A2A 运行时生命周期。</param>
    /// <param name="state">A2A 运行时状态。</param>
    /// <param name="serverHost">A2A 服务端宿主。</param>
    /// <param name="options">A2A 配置（提供总开关）。</param>
    public A2AStatusService(
        IA2ALifecycle lifecycle,
        A2ARuntimeState state,
        IA2AServerHost serverHost,
        IOptions<A2AOptions> options)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task<A2AStatusDto> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult(new A2AStatusDto(
            _options.Value.Enabled,
            _lifecycle.IsRunning,
            _state.LocalAgentName,
            _serverHost.Endpoint,
            _state.WorkspaceRoot));
}