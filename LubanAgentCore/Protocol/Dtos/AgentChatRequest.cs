/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： AgentChatRequest
*描述：一轮对话的传输入参
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>
/// 一轮对话的传输入参。
/// </summary>
public sealed record AgentChatRequest
{
    /// <summary>用户本轮输入；空白按 400 处理。</summary>
    public string Prompt { get; init; } = "";

    /// <summary>本轮人设/规则覆盖；null 或空白沿用服务端默认 Profile 提示词。</summary>
    public string? System { get; init; }

    /// <summary>附件来源（URL / 绝对路径 / 相对路径），由服务端解析。</summary>
    public IReadOnlyList<string>? Attachments { get; init; }

    /// <summary>目标会话；null 表示复用该用户最后一次会话，没有则新建。</summary>
    public string? SessionId { get; init; }

    /// <summary>本轮是否装配工具。</summary>
    public bool EnableTools { get; init; } = true;

    /// <summary>推理强度覆盖（None / Low / Medium / High）；null 沿用模型默认。</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>
    /// 本轮权限模式（default / plan / accept-edits / bypass）；null 沿用服务端默认。
    /// 客户端切换模式后必须随每轮下发，否则服务端一律按 Default 处理。
    /// </summary>
    public string? PermissionMode { get; init; }
}