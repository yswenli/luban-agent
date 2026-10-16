/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*Author：yswenli
*命名空间：LubanAgentCore.Configuration
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

namespace LubanAgentCore.Configuration;

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

    /// <summary>
    /// 各模型的多模态（图片）支持情况登记表，键为 "provider:model"（小写归一化），
    /// 值为是否支持。无记录表示未知，由 LLM 实际调用结果动态回填。
    /// </summary>
    public Dictionary<string, bool> ModelCapabilities { get; set; } = new();

    /// <summary>
    /// 外部集成配置与凭据（键为归一化键，如 <c>yunxiao_pat</c>、<c>yunxiao_organization_id</c>）。
    /// </summary>
    /// <remarks>
    /// PRD §4.9.3 放宽裁决：凭据沿用宿主现有配置存储基线（与 <c>Provider.ApiKey</c> 同基线），
    /// 不引入额外加密层。本字段即该基线的落点，由 <c>CodexCredentialStore</c> 经
    /// <c>ICredentialStore</c> 读写，供云效 PAT 与组织 ID 使用。
    /// </remarks>
    public Dictionary<string, string> Integrations { get; set; } = new();
}
