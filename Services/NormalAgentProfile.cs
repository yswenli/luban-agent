/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： NormalAgentProfile
*版本号： V1.0.0.0
*唯一标识：普通 Agent 配置
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：普通工作区的 Agent 配置，启用全部工具，不使用检索模式
*
*****************************************************************************/
using LuBan.AIAgent.Plugins;

namespace LubanAgent.Services;

/// <summary>
/// 普通工作区的 Agent 配置，启用全部工具，不使用检索模式。
/// </summary>
public class NormalAgentProfile : AgentProfile
{
    /// <summary>
    /// 普通工作区的系统提示词。
    /// </summary>
    public override string SystemPrompt => @"你是一个智能助手，可以帮助用户完成各类任务。
你可以使用以下能力：
- 调用工具完成任务（文件操作、代码执行、搜索等）
- 根据用户需求选择合适的工具
- 在执行敏感操作前向用户确认

请根据用户的输入，结合可用的工具，给出准确、有帮助的回复。";

    /// <summary>
    /// 启用的工具组列表，null 表示启用全部工具。
    /// </summary>
    public override string[]? ToolGroups => null;

    /// <summary>
    /// 检索模式，null 表示不使用检索。
    /// </summary>
    public override string? RetrievalMode => null;

    /// <summary>
    /// 注册工作区相关的自定义规则。
    /// </summary>
    /// <remarks>
    /// 框架 <see cref="RuleEngine"/> 通过 <c>ConfigManager.CustomRules</c> 惰性合并自定义规则，
    /// 不支持运行时直接注册。工作区 rules 目录下的规则文件由 <see cref="LoadCustomRules"/> 加载，
    /// 后续可通过 <c>ConfigManager.AddCustomRule</c> 持久化注册。
    /// </remarks>
    protected override Task RegisterRulesAsync(RuleEngine engine, WorkspaceInfo workspace)
    {
        // 触发规则文件加载与校验（实际合并由 ConfigManager 完成）
        LoadCustomRules(workspace);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注册工作区相关的 MCP 服务器。
    /// </summary>
    /// <remarks>
    /// 框架 <see cref="ToolPluginRegistry"/> 通过 DI 加载插件，不支持运行时注册 MCP 服务器。
    /// 工作区 mcps 目录下的配置文件需通过 <c>ConfigManager.AddMcpServer</c> 持久化注册，
    /// 当前版本暂未实现该桥接逻辑。
    /// </remarks>
    protected override Task RegisterMcpServersAsync(ToolPluginRegistry registry, WorkspaceInfo workspace)
    {
        return Task.CompletedTask;
    }
}
