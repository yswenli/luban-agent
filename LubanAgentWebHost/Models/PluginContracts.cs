/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： PluginContracts
*描述：Web 插件管理的请求 / 响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

/// <summary>安装插件。</summary>
public sealed record PluginInstallRequest
{
    /// <summary>原始文件名（仅用于提示，不参与落盘路径）。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>ZIP 内容的 Base64。</summary>
    public string ContentBase64 { get; init; } = string.Empty;
}

/// <summary>升级插件。</summary>
public sealed record PluginUpgradeRequest
{
    /// <summary>目标插件标识。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>原始文件名（仅用于提示，不参与落盘路径）。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>ZIP 内容的 Base64。</summary>
    public string ContentBase64 { get; init; } = string.Empty;
}

/// <summary>按标识操作插件（启停 / 卸载）。</summary>
public sealed record PluginIdRequest
{
    /// <summary>插件标识。</summary>
    public string Id { get; init; } = string.Empty;
}

/// <summary>插件贡献的技能摘要。</summary>
/// <param name="Id">技能标识。</param>
/// <param name="Name">技能名。</param>
/// <param name="Description">说明。</param>
public sealed record PluginSkillDto(string Id, string Name, string? Description);

/// <summary>插件声明的 MCP 服务摘要。</summary>
/// <param name="Name">服务名。</param>
/// <param name="Transport">传输方式。</param>
public sealed record PluginMcpDto(string Name, string? Transport);

/// <summary>插件的页面入口。</summary>
/// <param name="EntryId">侧栏条目标识。</param>
/// <param name="Label">显示名。</param>
/// <param name="Icon">图标标识。</param>
/// <param name="WebPage">包内相对路径。</param>
/// <param name="Url">当前镜像的访问地址；镜像不可用时为空串。</param>
public sealed record PluginPageDto(string EntryId, string Label, string? Icon, string WebPage, string Url);

/// <summary>插件列表项。</summary>
/// <param name="Id">插件标识。</param>
/// <param name="Name">展示名。</param>
/// <param name="Version">版本。</param>
/// <param name="Author">作者。</param>
/// <param name="Description">简介。</param>
/// <param name="State"><c>installed/enabled/disabled/error</c>。</param>
/// <param name="IsBuiltin">是否内置插件。</param>
/// <param name="RequiresRestart">是否需要重启才能彻底生效。</param>
/// <param name="LastError">最近一次错误。</param>
/// <param name="Skills">贡献的技能。</param>
/// <param name="McpServers">声明的 MCP 服务。</param>
/// <param name="Pages">页面入口。</param>
/// <param name="PageError">页面镜像失败原因；成功或无需镜像时为 <c>null</c>。</param>
public sealed record PluginItemDto(
    string Id,
    string Name,
    string Version,
    string? Author,
    string? Description,
    string State,
    bool IsBuiltin,
    bool RequiresRestart,
    string? LastError,
    IReadOnlyList<PluginSkillDto> Skills,
    IReadOnlyList<PluginMcpDto> McpServers,
    IReadOnlyList<PluginPageDto> Pages,
    string? PageError);

/// <summary>插件列表。</summary>
/// <param name="RootPath">Web 插件根目录（仅本机单用户场景回传）。</param>
/// <param name="Items">插件项。</param>
public sealed record PluginListDto(string RootPath, IReadOnlyList<PluginItemDto> Items);

/// <summary>安装 / 升级 / 启停后的结果。</summary>
/// <param name="Id">插件标识。</param>
/// <param name="State">操作后的状态。</param>
/// <param name="Warnings">不阻断安装的提示（如白名单外扩展名）。</param>
/// <param name="Plugin">最新插件项（卸载时为 <c>null</c>）。</param>
public sealed record PluginChangeResultDto(
    string Id,
    string State,
    IReadOnlyList<string> Warnings,
    PluginItemDto? Plugin);