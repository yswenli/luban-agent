/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： AgentHostService
*版本号： V1.0.0.0
*唯一标识：Agent 宿主服务
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：Agent 宿主服务：封装 Agent 创建与流式对话，输出 UI 无关的 StreamEvent 序列。
*
*****************************************************************************/
using LuBan.AIAgent;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.MCP;
using LuBan.AIAgent.Rules;
using LuBan.AIAgent.Skills;
using LubanAgentCore.Agents;
using LubanAgentCore.Configuration;
using LubanAgentCore.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace LubanAgentCore.Services;

/// <summary>
/// Agent 宿主服务：封装 Agent 创建与流式对话，输出 UI 无关的 StreamEvent 序列。
/// CLI 和 GUI 共用此服务。
/// </summary>
public class AgentHostService
{
    private readonly IServiceProvider _services;
    private LuBanAgent? _agent;
    private WorkspaceInfo? _workspace;

    public IServiceProvider Services => _services;

    public AgentHostService(IServiceProvider services)
    {
        _services = services;
    }

    public bool IsInitialized => _agent != null;

    /// <summary>
    /// 重置 Agent（置空当前实例），下次对话时按最新配置重建。
    /// 用于模型切换等配置变更后让新配置生效。
    /// </summary>
    public void Reset()
    {
        _agent = null;
    }

    /// <summary>
    /// 初始化 Agent（在首次对话前调用）。
    /// </summary>
    public async Task InitializeAsync()
    {
        var workspaceManager = _services.GetRequiredService<IWorkspaceManager>();
        _workspace = workspaceManager.CurrentWorkspace
            ?? throw new InvalidOperationException("未设置当前工作区");

        var agentFactory = _services.GetRequiredService<ILuBanAgentFactory>();
        var ruleEngine = _services.GetRequiredService<RuleEngine>();
        var pluginRegistry = _services.GetRequiredService<ToolPluginRegistry>();
        var skillRegistry = _services.GetRequiredService<SkillRegistry>();
        var mcpRegistry = _services.GetRequiredService<MCPRegistry>();
        var configManager = _services.GetRequiredService<ConfigManager>();

        var modelName = configManager.SelectedModel
            ?? throw new InvalidOperationException("未选择模型");

        // 加载工作区级组件
        skillRegistry.LoadFromWorkspace(_workspace.RootPath);
        mcpRegistry.LoadFromWorkspace(_workspace.RootPath);
        ruleEngine.LoadFromWorkspace(_workspace.RootPath);

        // 选择 Profile
        AgentProfile profile = _workspace.Type == "Rag"
            ? new RagAgentProfile(_workspace)
            : new NormalAgentProfile();

        _agent = await profile.CreateAgentAsync(
            agentFactory, modelName, _workspace,
            ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);
    }

    /// <summary>
    /// 流式对话，输出 StreamEvent 序列。
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <param name="confirmHandler">工具确认处理器（返回 ConfirmResult）</param>
    /// <param name="permissionMode">权限模式</param>
    /// <param name="ct">取消令牌</param>
    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string input,
        Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult> confirmHandler,
        ToolPermissionMode permissionMode,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 确保 Agent 与当前工作区匹配：切换工作区后必须重新初始化，
        // 否则会复用上一工作区的 Profile（如普通工作区），
        // 导致 RAG 知识库不加载检索工具组/RAG 系统提示词，表现与常规工作区无异。
        var currentWorkspace = _services.GetRequiredService<IWorkspaceManager>().CurrentWorkspace;
        if (_agent == null || (currentWorkspace != null && _workspace?.WorkspaceId != currentWorkspace.WorkspaceId))
        {
            await InitializeAsync();
        }

        if (_agent == null)
            throw new InvalidOperationException("Agent 初始化失败，请先调用 InitializeAsync");

        var context = _services.GetRequiredService<ToolConfirmationContext>();
        context.Mode = permissionMode;
        context.CancellationToken = ct;
        context.WorkspacePathChecker = path => WorkspaceManager.IsWithinWorkspace(path);

        // 适配 ConfirmResult → bool
        context.Callback = (toolName, args) =>
        {
            var result = confirmHandler(toolName, args);
            if (result == ConfirmResult.AllowAll)
                context.AllowedThisTurn.Add(toolName);
            return result != ConfirmResult.Deny;
        };

        try
        {
            await foreach (var update in _agent.RunStreamingAsync(input, ct))
            {
                if (update.Contents is null) continue;

                foreach (var content in update.Contents)
                {
                    // 思考过程
                    if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    {
                        yield return new ThinkingDeltaEvent(reasoning.Text);
                        continue;
                    }

                    // 工具调用
                    if (content is FunctionCallContent functionCall)
                    {
                        var args = functionCall.Arguments != null
                            ? new Dictionary<string, object?>(functionCall.Arguments)
                            : new Dictionary<string, object?>();
                        yield return new ToolCallStartedEvent(
                            functionCall.Name,
                            functionCall.CallId,
                            args);
                        continue;
                    }

                    // 工具结果
                    if (content is FunctionResultContent functionResult)
                    {
                        if (functionResult.Exception is not null)
                        {
                            yield return new ToolCallFailedEvent(
                                functionResult.CallId,
                                functionResult.Exception.Message);
                        }
                        else
                        {
                            yield return new ToolCallCompletedEvent(functionResult.CallId);
                        }
                        continue;
                    }

                    // 正文回复
                    if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    {
                        yield return new TextDeltaEvent(text.Text);
                        continue;
                    }

                    // 错误
                    if (content is ErrorContent error)
                    {
                        yield return new ErrorEvent(error.Message);
                        continue;
                    }

                    // UsageContent 静默跳过
                }
            }
        }
        finally
        {
            context.Reset();
        }
    }
}
