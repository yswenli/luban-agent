/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.ViewModels.Settings
*文件名： SettingsContracts
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗 ViewModel 层的返回值、列表项与表单契约（不含任何 UI 类型）
*
*****************************************************************************/

namespace LubanAgentCodex.ViewModels.Settings;

/// <summary>
/// 设置窗一次操作的结果：<see cref="Ok"/> 为 false 时 <see cref="Message"/> 是错误文案；
/// 成功时 <see cref="Message"/> 用于可选的成功提示，<see cref="Key"/> 为操作后的条目键（如有）。
/// </summary>
/// <param name="Ok">是否成功。</param>
/// <param name="Message">提示文案（成功或失败）。</param>
/// <param name="Key">操作后的条目键（保存/新建/改名后用于回写选中项）。</param>
public readonly record struct SettingsOutcome(bool Ok, string Message = "", string? Key = null)
{
    /// <summary>构造成功结果。</summary>
    /// <param name="message">可选成功提示。</param>
    /// <param name="key">操作后的条目键。</param>
    public static SettingsOutcome Success(string message = "", string? key = null) => new(true, message, key);

    /// <summary>构造失败结果。</summary>
    /// <param name="message">错误文案。</param>
    public static SettingsOutcome Failure(string message) => new(false, message);
}

/// <summary>
/// 中栏列表项（供应商 / 模型 / A2A 用；技能/规则/MCP 直接展示字符串键）。
/// </summary>
public sealed class SettingsListItem
{
    /// <summary>条目键（供应商=Name，模型=provider:model，A2A=文件名）。</summary>
    public string Key { get; init; } = "";

    /// <summary>列表展示文本。</summary>
    public string Display { get; init; } = "";

    /// <inheritdoc />
    public override string ToString() => Display;
}

/// <summary>技能表单（窗口采集，VM 负责校验与落盘）。</summary>
/// <param name="Name">技能名（必填）。</param>
/// <param name="Description">描述（必填）。</param>
/// <param name="Category">分类。</param>
/// <param name="Triggers">触发词（逗号分隔）。</param>
/// <param name="Body">正文 Markdown。</param>
public sealed record SkillForm(string Name, string Description, string Category, string Triggers, string Body);

/// <summary>规则表单。</summary>
/// <param name="Id">规则 Id（必填，即文件名）。</param>
/// <param name="Name">名称。</param>
/// <param name="Description">描述。</param>
/// <param name="ActionTypePattern">动作类型通配符。</param>
/// <param name="TargetPattern">目标通配符。</param>
/// <param name="Action">allow / deny。</param>
/// <param name="PriorityText">优先级原文（VM 内解析）。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="Content">引导正文。</param>
public sealed record RuleForm(
    string Id,
    string Name,
    string Description,
    string ActionTypePattern,
    string TargetPattern,
    string Action,
    string PriorityText,
    bool Enabled,
    string Content);

/// <summary>MCP 服务表单。</summary>
/// <param name="Name">服务名（必填，即文件名）。</param>
/// <param name="Description">描述。</param>
/// <param name="Transport">传输方式（空=stdio）。</param>
/// <param name="Command">命令或 URL。</param>
/// <param name="ArgsText">参数原文（一行一个）。</param>
/// <param name="Enabled">是否启用。</param>
public sealed record McpForm(
    string Name,
    string Description,
    string Transport,
    string Command,
    string ArgsText,
    bool Enabled);

/// <summary>供应商表单。</summary>
/// <param name="Name">唯一标识（必填，小写）。</param>
/// <param name="ApiKey">API Key（必填）。</param>
/// <param name="BaseUrl">基址（空=默认）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="NetworkTimeoutSecondsText">网络超时原文（空=默认）。</param>
/// <param name="CustomModels">自定义模型清单（以表单当前行为准，做增量更新）。</param>
public sealed record ProviderForm(
    string Name,
    string ApiKey,
    string BaseUrl,
    string DisplayName,
    string NetworkTimeoutSecondsText,
    IReadOnlyList<string> CustomModels);

/// <summary>A2A 服务端表单。</summary>
/// <param name="Host">监听地址（空=127.0.0.1）。</param>
/// <param name="PortText">端口原文（0=随机）。</param>
/// <param name="Path">端点前缀（空=/a2a）。</param>
/// <param name="RequireApiKey">是否要求 X-Api-Key。</param>
/// <param name="ApiKeysText">API Key 原文（一行一个）。</param>
/// <param name="AgentName">对外名称。</param>
/// <param name="AgentDescription">对外描述。</param>
/// <param name="AgentVersion">对外版本（空=1.0.0）。</param>
public sealed record A2aServerForm(
    string Host,
    string PortText,
    string Path,
    bool RequireApiKey,
    string ApiKeysText,
    string AgentName,
    string AgentDescription,
    string AgentVersion);

/// <summary>A2A 客户端表单。</summary>
/// <param name="Name">唯一标识（必填）。</param>
/// <param name="Endpoint">远端基址（必填）。</param>
/// <param name="ApiKey">API Key（明文）。</param>
/// <param name="RegisterAsSubAgent">是否注册为本地 SubAgent。</param>
/// <param name="RemoteAgentName">远端 Agent 名。</param>
/// <param name="AllowAll">是否对外声明 AllowAll。</param>
public sealed record A2aClientForm(
    string Name,
    string Endpoint,
    string ApiKey,
    bool RegisterAsSubAgent,
    string RemoteAgentName,
    bool AllowAll);