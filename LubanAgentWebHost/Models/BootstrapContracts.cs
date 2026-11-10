/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： BootstrapContracts
*描述：浏览器端首屏引导与命令清单的响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

/// <summary>首屏引导数据。</summary>
/// <param name="UserId">当前宿主身份。</param>
/// <param name="Version">程序集信息版本。</param>
/// <param name="Workspaces">工作区列表。</param>
/// <param name="CurrentWorkspaceId">当前工作区标识；未设置时为 null。</param>
/// <param name="Providers">供应商与模型（不含密钥）。</param>
/// <param name="Selection">当前全局模型选择。</param>
/// <param name="PermissionModes">权限模式清单。</param>
/// <param name="ReasoningEfforts">推理强度清单。</param>
/// <param name="Attachments">附件策略。</param>
/// <param name="Confirmation">工具确认策略。</param>
public sealed record BootstrapDto(
    string UserId,
    string? Version,
    IReadOnlyList<WorkspaceDto> Workspaces,
    string? CurrentWorkspaceId,
    IReadOnlyList<ProviderSummaryDto> Providers,
    ModelSelectionDto? Selection,
    IReadOnlyList<PermissionModeDto> PermissionModes,
    IReadOnlyList<string> ReasoningEfforts,
    AttachmentPolicyDto Attachments,
    ConfirmationPolicyDto Confirmation);

/// <summary>供应商摘要。</summary>
/// <param name="Id">供应商标识（小写）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Models">可用模型。</param>
public sealed record ProviderSummaryDto(string Id, string DisplayName, IReadOnlyList<string> Models);

/// <summary>全局模型选择。</summary>
/// <param name="ProviderId">供应商标识。</param>
/// <param name="ModelId">模型标识。</param>
public sealed record ModelSelectionDto(string? ProviderId, string? ModelId);

/// <summary>权限模式。</summary>
/// <param name="Key">稳定键名（default / plan / accept-edits / bypass）。</param>
/// <param name="DisplayName">可读名称。</param>
public sealed record PermissionModeDto(string Key, string DisplayName);

/// <summary>附件策略。</summary>
/// <param name="Extensions">允许的扩展名（含点）。</param>
/// <param name="MaxCount">每轮最多附件数。</param>
/// <param name="UndecodableExtensions">需要提示转换格式的扩展名。</param>
/// <param name="MaxTextBytes">文本单文件上限。</param>
/// <param name="MaxImageBytes">图片缩放阈值（超过即重编码）。</param>
/// <param name="MaxImageEdge">图片最长边阈值（超过即缩放）。</param>
public sealed record AttachmentPolicyDto(
    IReadOnlyList<string> Extensions,
    int MaxCount,
    IReadOnlyList<string> UndecodableExtensions,
    long MaxTextBytes,
    long MaxImageBytes,
    int MaxImageEdge);

/// <summary>工具确认策略。</summary>
/// <param name="TimeoutSeconds">等待上限（秒），超时按拒绝处理。</param>
public sealed record ConfirmationPolicyDto(int TimeoutSeconds);

/// <summary>斜杠命令（浏览器端）。</summary>
/// <param name="Name">命令名（含 /）。</param>
/// <param name="Aliases">别名。</param>
/// <param name="Description">描述。</param>
/// <param name="Category">分类。</param>
/// <param name="Usage">用法。</param>
/// <param name="Action">前端动作标识（形如 <c>openSettings:provider</c> 或 <c>action:clearMessages</c>）。</param>
public sealed record WebCommandDto(
    string Name,
    IReadOnlyList<string> Aliases,
    string Description,
    string Category,
    string Usage,
    string Action);