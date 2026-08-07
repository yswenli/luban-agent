/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*Author：yswenli
*命名空间：LubanAgent.Configuration
*文件名： AppConfig
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：应用配置文件模型
*
*****************************************************************************/
using LuBan.AIAgent.Configuration;

namespace LubanAgent.Configuration;

/// <summary>
/// 应用配置模型，包含 Provider、选中的模型、自定义 Skill/规则、MCP 服务器及内置功能的禁用状态
/// </summary>
public class AppConfig
{
    /// <summary>
    /// 已配置的 AI Provider 列表
    /// </summary>
    public List<ProviderConfig> Providers { get; set; } = new();

    /// <summary>
    /// 当前选中的模型（格式：providerName:modelName）
    /// </summary>
    public string? SelectedModel { get; set; }

    /// <summary>
    /// 自定义 Skill 配置列表
    /// </summary>
    public List<CustomSkillConfig> CustomSkills { get; set; } = new();

    /// <summary>
    /// 自定义规则配置列表
    /// </summary>
    public List<CustomRuleConfig> CustomRules { get; set; } = new();

    /// <summary>
    /// MCP 服务器配置列表
    /// </summary>
    public List<McpServerConfig> McpServers { get; set; } = new();

    /// <summary>
    /// 已禁用的内置 Skill 标识列表
    /// </summary>
    public List<string> DisabledBuiltinSkills { get; set; } = new();

    /// <summary>
    /// 已禁用的内置规则标识列表
    /// </summary>
    public List<string> DisabledBuiltinRules { get; set; } = new();

    /// <summary>
    /// 已禁用的内置 MCP 客户端名称列表
    /// </summary>
    public List<string> DisabledBuiltinMcpClients { get; set; } = new();
}
