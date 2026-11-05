/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Protocol.Dtos
*文件名： A2ADtos
*描述：A2A 协作状态接口的传输模型
*
*****************************************************************************/

namespace LubanAgent.Protocol.Dtos;

/// <summary>A2A 运行状态（只读快照）。</summary>
/// <param name="Enabled">配置总开关（<c>LuBanAgent:Agent2Agent:Enabled</c>）。</param>
/// <param name="IsRunning">运行时是否已启动。</param>
/// <param name="LocalAgentName">本地 Agent 名称。</param>
/// <param name="Endpoint">服务端对外端点；未启动为 null。</param>
/// <param name="WorkspaceRoot">启动时使用的工作区根；未启动为 null。</param>
/// <remarks>
/// A2A 运行时跟随承载回合的宿主进程（WebHost）启动与退出，客户端只读取状态、不提供启停。
/// </remarks>
public sealed record A2AStatusDto(
    bool Enabled,
    bool IsRunning,
    string LocalAgentName,
    string? Endpoint,
    string? WorkspaceRoot);