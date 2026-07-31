/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： AgentProfile
*版本号： V1.0.0.0
*唯一标识：Agent 抽象基类
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：Agent 配置抽象基类，定义不同类型 Agent 的系统提示词、工具组、检索模式及创建流程
*
*=================================================
*修改标记
*修改时间：2026/7/31
*修改人： yswenli
*版本号： V1.0.0.0
*描述：Agent 配置抽象基类，定义不同类型 Agent 的系统提示词、工具组、检索模式及创建流程
*
*****************************************************************************/
using LuBan.AIAgent.Plugins;

namespace LubanAgent.Services;

/// <summary>
/// Agent 配置抽象基类，定义不同类型 Agent 的系统提示词、工具组、检索模式及创建流程。
/// </summary>
/// <remarks>
/// 派生类通过重写 <see cref="SystemPrompt"/>、<see cref="ToolGroups"/>、<see cref="RetrievalMode"/>
/// 提供特定于 Agent 类型的配置，并通过重写 <see cref="RegisterRulesAsync"/> 与
/// <see cref="RegisterMcpServersAsync"/> 注册工作区相关的规则与 MCP 服务器。
/// </remarks>
public abstract class AgentProfile
{
    /// <summary>
    /// 系统提示词。
    /// </summary>
    public abstract string SystemPrompt { get; }

    /// <summary>
    /// 启用的工具组列表，null 表示启用全部工具。
    /// </summary>
    public abstract string[]? ToolGroups { get; }

    /// <summary>
    /// 检索模式：null 表示不使用检索；"auto" 表示自动检索注入（RAG 工作区使用）。
    /// </summary>
    public abstract string? RetrievalMode { get; }

    /// <summary>
    /// 创建 Agent 实例。先注册工作区相关的规则与 MCP 服务器，再通过工厂创建 Agent。
    /// </summary>
    /// <remarks>
    /// 规则与 MCP 注册由派生类实现。注意：每次创建 Agent 都会调用注册方法，
    /// 派生类应确保注册逻辑幂等（重复注册相同规则不应有副作用）。
    /// </remarks>
    /// <param name="factory">Agent 工厂</param>
    /// <param name="modelName">模型名称，格式 "provider:model"</param>
    /// <param name="workspace">工作区信息</param>
    /// <param name="ruleEngine">规则引擎</param>
    /// <param name="pluginRegistry">工具插件注册表</param>
    /// <returns>LuBanAgent 实例</returns>
    public virtual async Task<LuBanAgent> CreateAgentAsync(
        ILuBanAgentFactory factory,
        string modelName,
        WorkspaceInfo workspace,
        RuleEngine ruleEngine,
        ToolPluginRegistry pluginRegistry)
    {
        // 注册工作区相关的规则与 MCP（派生类应确保幂等性）
        await RegisterRulesAsync(ruleEngine, workspace);
        await RegisterMcpServersAsync(pluginRegistry, workspace);

        return await factory.CreateAsync(
            modelName: modelName,
            systemPrompt: SystemPrompt,
            toolGroups: ToolGroups,
            useSessionHistory: true);
    }

    /// <summary>
    /// 注册工作区相关的规则到规则引擎。
    /// </summary>
    /// <param name="engine">规则引擎</param>
    /// <param name="workspace">工作区信息</param>
    protected abstract Task RegisterRulesAsync(RuleEngine engine, WorkspaceInfo workspace);

    /// <summary>
    /// 注册工作区相关的 MCP 服务器到插件注册表。
    /// </summary>
    /// <param name="registry">工具插件注册表</param>
    /// <param name="workspace">工作区信息</param>
    protected abstract Task RegisterMcpServersAsync(ToolPluginRegistry registry, WorkspaceInfo workspace);

    /// <summary>
    /// 从工作区的 rules 目录加载自定义规则配置文件（*.json）。
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    /// <returns>启用的自定义规则列表</returns>
    /// <remarks>
    /// 规则文件位于 <c>&lt;RootPath&gt;/&lt;ConfigPath&gt;/rules/*.json</c>，
    /// 仅加载 <see cref="CustomRuleConfig.Enabled"/> 为 true 的规则。
    /// </remarks>
    protected static List<CustomRule> LoadCustomRules(WorkspaceInfo workspace)
    {
        var rules = new List<CustomRule>();
        if (workspace.ConfigPath == null) return rules;

        var rulesDir = Path.Combine(workspace.RootPath, workspace.ConfigPath, "rules");
        if (!Directory.Exists(rulesDir)) return rules;

        foreach (var file in Directory.GetFiles(rulesDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var config = System.Text.Json.JsonSerializer.Deserialize<CustomRuleConfig>(json);
                if (config != null && config.Enabled)
                    rules.Add(new CustomRule(config));
            }
            catch
            {
                // 忽略单个规则文件解析失败，继续加载其余文件
            }
        }
        return rules;
    }
}
