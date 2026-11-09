/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： AgentChatResponse
*描述：非流式对话的响应载荷与附件摘要
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>非流式对话结果。</summary>
/// <param name="SessionId">实际使用的会话标识。</param>
/// <param name="Content">正文（可能是部分内容）。</param>
/// <param name="Thinking">思考内容。</param>
/// <param name="Attachments">附件汇总。</param>
/// <param name="Error">错误文案；null 表示完整成功。</param>
public sealed record AgentChatResponse(
    string SessionId,
    string Content,
    string Thinking,
    AttachmentOverviewDto Attachments,
    string? Error);

/// <summary>附件汇总。</summary>
/// <param name="Added">已加入的附件。</param>
/// <param name="Skipped">被跳过的附件来源。</param>
public sealed record AttachmentOverviewDto(
    IReadOnlyList<AttachmentAddedDto> Added,
    IReadOnlyList<AttachmentSkippedDto> Skipped)
{
    /// <summary>空汇总。</summary>
    public static AttachmentOverviewDto Empty { get; } = new([], []);
}

/// <summary>已加入的附件。</summary>
/// <param name="Source">调用方给的原始来源。</param>
/// <param name="FileName">文件名。</param>
/// <param name="MediaType">MIME 类型。</param>
/// <param name="FileSize">字节数。</param>
public sealed record AttachmentAddedDto(string Source, string FileName, string MediaType, long FileSize);

/// <summary>被跳过的附件来源。</summary>
/// <param name="Source">调用方给的原始来源。</param>
/// <param name="Reason">跳过原因。</param>
public sealed record AttachmentSkippedDto(string Source, string Reason);