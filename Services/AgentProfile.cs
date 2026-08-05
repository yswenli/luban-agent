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
using LuBan.AIAgent.MCP;
using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Skills;

namespace LubanAgent.Services;

/// <summary>
/// Agent 配置抽象基类，定义不同类型 Agent 的系统提示词、工具组、检索模式及创建流程。
/// </summary>
/// <remarks>
/// 派生类通过重写 <see cref="SystemPrompt"/>、<see cref="ToolGroups"/>、<see cref="RetrievalMode"/>
/// 提供特定于 Agent 类型的配置。<see cref="CreateAgentAsync"/> 会在创建 Agent 前
/// 自动调用各注册表的 LoadFromWorkspace 加载工作区级组件。
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
    /// 当前激活的 Skill（对话内通过 /skill -switch 切换）。
    /// </summary>
    public ISkill? ActiveSkill { get; set; }

    /// <summary>
    /// 创建 Agent 实例。先加载工作区级别的组件（Skills/MCPs/Rules），再通过工厂创建 Agent。
    /// </summary>
    /// <remarks>
    /// 工作区组件加载遵循三级优先级：硬编码 > 工作区文件 > config.json。
    /// 每次创建 Agent 都会调用加载方法，确保工作区切换后组件列表正确更新。
    /// </remarks>
    /// <param name="factory">Agent 工厂</param>
    /// <param name="modelName">模型名称，格式 "provider:model"</param>
    /// <param name="workspace">工作区信息</param>
    /// <param name="ruleEngine">规则引擎</param>
    /// <param name="pluginRegistry">工具插件注册表</param>
    /// <param name="skillRegistry">Skill 注册表</param>
    /// <param name="mcpRegistry">MCP 注册表</param>
    /// <returns>LuBanAgent 实例</returns>
    public virtual async Task<LuBanAgent> CreateAgentAsync(
        ILuBanAgentFactory factory,
        string modelName,
        WorkspaceInfo workspace,
        RuleEngine ruleEngine,
        ToolPluginRegistry pluginRegistry,
        SkillRegistry skillRegistry,
        MCPRegistry mcpRegistry)
    {
        // 加载工作区级别的组件（三级优先级：硬编码 > 工作区文件 > config.json）
        skillRegistry.LoadFromWorkspace(workspace.RootPath);
        mcpRegistry.LoadFromWorkspace(workspace.RootPath);
        ruleEngine.LoadFromWorkspace(workspace.RootPath);

        // 将工作区上下文和激活的 Skill 注入系统提示词
        var fullPrompt = BuildFullPrompt(workspace, ActiveSkill, ruleEngine);

        return await factory.CreateAsync(
            modelName: modelName,
            systemPrompt: fullPrompt,
            toolGroups: ToolGroups,
            useSessionHistory: true);
    }

    /// <summary>
    /// 构建包含工作区上下文和激活 Skill 的完整系统提示词。
    /// </summary>
    private string BuildFullPrompt(WorkspaceInfo workspace, ISkill? activeSkill, LuBan.AIAgent.Rules.RuleEngine ruleEngine)
    {
        var sb = new System.Text.StringBuilder(SystemPrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("## 当前工作区");
        sb.AppendLine($"- 名称: {workspace.Name}");
        sb.AppendLine($"- 根目录: {workspace.RootPath}");
        sb.AppendLine($"- 类型: {workspace.Type}");
        sb.AppendLine();
        sb.AppendLine("## 路径使用说明");
        sb.AppendLine("- 搜索文件/内容时，rootPath 参数请使用工作区根目录的绝对路径，或使用 \".\" （已设置为根目录）");
        sb.AppendLine($"- 示例: Grep(rootPath=\"{workspace.RootPath}\", pattern=\"关键字\")");
        sb.AppendLine("- 示例: ListDirectory(path=\".\") 或 ListDirectory(path=\"" + workspace.RootPath + "\")");

        if (activeSkill != null && !string.IsNullOrEmpty(activeSkill.PromptTemplate))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"## 当前激活的 Skill: {activeSkill.Name}");
            sb.AppendLine(activeSkill.PromptTemplate);
        }

        if (ruleEngine.GetRule("base-behavior") is LuBan.AIAgent.Rules.IContentRule contentRule
            && !string.IsNullOrWhiteSpace(contentRule.Content))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## 基础行为规则");
            sb.AppendLine(contentRule.Content);
        }

        return sb.ToString();
    }
}
