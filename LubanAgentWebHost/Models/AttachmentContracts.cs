/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： AttachmentContracts
*描述：浏览器端附件上传的请求 / 响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

/// <summary>附件上传请求。</summary>
public sealed record AttachmentUploadRequest
{
    /// <summary>目标会话；为空时落到当前工作区。</summary>
    public string? SessionId { get; init; }

    /// <summary>待上传文件。</summary>
    public IReadOnlyList<AttachmentUploadFile>? Files { get; init; }
}

/// <summary>单个待上传文件。</summary>
public sealed record AttachmentUploadFile
{
    /// <summary>原始文件名（只取末段，不接受路径）。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>文件内容（Base64）。</summary>
    public string ContentBase64 { get; init; } = string.Empty;
}

/// <summary>附件上传结果。</summary>
/// <param name="Accepted">已落盘文件。</param>
/// <param name="Rejected">被拒文件与原因。</param>
public sealed record AttachmentUploadResult(
    IReadOnlyList<AcceptedAttachmentDto> Accepted,
    IReadOnlyList<RejectedAttachmentDto> Rejected);

/// <summary>已落盘附件。</summary>
/// <param name="Path">绝对路径（发送时放进 <c>AgentChatRequest.Attachments</c>）。</param>
/// <param name="FileName">落盘文件名。</param>
/// <param name="MediaType">媒体类型。</param>
/// <param name="Size">字节数。</param>
public sealed record AcceptedAttachmentDto(string Path, string FileName, string MediaType, long Size);

/// <summary>被拒附件。</summary>
/// <param name="FileName">原始文件名。</param>
/// <param name="Reason">中文原因。</param>
public sealed record RejectedAttachmentDto(string FileName, string Reason);