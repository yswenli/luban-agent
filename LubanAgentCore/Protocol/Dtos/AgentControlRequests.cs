/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： AgentControlRequests
*描述：工具确认 / 取消回合 / 重置会话运行时的传输入参
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>回传工具确认结果。</summary>
public sealed record AgentConfirmRequest
{
    /// <summary>所属会话。</summary>
    public string SessionId { get; init; } = "";

    /// <summary>确认标识（取自 <c>tool.confirm</c> 帧）。</summary>
    public string ConfirmationId { get; init; } = "";

    /// <summary>用户选择：allow / deny / allowAll / allowSession。</summary>
    public string Result { get; init; } = "";
}

/// <summary>取消指定会话正在进行的回合。</summary>
public sealed record AgentCancelRequest
{
    /// <summary>所属会话。</summary>
    public string SessionId { get; init; } = "";

    /// <summary>
    /// 本轮标识（取自 <c>session</c> 帧）；为空表示取消该会话当前登记的回合。
    /// 非空时服务端只在登记仍是本轮时才取消，避免迟到请求误伤新回合。
    /// </summary>
    public string? RunId { get; init; }
}

/// <summary>丢弃服务端缓存的会话运行时（配置变更后重建）。</summary>
public sealed record AgentResetRequest
{
    /// <summary>目标会话；null 表示当前会话。</summary>
    public string? SessionId { get; init; }
}