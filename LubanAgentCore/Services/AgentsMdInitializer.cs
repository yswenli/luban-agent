/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： AgentsMdInitializer
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/17
*描述：工作区 AGENTS.md 初始化服务：提示词装配、旧文件读取、结果规范化（补「Skill 流程遵守」与「协作约定」固定章节）、落盘与校验提示，CLI/GUI 共用
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// AGENTS.md 生成选项，对应内置 Skill agents-md-generator 的输入参数
/// </summary>
public sealed record AgentsMdOptions
{
    /// <summary>
    /// Agent 名称（必填）
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>
    /// 业务场景（必填）
    /// </summary>
    public string? AgentScene { get; init; }

    /// <summary>
    /// 能力清单（选填）
    /// </summary>
    public IReadOnlyList<string>? Capabilities { get; init; }

    /// <summary>
    /// 工具/API 清单（选填）
    /// </summary>
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// 输出模式：full | lite，默认 full
    /// </summary>
    public string OutputMode { get; init; } = "full";

    /// <summary>
    /// 自由描述（CLI 透传文本、GUI 表单附加说明）
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 工作区已存在的 AGENTS.md 全文，用于让模型合并更新
    /// </summary>
    public string? ExistingContent { get; init; }
}

/// <summary>
/// 工作区 AGENTS.md 初始化接口：宿主无关部分（提示词装配、文件读写、结果规范化、校验提示）
/// </summary>
public interface IAgentsMdInitializer
{
    /// <summary>
    /// 依赖的内置 Skill 标识
    /// </summary>
    string SkillId { get; }

    /// <summary>
    /// 目标文件名
    /// </summary>
    string FileName { get; }

    /// <summary>
    /// 获取工作区 AGENTS.md 的绝对路径
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    /// <returns>AGENTS.md 绝对路径</returns>
    string GetTargetPath(WorkspaceInfo workspace);

    /// <summary>
    /// 读取工作区已存在的 AGENTS.md 内容
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    /// <returns>文件内容；不存在或读取失败时返回 null</returns>
    string? ReadExisting(WorkspaceInfo workspace);

    /// <summary>
    /// 渲染 Skill 提示词模板，返回可直接交给 Agent 的完整提示词
    /// </summary>
    /// <param name="options">生成选项</param>
    /// <returns>完整提示词</returns>
    string BuildPrompt(AgentsMdOptions options);

    /// <summary>
    /// 规范化模型输出：剥离外层代码围栏、统一换行、补「Skill 流程遵守」与「协作约定」两个固定章节、补尾部换行，得到可落盘的 Markdown
    /// </summary>
    /// <param name="llmOutput">模型原始输出</param>
    /// <returns>规范化后的 Markdown；无有效内容时返回空字符串</returns>
    string Normalize(string llmOutput);

    /// <summary>
    /// 把 Markdown 写入工作区根目录的 AGENTS.md
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    /// <param name="markdown">已规范化的 Markdown</param>
    /// <param name="ct">取消令牌</param>
    Task WriteAsync(WorkspaceInfo workspace, string markdown, CancellationToken ct = default);

    /// <summary>
    /// 生成用于校验 AGENTS.md 完整性的提示词，供聊天区追加显示、用户复制复用
    /// </summary>
    /// <param name="targetPath">AGENTS.md 绝对路径</param>
    /// <returns>校验提示词</returns>
    string BuildVerificationPrompt(string targetPath);
}

/// <summary>
/// 工作区 AGENTS.md 初始化服务实现。
/// 只负责宿主无关部分，模型调用由各宿主自行完成（CLI 走自建容器、GUI 走聊天流）。
/// </summary>
public class AgentsMdInitializer : IAgentsMdInitializer
{
    private readonly BuiltinSkillCatalog _skillCatalog;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="skillCatalog">内置 Skill 目录（单例；仅读取内置 agents-md-generator）</param>
    public AgentsMdInitializer(BuiltinSkillCatalog skillCatalog)
    {
        _skillCatalog = skillCatalog;
    }

    /// <inheritdoc/>
    public string SkillId => "agents-md-generator";

    /// <inheritdoc/>
    public string FileName => "AGENTS.md";

    /// <inheritdoc/>
    public string GetTargetPath(WorkspaceInfo workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return Path.Combine(workspace.RootPath, FileName);
    }

    /// <inheritdoc/>
    public string? ReadExisting(WorkspaceInfo workspace)
    {
        try
        {
            var path = GetTargetPath(workspace);
            if (!File.Exists(path)) return null;
            var content = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取已存在的 AGENTS.md 失败: {workspace.RootPath}", ex);
            return null;
        }
    }

    /// <inheritdoc/>
    public string BuildPrompt(AgentsMdOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var skill = _skillCatalog.Get(SkillId)
            ?? throw new InvalidOperationException($"内置 Skill {SkillId} 未注册");

        var input = BuildSkillInput(options);
        var template = skill.PromptTemplate;
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException($"内置 Skill {SkillId} 未提供提示词模板");
        }

        return template.Contains("{input}")
            ? template.Replace("{input}", input)
            : $"{template}\n\n{input}";
    }

    /// <inheritdoc/>
    public string Normalize(string llmOutput)
    {
        if (string.IsNullOrWhiteSpace(llmOutput)) return string.Empty;

        var text = llmOutput.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
            {
                text = text[(firstNewline + 1)..lastFence].Trim();
            }
        }

        if (text.Length == 0) return string.Empty;

        text = EnsureSkillDirective(text);
        text = EnsureCollabConvention(text);
        return text + "\n";
    }

    /// <summary>
    /// 固定章节标题：流程型 Skill 的遵守约定。模型输出缺失时由本服务确定性补齐。
    /// </summary>
    public const string SkillDirectiveHeading = "## Skill 流程遵守";

    /// <summary>
    /// 固定章节正文：用户调用流程型 Skill 时必须严格按其步骤执行。
    /// </summary>
    public const string SkillDirectiveContent =
        "当用户调用 `brainstorming`、`grilling`（grill-me）等流程型 skill 时，必须严格按 skill 指示的步骤与顺序执行，" +
        "不得擅自增删步骤、跳过环节或改写流程；与「方案确认」等通用协作规则冲突时，以 skill 的流程为准。";

    /// <summary>
    /// 固定章节标题：协作约定。模型输出缺失时由本服务确定性补齐。
    /// </summary>
    public const string CollabConventionHeading = "## 协作约定（优先于助手默认行为）";

    /// <summary>
    /// 固定章节正文：协作约定条目。逐条列出中文交流、证据可核、任务范围、包版本、方案比选等通用规则。
    /// </summary>
    public const string CollabConventionContent =
        "- 全程中文；结论先行，再给一句话依据。回复精简但不省略依据与风险。\n" +
        "- 位置以「文件 + 符号/章节」为主锚，`文件:行号` 为辅；编辑后重新核对行号再引用。\n" +
        "- 有上游设计依据时给出处（如 `docs/superpowers/specs/<文件>.md` 的章节）；没有就写「无上游设计依据」，不编造。\n" +
        "- 代码与注释不写「见 PRD §X」「按设计 vX」这类溯源标记，规则直接写完整；跨文件引用用「文件名 + 主题」。\n" +
        "- 严格按任务范围执行，不顺手改无关文件或重构；范围外问题先报告，然后继续原任务。必要的连带改动（接口调用方、README 中英同步）不算扩面。\n" +
        "- 改动代码后不得自行修改项目的包版本（`.csproj` 的 `<Version>`），也不得增删、升级或降级 `PackageReference` 版本；包版本管理由用户处理，除非当次任务明确要求。\n" +
        "- 多文件改动、对外行为变更、不可逆操作或存在多种合理实现时：先给 2–3 个方案 + 权衡，暂停等明确许可再动手；中途失效回到比选，不自行换方案。\n" +
        "- 不执行 `git push`、`reset --hard`、`clean`、改写已有提交，除非当次点名。\n" +
        "- 凭据不写入代码/注释/文档/日志/提交；文档只写位置。\n" +
        "- 未实际运行的构建/测试不得声称通过，报告附关键输出或退出码；环境缺失导致的失败写「环境原因」，只做静态核对写「未运行」。\n" +
        "- 具体执行类任务（落码、代码审查、测试补齐、批量文档同步等）一律交给子代理完成；主会话只做需求澄清、方案比选、任务拆解、委派与验收，不亲自改代码。子代理返回后由主会话复核改动范围与构建/测试证据再下结论，不直接采信其自述。\n" +
        "- 流程型 skill（brainstorming / grilling 等）只让渡「步骤顺序」，不因 skill 放宽范围与证据要求。";

    /// <summary>
    /// 确保正文包含「Skill 流程遵守」固定章节；已存在时原样返回，避免重复初始化造成重复追加。
    /// </summary>
    /// <param name="markdown">已剥离围栏、统一换行的 Markdown 正文</param>
    /// <returns>补章后的 Markdown 正文</returns>
    private static string EnsureSkillDirective(string markdown)
    {
        // 模型可能已按提示词生成该章节，标题或关键措辞任一命中即视为已存在，保持幂等
        if (markdown.Contains(SkillDirectiveHeading, StringComparison.Ordinal)
            || markdown.Contains("流程型 skill", StringComparison.OrdinalIgnoreCase))
        {
            return markdown;
        }

        return $"{markdown}\n\n{SkillDirectiveHeading}\n\n{SkillDirectiveContent}";
    }

    /// <summary>
    /// 确保正文包含「协作约定」固定章节；已存在时原样返回，避免重复初始化造成重复追加。
    /// </summary>
    /// <param name="markdown">已剥离围栏、统一换行的 Markdown 正文</param>
    /// <returns>补章后的 Markdown 正文</returns>
    private static string EnsureCollabConvention(string markdown)
    {
        // 模型可能已按提示词生成该章节，标题命中即视为已存在，保持幂等
        if (markdown.Contains("## 协作约定", StringComparison.Ordinal))
        {
            return markdown;
        }

        return $"{markdown}\n\n{CollabConventionHeading}\n\n{CollabConventionContent}";
    }

    /// <inheritdoc/>
    public async Task WriteAsync(WorkspaceInfo workspace, string markdown, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var path = GetTargetPath(workspace);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(path, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
    }

    /// <inheritdoc/>
    public string BuildVerificationPrompt(string targetPath)
    {
        return $@"请校验 {targetPath} 的完整性：

1. 逐章核对是否包含 full 模式 10 个章节：Metadata、角色定位、业务场景、能力清单、工具与API、权限与授权、禁止行为、异常处理、输入输出规约、协作与调度接口（末尾另须有固定章节，见第 6、7 条）。
2. 若为 lite 模式，核对 4 个章节：Metadata、能力清单、工具与API、边界（含禁止行为与异常处理）。
3. 检查「能力清单」「工具与API」是否为 Markdown 表格。
4. 检查「禁止行为」「异常处理」在 full 模式下是否独立成章。
5. 检查所有命令、路径、工具名是否与工作区实际内容一致，是否存在编造。
6. 检查是否包含「Skill 流程遵守」章节：须写明调用 brainstorming、grilling（grill-me）等流程型 skill 时严格按其步骤与顺序执行，不得擅自增删步骤、跳过环节或改写流程。
7. 检查是否包含「协作约定（优先于助手默认行为）」章节：须写明全程中文、结论先行、依据可核（文件 + 符号为主锚、行号为辅、无依据时明写「无上游设计依据」）、任务范围约束、包版本约束（不得自行改 `<Version>` 或增删升降 `PackageReference`）、方案比选与暂停确认、禁止破坏性 git 操作、凭据不入库、未运行不得声称通过、执行类任务交子代理。
8. 列出缺失或空泛的章节，并给出补齐后的内容。";
    }

    /// <summary>
    /// 把生成选项序列化为 Skill 输入（key: value 形式，空字段省略交由模型从工作区推断）
    /// </summary>
    /// <param name="options">生成选项</param>
    /// <returns>Skill 输入文本</returns>
    private static string BuildSkillInput(AgentsMdOptions options)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(options.AgentName))
            sb.AppendLine($"agent_name: {options.AgentName.Trim()}");

        if (!string.IsNullOrWhiteSpace(options.AgentScene))
            sb.AppendLine($"agent_scene: {options.AgentScene.Trim()}");

        var capabilities = JoinList(options.Capabilities);
        if (capabilities != null)
            sb.AppendLine($"capabilities: {capabilities}");

        var tools = JoinList(options.Tools);
        if (tools != null)
            sb.AppendLine($"tools: {tools}");

        if (!string.IsNullOrWhiteSpace(options.OutputMode))
            sb.AppendLine($"output_mode: {options.OutputMode.Trim().ToLowerInvariant()}");

        if (!string.IsNullOrWhiteSpace(options.Description))
            sb.AppendLine($"description: {options.Description.Trim()}");

        if (!string.IsNullOrWhiteSpace(options.ExistingContent))
        {
            sb.AppendLine("existing_agents_md: |");
            foreach (var line in options.ExistingContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                sb.AppendLine($"  {line}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 拼接字符串列表，过滤空白项
    /// </summary>
    /// <param name="values">字符串列表</param>
    /// <returns>逗号分隔文本；无有效项时返回 null</returns>
    private static string? JoinList(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0) return null;

        var items = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToList();

        return items.Count == 0 ? null : string.Join(", ", items);
    }
}