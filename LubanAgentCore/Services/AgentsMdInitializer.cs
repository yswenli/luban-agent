/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： AgentsMdInitializer
*版本号： V1.0.0.0
*唯一标识：AgentsMd 初始化服务
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/17
*描述：工作区 AGENTS.md 初始化服务：提示词装配、旧文件读取、结果规范化、落盘与校验提示，CLI/GUI 共用
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
    /// 规范化模型输出：剥离外层代码围栏、统一换行、补尾部换行，得到可落盘的 Markdown
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
    private readonly SkillRegistry _skillRegistry;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="skillRegistry">Skill 注册表</param>
    public AgentsMdInitializer(SkillRegistry skillRegistry)
    {
        _skillRegistry = skillRegistry;
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

        var skill = _skillRegistry.Get(SkillId)
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

        return text.Length == 0 ? string.Empty : text + "\n";
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

1. 逐章核对是否包含 full 模式 10 个章节：Metadata、角色定位、业务场景、能力清单、工具与API、权限与授权、禁止行为、异常处理、输入输出规约、协作与调度接口。
2. 若为 lite 模式，核对 4 个章节：Metadata、能力清单、工具与API、边界（含禁止行为与异常处理）。
3. 检查「能力清单」「工具与API」是否为 Markdown 表格。
4. 检查「禁止行为」「异常处理」在 full 模式下是否独立成章。
5. 检查所有命令、路径、工具名是否与工作区实际内容一致，是否存在编造。
6. 列出缺失或空泛的章节，并给出补齐后的内容。";
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