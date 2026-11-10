/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： SessionContracts
*描述：会话接口的浏览器端响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

/// <summary>会话摘要（含工作区与模型）。</summary>
/// <param name="SessionId">会话标识。</param>
/// <param name="Title">标题。</param>
/// <param name="CreatedAt">创建时间。</param>
/// <param name="UpdatedAt">更新时间；未更新过为 null。</param>
/// <param name="MessageCount">消息数。</param>
/// <param name="TotalTokens">累计 Token。</param>
/// <param name="WorkspaceId">所属工作区。</param>
/// <param name="Provider">模型供应商。</param>
/// <param name="ModelName">模型名。</param>
public sealed record SessionSummaryDto(
    string SessionId,
    string? Title,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int MessageCount,
    int TotalTokens,
    string? WorkspaceId,
    string? Provider,
    string? ModelName);