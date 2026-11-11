/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： AgentRunRequests
*描述：进行中回合的探活与接续入参
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>探活：该会话是否有正在进行的回合。</summary>
public sealed record AgentActiveRequest
{
    /// <summary>所属会话。</summary>
    public string SessionId { get; init; } = "";
}

/// <summary>接上正在进行的回合。</summary>
public sealed record AgentAttachRequest
{
    /// <summary>所属会话。</summary>
    public string SessionId { get; init; } = "";

    /// <summary>期望接上的轮次标识；非空且与服务端当前轮次不符时不续流（回 204）。</summary>
    public string? RunId { get; init; }

    /// <summary>已收到的最后帧序号；缺省表示前端内存为空，按快照重建。</summary>
    public int? FromSeq { get; init; }
}