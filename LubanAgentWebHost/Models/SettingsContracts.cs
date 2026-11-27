/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： SettingsContracts
*描述：设置页（技能 / 规则 / MCP）的请求 / 响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

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

    /// <summary>条目键（技能目录名 / 规则 Id / MCP 名）。</summary>
    public string Key { get; init; } = string.Empty;
}

/// <summary>保存成功后的条目键。</summary>
/// <param name="Key">保存后的条目键。</param>
public sealed record SettingsKeyResult(string Key);

/// <summary>技能文件内容。</summary>
/// <param name="Id">目录名（条目的稳定键）。</param>
/// <param name="Name">技能显示名（frontmatter 的 name）。</param>
/// <param name="Description">描述。</param>
/// <param name="Category">分类。</param>
/// <param name="Triggers">触发词。</param>
/// <param name="Body">正文 Markdown。</param>
public sealed record SkillFileDto(string Id, string Name, string Description, string Category, string Triggers, string Body);

/// <summary>技能列表项（不读正文，供左侧分组列表用）。</summary>
/// <param name="Id">目录名。</param>
/// <param name="Name">显示名。</param>
/// <param name="Category">分类。</param>
public sealed record SkillFileSummaryDto(string Id, string Name, string Category);

/// <summary>输入 <c>@</c> 时可选的技能（当前会话可用技能，只含启用项）。</summary>
/// <param name="Id">技能标识（插入输入框的文本）。</param>
/// <param name="Name">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="Category">分类。</param>
/// <param name="IsBuiltin">是否内置技能。</param>
public sealed record AvailableSkillDto(string Id, string Name, string Description, string Category, bool IsBuiltin);

/// <summary>导入技能 ZIP 包。</summary>
public sealed record SkillImportRequest
{
    /// <summary>作用域（<c>global</c> 或工作区标识）。</summary>
    public string Scope { get; init; } = "global";

    /// <summary>原始文件名（仅用于提示，不参与落盘路径）。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>ZIP 内容的 Base64。</summary>
    public string ContentBase64 { get; init; } = string.Empty;
}

/// <summary>保存技能。</summary>
public sealed record SkillSaveRequest
{
    /// <summary>作用域（<c>global</c> 或工作区标识）。</summary>
    public string Scope { get; init; } = "global";

    /// <summary>原条目键（目录名）；新建时为 null。保存时目录不变，只改 frontmatter。</summary>
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