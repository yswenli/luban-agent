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
    /// <param name="engine">规则引擎</param>
    /// <param name="workspace">工作区信息</param>
    /// <remarks>
    /// 当前框架的 <see cref="RuleEngine"/> 通过 <c>ConfigManager</c> 惰性合并自定义规则，
    /// 不支持运行时直接注册。<see cref="RagPrecisionRule"/> 亦为标记规则，
    /// 实际的多版本去重逻辑在 <c>AgiCommand.InjectRetrievalContextAsync</c> 中实现。
    /// </remarks>
    protected override Task RegisterRulesAsync(RuleEngine engine, WorkspaceInfo workspace)
    {
        var customRules = LoadCustomRules(workspace);
        foreach (var rule in customRules)
        {
            // no-op: RuleEngine 不支持运行时注册，规则经 ConfigManager.CustomRules 惰性合并
        }
        // RagPrecisionRule 注册亦为 no-op（框架限制），实际多版本去重在 AgiCommand.InjectRetrievalContextAsync
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注册工作区相关的 MCP 服务器。
    /// </summary>
    /// <param name="registry">工具插件注册表</param>
    /// <param name="workspace">工作区信息</param>
    /// <remarks>
    /// 扫描工作区 mcps 目录下的配置文件。当前框架的 <see cref="ToolPluginRegistry"/>
    /// 通过 DI 加载插件，不支持运行时注册 MCP 服务器，此处为预留实现。
    /// </remarks>
    protected override Task RegisterMcpServersAsync(ToolPluginRegistry registry, WorkspaceInfo workspace)
    {
        if (workspace.ConfigPath == null) return Task.CompletedTask;

        var mcpsDir = Path.Combine(workspace.RootPath, workspace.ConfigPath, "mcps");
        if (!Directory.Exists(mcpsDir)) return Task.CompletedTask;

        foreach (var file in Directory.GetFiles(mcpsDir, "*.json"))
        {
            try
            {
                // TODO: MCP 服务器注册逻辑依赖框架 API（ConfigManager.AddMcpServer），
                // 当前 ToolPluginRegistry 无运行时注册方法，此处为预留实现。
            }
            catch
            {
                // 忽略单个 MCP 配置文件解析失败
            }
        }
        return Task.CompletedTask;
    }
}
