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

/// <summary>设置会话单独使用模型。</summary>
/// <param name="SessionId">会话标识。</param>
/// <param name="ProviderId">供应商标识。</param>
/// <param name="ModelId">模型标识。</param>
public sealed record SessionModelRequest(string? SessionId, string? ProviderId, string? ModelId);

/// <summary>会话当前生效的生成参数（思考强度 / 上下文档位）。</summary>
/// <param name="ReasoningEffort">生效的思考强度；null 表示跟随模型。</param>
/// <param name="ContextTier">生效的上下文预算档位（token）；null 表示未知或未设。</param>
/// <param name="ReasoningEfforts">该会话模型可选的思考档位（空串=跟随模型；空集合=不显示控件）。</param>
/// <param name="ContextTiers">该会话模型可选的上下文档位（token；空表示不显示控件）。</param>
/// <param name="ContextWindow">该会话模型上下文窗口。</param>
public sealed record SessionGenerationSettingsDto(
    string? ReasoningEffort,
    int? ContextTier,
    IReadOnlyList<string> ReasoningEfforts,
    IReadOnlyList<int> ContextTiers,
    int? ContextWindow);

/// <summary>写入会话生成参数（整体替换：字段缺省即清除该覆盖）。</summary>
/// <param name="SessionId">会话标识；空表示写全局默认。</param>
/// <param name="ReasoningEffort">思考强度枚举名；null 表示清除覆盖。</param>
/// <param name="ContextTier">上下文预算档位（token）；null 表示清除覆盖。</param>
public sealed record SessionGenerationSettingsRequest
{
    /// <summary>会话标识；空表示写全局默认。</summary>
    public string? SessionId { get; init; }

    /// <summary>思考强度枚举名；null 表示清除覆盖。</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>上下文预算档位（token）；null 表示清除覆盖。</summary>
    public int? ContextTier { get; init; }
}