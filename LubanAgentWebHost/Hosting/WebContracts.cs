/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebContracts
*描述：浏览器端专用接口的请求 / 响应契约（只服务 wwwroot 页面，不进入 LubanAgentCore 协议）
*
*****************************************************************************/

using LubanAgentCore.Protocol.Dtos;

namespace LubanAgentWebHost.Hosting;

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

/// <summary>切换全局模型选择。</summary>
public sealed record ModelSelectRequest
{
    /// <summary>供应商标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>模型标识。</summary>
    public string ModelId { get; init; } = string.Empty;
}

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

/// <summary>设置页作用域：<c>global</c> 或工作区标识。</summary>
public sealed record SettingsScopeRequest
{
    /// <summary>作用域（<c>global</c> 或工作区标识）。</summary>
    public string Scope { get; init; } = "global";
}

/// <summary>按作用域定位单个设置条目。</summary>
public sealed record SettingsKeyRequest
{
    /// <summary>作用域（<c>global</c> 或工作区标识）。</summary>
    public string Scope { get; init; } = "global";

    /// <summary>条目键（技能目录名 / 规则 Id / MCP 名 / A2A 文件名）。</summary>
    public string Key { get; init; } = string.Empty;
}

/// <summary>保存成功后的条目键。</summary>
/// <param name="Key">保存后的条目键。</param>
public sealed record SettingsKeyResult(string Key);

/// <summary>技能文件内容。</summary>
/// <param name="Name">技能名（目录名）。</param>
/// <param name="Description">描述。</param>
/// <param name="Category">分类。</param>
/// <param name="Triggers">触发词。</param>
/// <param name="Body">正文 Markdown。</param>
public sealed record SkillFileDto(string Name, string Description, string Category, string Triggers, string Body);

/// <summary>保存技能。</summary>
public sealed record SkillSaveRequest
{
    /// <summary>作用域（<c>global</c> 或工作区标识）。</summary>
    public string Scope { get; init; } = "global";

    /// <summary>原条目键；新建时为 null（改名时用于移动目录）。</summary>
    public string? OriginalId { get; init; }

    /// <summary>技能名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>分类。</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>触发词（逗号分隔）。</summary>
    public string Triggers { get; init; } = string.Empty;

    /// <summary>正文 Markdown。</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>规则文件内容。</summary>
/// <param name="Id">规则 Id（文件名）。</param>
/// <param name="Name">名称。</param>
/// <param name="Description">描述。</param>
/// <param name="ActionTypePattern">动作类型通配符。</param>
/// <param name="TargetPattern">目标通配符。</param>
/// <param name="Action">allow / deny。</param>
/// <param name="Priority">优先级。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="Content">引导正文。</param>
public sealed record RuleFileDto(
    string Id,
    string Name,
    string Description,
    string ActionTypePattern,
    string TargetPattern,
    string Action,
    int Priority,
    bool Enabled,
    string Content);

/// <summary>保存规则。</summary>
public sealed record RuleSaveRequest
{
    /// <summary>作用域（<c>global</c> 或工作区标识）。</summary>
    public string Scope { get; init; } = "global";

    /// <summary>原条目键；新建时为 null。</summary>
    public string? OriginalId { get; init; }

    /// <summary>规则 Id。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>动作类型通配符。</summary>
    public string ActionTypePattern { get; init; } = string.Empty;

    /// <summary>目标通配符。</summary>
    public string TargetPattern { get; init; } = string.Empty;

    /// <summary>allow / deny。</summary>
    public string Action { get; init; } = "allow";

    /// <summary>优先级。</summary>
    public int Priority { get; init; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>引导正文。</summary>
    public string Content { get; init; } = string.Empty;
}

/// <summary>MCP 配置文件内容。</summary>
/// <param name="Name">服务名（文件名）。</param>
/// <param name="Description">描述。</param>
/// <param name="Transport">传输方式（stdio / sse / http）。</param>
/// <param name="Target">启动命令（stdio）或端点 URL（http / sse）。</param>
/// <param name="Args">启动参数。</param>
/// <param name="Enabled">是否启用。</param>
public sealed record McpFileDto(
    string Name,
    string Description,
    string Transport,
    string Target,
    IReadOnlyList<string> Args,
    bool Enabled);

/// <summary>保存 MCP 配置。</summary>
public sealed record McpSaveRequest
{
    /// <summary>作用域（<c>global</c> 或工作区标识）。</summary>
    public string Scope { get; init; } = "global";

    /// <summary>原条目键；新建时为 null。</summary>
    public string? OriginalId { get; init; }

    /// <summary>服务名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>传输方式（空 = stdio）。</summary>
    public string Transport { get; init; } = "stdio";

    /// <summary>启动命令（stdio）或端点 URL（http / sse）。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>启动参数（stdio）。</summary>
    public IReadOnlyList<string>? Args { get; init; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>供应商设置项。</summary>
/// <param name="Name">标识（小写）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="BaseUrl">基址。</param>
/// <param name="ApiKeyMask">API Key 掩码（不回显明文）。</param>
/// <param name="NetworkTimeoutSeconds">网络超时（秒）。</param>
/// <param name="CustomModels">自定义模型。</param>
/// <param name="IsSelected">是否包含当前全局选择。</param>
public sealed record ProviderListItemDto(
    string Name,
    string DisplayName,
    string? BaseUrl,
    string ApiKeyMask,
    int? NetworkTimeoutSeconds,
    IReadOnlyList<string> CustomModels,
    bool IsSelected);

/// <summary>保存供应商。<see cref="ApiKey"/> 留空表示不修改已有密钥。</summary>
public sealed record ProviderSaveRequest
{
    /// <summary>标识（小写）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>API Key 明文；留空表示不修改。</summary>
    public string? ApiKey { get; init; }

    /// <summary>基址。</summary>
    public string? BaseUrl { get; init; }

    /// <summary>显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>网络超时（秒）；小于等于 0 表示使用默认。</summary>
    public int? NetworkTimeoutSeconds { get; init; }

    /// <summary>自定义模型清单（按表单当前值做增量更新）。</summary>
    public IReadOnlyList<string>? CustomModels { get; init; }
}

/// <summary>供应商设置项删除请求。</summary>
public sealed record ProviderDeleteRequest
{
    /// <summary>供应商标识。</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>单个模型条目。</summary>
/// <param name="ModelId">模型标识。</param>
/// <param name="IsCustom">是否为用户自定义模型（可改名 / 删除）。</param>
/// <param name="IsSelected">是否为全局当前模型。</param>
public sealed record ModelItemDto(string ModelId, bool IsCustom, bool IsSelected);

/// <summary>按供应商分组的模型清单。</summary>
/// <param name="ProviderId">供应商标识。</param>
/// <param name="DisplayName">供应商显示名。</param>
/// <param name="Models">模型条目。</param>
public sealed record ProviderModelsDto(
    string ProviderId,
    string DisplayName,
    IReadOnlyList<ModelItemDto> Models);

/// <summary>按供应商定位模型。</summary>
public sealed record ModelTargetRequest
{
    /// <summary>供应商标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>模型标识。</summary>
    public string ModelId { get; init; } = string.Empty;
}

/// <summary>新增模型。</summary>
public sealed record ModelAddRequest
{
    /// <summary>供应商标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>模型标识。</summary>
    public string ModelId { get; init; } = string.Empty;
}

/// <summary>重命名模型。</summary>
public sealed record ModelRenameRequest
{
    /// <summary>供应商标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>原模型标识。</summary>
    public string OldModelId { get; init; } = string.Empty;

    /// <summary>新模型标识。</summary>
    public string NewModelId { get; init; } = string.Empty;
}

/// <summary>A2A 服务端配置。</summary>
/// <param name="Host">监听地址。</param>
/// <param name="Port">监听端口（0 = 随机）。</param>
/// <param name="Path">端点前缀。</param>
/// <param name="RequireApiKey">是否要求 API Key。</param>
/// <param name="ApiKeys">允许的 API Key 列表。</param>
/// <param name="AgentName">对外名称。</param>
/// <param name="AgentDescription">对外描述。</param>
/// <param name="AgentVersion">对外版本。</param>
public sealed record A2AServerDto(
    string Host,
    int Port,
    string Path,
    bool RequireApiKey,
    IReadOnlyList<string> ApiKeys,
    string? AgentName,
    string? AgentDescription,
    string? AgentVersion);

/// <summary>保存 A2A 服务端配置。</summary>
public sealed record A2AServerSaveRequest
{
    /// <summary>作用域（必须是工作区标识）。</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>监听地址。</summary>
    public string? Host { get; init; }

    /// <summary>监听端口（0 = 随机）。</summary>
    public int? Port { get; init; }

    /// <summary>端点前缀。</summary>
    public string? Path { get; init; }

    /// <summary>是否要求 API Key。</summary>
    public bool RequireApiKey { get; init; }

    /// <summary>允许的 API Key 列表。</summary>
    public IReadOnlyList<string>? ApiKeys { get; init; }

    /// <summary>对外名称。</summary>
    public string? AgentName { get; init; }

    /// <summary>对外描述。</summary>
    public string? AgentDescription { get; init; }

    /// <summary>对外版本。</summary>
    public string? AgentVersion { get; init; }
}

/// <summary>A2A 客户端列表项。</summary>
/// <param name="Key">文件名（条目键）。</param>
/// <param name="Name">客户端名称。</param>
/// <param name="Endpoint">远端地址。</param>
public sealed record A2AClientListItemDto(string Key, string? Name, string? Endpoint);

/// <summary>A2A 客户端配置。</summary>
/// <param name="Key">文件名（条目键）。</param>
/// <param name="Name">客户端名称。</param>
/// <param name="Endpoint">远端地址。</param>
/// <param name="ApiKey">出站 API Key 明文（本机配置）。</param>
/// <param name="RegisterAsSubAgent">是否注册为本地子代理。</param>
/// <param name="RemoteAgentName">远端 Agent 名。</param>
/// <param name="AllowAll">是否声明 AllowAll。</param>
public sealed record A2AClientDto(
    string Key,
    string? Name,
    string? Endpoint,
    string? ApiKey,
    bool RegisterAsSubAgent,
    string? RemoteAgentName,
    bool AllowAll);

/// <summary>保存 A2A 客户端配置。</summary>
public sealed record A2AClientSaveRequest
{
    /// <summary>作用域（必须是工作区标识）。</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>原文件名；新建时为 null。</summary>
    public string? OldKey { get; init; }

    /// <summary>客户端名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>远端地址。</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>出站 API Key。</summary>
    public string? ApiKey { get; init; }

    /// <summary>是否注册为本地子代理。</summary>
    public bool RegisterAsSubAgent { get; init; }

    /// <summary>远端 Agent 名。</summary>
    public string? RemoteAgentName { get; init; }

    /// <summary>是否声明 AllowAll。</summary>
    public bool AllowAll { get; init; }
}