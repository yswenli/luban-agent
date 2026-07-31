/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： RagAgentProfile
*版本号： V1.0.0.0
*唯一标识：RAG Agent 配置
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：RAG 工作区的 Agent 配置，启用检索与文件系统工具组，使用 auto 检索模式
*
*=================================================
*修改标记
*修改时间：2026/7/31
*修改人： yswenli
*版本号： V1.0.0.0
*描述：RAG 工作区的 Agent 配置，启用检索与文件系统工具组，使用 auto 检索模式
*
*****************************************************************************/
using LuBan.AIAgent.Plugins;

namespace LubanAgent.Services;

/// <summary>
/// RAG 工作区的 Agent 配置，启用检索与文件系统工具组，使用 auto 检索模式。
/// </summary>
/// <remarks>
/// 系统提示词与工具组可通过工作区 <c>rag-config.json</c> 的 <c>agentProfile</c> 节点覆盖。
/// </remarks>
public class RagAgentProfile : AgentProfile
{
    private readonly WorkspaceInfo _workspace;
    private string _systemPrompt;
    private string[]? _toolGroups;

    /// <summary>
    /// 创建 RagAgentProfile 实例，并加载工作区的 RAG 配置。
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    public RagAgentProfile(WorkspaceInfo workspace)
    {
        _workspace = workspace;
        _systemPrompt = "你是一个知识库问答专家。请基于检索到的文档片段回答问题，不要超出文档范围。如果文档中没有相关信息，请明确告知用户。";
        _toolGroups = new[] { "retrieval", "filesystem" };
        LoadRagConfig();
    }

    /// <summary>
    /// 系统提示词。
    /// </summary>
    public override string SystemPrompt => _systemPrompt;

    /// <summary>
    /// 启用的工具组列表，null 表示启用全部工具。
    /// </summary>
    public override string[]? ToolGroups => _toolGroups;

    /// <summary>
    /// 检索模式，使用 auto 表示自动检索。
    /// </summary>
    public override string? RetrievalMode => "auto";

    /// <summary>
    /// 从工作区的 rag-config.json 加载 agentProfile 配置，覆盖默认的系统提示词与工具组。
    /// </summary>
    private void LoadRagConfig()
    {
        if (_workspace.ConfigPath == null) return;

        var configPath = Path.Combine(_workspace.RootPath, _workspace.ConfigPath, "rag-config.json");
        if (!File.Exists(configPath)) return;

        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("agentProfile", out var profile))
            {
                if (profile.TryGetProperty("systemPrompt", out var sp))
                    _systemPrompt = sp.GetString() ?? _systemPrompt;
                if (profile.TryGetProperty("toolGroups", out var tg) && tg.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var groups = tg.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    // 空数组视为未配置（保持默认），避免传空数组给工厂导致无工具可用
                    _toolGroups = groups.Count > 0 ? groups.ToArray() : _toolGroups;
                }
            }
        }
        catch
        {
            // 忽略 rag-config.json 解析失败，沿用默认配置
        }
    }

    /// <summary>
    /// 注册工作区相关的自定义规则。
    /// </summary>
    /// <remarks>
    /// 框架 <see cref="RuleEngine"/> 通过 <c>ConfigManager.CustomRules</c> 惰性合并自定义规则，
    /// 不支持运行时直接注册。RAG 的多版本去重逻辑在 <c>AgiCommand.InjectRetrievalContextAsync</c> 中实现，
    /// 不依赖规则引擎。工作区 rules 目录下的规则文件由 <see cref="LoadCustomRules"/> 加载。
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
