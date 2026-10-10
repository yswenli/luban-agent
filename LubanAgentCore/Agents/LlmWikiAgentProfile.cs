/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Agents
*文件名： LlmWikiAgentProfile
*版本号： V1.0.0.0
*唯一标识：LLM Wiki Agent 配置
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：LLM Wiki 工作区的 Agent 配置，启用 wiki 工具组维护知识库，开启每轮语义检索兜底注入并硬约束工具结果优先
*
*=================================================
*修改标记
*修改时间：2026/9/21
*修改人： yswenli
*版本号： V1.0.0.2
*描述：由 RagAgentProfile 改名为 LlmWikiAgentProfile，配置文件名改为 llmwiki-config.json，提示词措辞统一为 LLM Wiki
*
*****************************************************************************/
using LuBan.AIAgent.Wiki;

namespace LubanAgentCore.Agents;

/// <summary>
/// LLM Wiki 工作区的 Agent 配置，启用 wiki 工具组维护知识库，开启每轮语义检索兜底注入并硬约束工具结果优先。
/// </summary>
/// <remarks>
/// 系统提示词与工具组可通过工作区 <c>llmwiki-config.json</c> 的 <c>agentProfile</c> 节点覆盖；
/// wiki 相关行为（开关、schema 路径、TopK、raw 回落）可通过同文件的 <c>wiki</c> 节点覆盖；
/// 检索工具行为（默认条数、单次返回字符上限）可通过 <c>retrieval</c> 节点覆盖。
/// 构造时会追加 <see cref="WikiDefaults.Schema"/> 及工作区自定义 schema 到系统提示词。
/// </remarks>
public class LlmWikiAgentProfile : AgentProfile
{
    private const string RetrievalHardRules = """
        ## 知识库问答硬约束
        1. 回答知识库相关问题前**必须先调用检索工具**：先用 `retrieval.searchCode` 搜工作区原始资料，必要时用 `wiki.search` 搜 wiki 页；不得跳过检索直接作答。
        2. 若本轮已有检索工具的结果，**以工具结果为准**；自动注入的「工作区语义检索上下文」仅作兜底参考，两者冲突时以工具结果为准。
        3. 检索无命中且无自动注入内容时，明确说明「知识库中未找到依据」，**不得凭记忆作答**；不得编造来源或页码。
        """;

    private readonly WorkspaceInfo _workspace;
    private string _systemPrompt;
    private string[]? _toolGroups;
    private WikiConfig _wiki = new();
    private RetrievalConfig _retrieval = new();

    /// <summary>
    /// 创建 LlmWikiAgentProfile 实例，并加载工作区的 LLM Wiki 配置。
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    public LlmWikiAgentProfile(WorkspaceInfo workspace)
    {
        _workspace = workspace;
        _systemPrompt = "你是一个 LLM Wiki 知识库问答专家，维护并基于 wiki 知识库回答问题。优先使用 wiki 工具检索与沉淀知识；wiki 无相关内容时再回落原始资料。";
        _toolGroups = new[] { "wiki", "retrieval", "filesystem", "context", "log" };
        LoadLlmWikiConfig();
        _systemPrompt = $"{_systemPrompt}\n\n{WikiDefaults.Schema}";
        if (_wiki.SchemaPath != null)
        {
            var schemaFile = Path.Combine(_workspace.RootPath, _wiki.SchemaPath);
            if (File.Exists(schemaFile))
                _systemPrompt = $"{_systemPrompt}\n\n{File.ReadAllText(schemaFile)}";
        }
        _systemPrompt = $"{_systemPrompt}\n\n{RetrievalHardRules}";
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
    /// 检索模式：auto 启用每轮语义检索注入兜底；提示词同时硬约束「工具结果优先」。
    /// </summary>
    public override string? RetrievalMode => "auto";

    /// <inheritdoc/>
    protected override ToolGroupOptions BuildToolOptions() => new()
    {
        Browser = { Headless = false },
        Script = { DefaultTimeout = 30000 },
        Retrieval = { DefaultTopK = _retrieval.DefaultTopK, MaxResultChars = _retrieval.MaxResultChars },
        Wiki = { Enabled = _wiki.Enabled, TopK = _wiki.TopK, IncludeRawDefault = _wiki.IncludeRaw }
    };

    /// <summary>
    /// 从工作区的 llmwiki-config.json 加载 agentProfile 配置，覆盖默认的系统提示词与工具组。
    /// </summary>
    private void LoadLlmWikiConfig()
    {
        if (_workspace.ConfigPath == null) return;

        var configPath = Path.Combine(_workspace.RootPath, _workspace.ConfigPath, "llmwiki-config.json");
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
            if (doc.RootElement.TryGetProperty("wiki", out var wiki))
            {
                if (wiki.TryGetProperty("enabled", out var en) && en.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    _wiki.Enabled = en.GetBoolean();
                if (wiki.TryGetProperty("schemaPath", out var sp2))
                    _wiki.SchemaPath = sp2.GetString();
                if (wiki.TryGetProperty("topK", out var tk) && tk.TryGetInt32(out var topK))
                    _wiki.TopK = topK;
                if (wiki.TryGetProperty("includeRaw", out var ir) && ir.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    _wiki.IncludeRaw = ir.GetBoolean();
            }
            if (doc.RootElement.TryGetProperty("retrieval", out var retrieval))
            {
                if (retrieval.TryGetProperty("defaultTopK", out var dk) && dk.TryGetInt32(out var defaultTopK) && defaultTopK > 0)
                    _retrieval.DefaultTopK = defaultTopK;
                if (retrieval.TryGetProperty("maxResultChars", out var mc) && mc.TryGetInt32(out var maxResultChars) && maxResultChars > 0)
                    _retrieval.MaxResultChars = maxResultChars;
            }
            // wiki 组默认纳入；显式 wiki.enabled=false 可关闭；toolGroups 显式列出时其内容优先，但 wiki 组仍自动补齐。
            if (_wiki.Enabled && _toolGroups != null && !_toolGroups.Contains("wiki", StringComparer.OrdinalIgnoreCase))
                _toolGroups = _toolGroups.Append("wiki").ToArray();
        }
        catch
        {
            // 忽略 llmwiki-config.json 解析失败，沿用默认配置
        }
    }
}

/// <summary>llmwiki-config.json 的 wiki 节点。</summary>
internal sealed class WikiConfig
{
    /// <summary>是否启用 wiki 工具组。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>相对工作区根的 schema 路径。</summary>
    public string? SchemaPath { get; set; } = "wiki/SCHEMA.md";

    /// <summary>默认搜索返回条数。</summary>
    public int TopK { get; set; } = 8;

    /// <summary>搜索是否默认回落 raw（默认开启，避免 wiki 未建页时零召回）。</summary>
    public bool IncludeRaw { get; set; } = true;
}

/// <summary>llmwiki-config.json 的 retrieval 节点（映射到检索工具组选项）。</summary>
internal sealed class RetrievalConfig
{
    /// <summary>检索工具默认返回条数。</summary>
    public int DefaultTopK { get; set; } = 5;

    /// <summary>单次检索返回内容字符上限。</summary>
    public int MaxResultChars { get; set; } = 8000;
}