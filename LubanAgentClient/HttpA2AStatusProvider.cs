/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Client
*文件名： HttpA2AStatusProvider
*描述：IA2AStatusProvider 的 HTTP 实现（状态只读，启停由宿主进程负责）
*
*****************************************************************************/

namespace LubanAgent.Client;

/// <summary>
/// A2A 运行状态的 HTTP 实现。
/// </summary>
/// <remarks>
/// A2A 运行时在 WebHost 进程内随工作区就绪启动、随进程退出停止；
/// 客户端只把状态读回来展示，不发起启停。
/// </remarks>
public sealed class HttpA2AStatusProvider(AgentServerClient client) : IA2AStatusProvider
{
    /// <inheritdoc />
    public Task<A2AStatusDto> GetStatusAsync(CancellationToken ct = default)
        => client.GetAsync<A2AStatusDto>(AgentApiRoutes.A2A.Status, ct);
}