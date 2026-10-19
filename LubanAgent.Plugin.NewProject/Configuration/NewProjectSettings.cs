/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Configuration
*文件名： NewProjectSettings
*创建时间：2026/10/16
*描述：插件配置（模板对照 / 参考流水线 / 命名与分支约定），包内默认值 + 宿主可覆盖
*****************************************************************************/

using System.Reflection;
using System.Text.Json;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Serialization;

namespace LubanAgent.Plugin.NewProject.Configuration;

/// <summary>
/// 模板仓绑定（PRD §4.5「模板对照」）。
/// </summary>
public sealed class TemplateBinding
{
    /// <summary>所属端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>模板仓名（<c>TJCES-Service</c> / <c>TJCES-Admin</c> / <c>TJCES-Mobile</c>）。</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// 模板 pin 的<b>可选覆盖</b>（分支 / 标签）。
    /// </summary>
    /// <remarks>
    /// <b>留空＝运行期实时解析</b>：在模板仓的现有分支里按 <c>develop → master → main → 模板默认分支</c>
    /// 顺序取第一个存在的分支作为 pin，并把「取到了哪个、为什么」回显在 Step 3 预检里。
    /// 登记了值则<b>必须</b>真实存在，否则阻断（不静默回退，避免用错版本建库）。
    /// </remarks>
    public string Branch { get; set; } = string.Empty;

    /// <summary>
    /// 模板仓 ID（可选覆盖）：登记后 Step 3 在模板库列表中按该 ID 精确定位；
    /// 留空则按 <see cref="Repository"/> 名匹配（模板仓改名只需改名字，无需同步改 ID）。
    /// </summary>
    /// <remarks>云效侧是数字 ID，此处用字符串承载是为了允许配置里留空；解析失败即视为未登记。</remarks>
    public string? ProjectId { get; set; }
}

/// <summary>
/// 参考流水线绑定（PRD §4.5「参考流水线清单」，Step 7 的克隆来源）。
/// </summary>
public sealed class PipelineReference
{
    /// <summary>所属端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>企服版参考流水线标识（M6 联调时填入真实值）。</summary>
    public string PipelineId { get; set; } = string.Empty;
}

/// <summary>
/// 占位符替换规则（F3=A，PRD §4.3 Step 3「随后自动替换占位符：命名空间、项目名、缩写」）。
/// </summary>
/// <remarks>
/// <para>
/// 按<b>字面量、区分大小写</b>替换；模板里的写法大小写各异（如 <c>Tjces.Service</c> 的命名空间前缀
/// 与 <c>TJCES-Service</c> 的仓库名），故默认给三条大小写变体，而不是做「忽略大小写」的模糊匹配
/// （模糊匹配会把 <c>tjces</c> 意外改写进 URL、注释与第三方标识里）。
/// </para>
/// <para>
/// <see cref="To"/> 支持令牌：<c>{code}</c>（缩写原文）/ <c>{Code}</c>（首字母大写）/ <c>{CODE}</c>（全大写）/
/// <c>{name}</c>（项目中文名）。规则由插件配置维护，首次真机联调后按模板实际结构调整。
/// </para>
/// </remarks>
public sealed class PlaceholderRule
{
    /// <summary>被替换的字面量（区分大小写）。</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>替换目标（支持 <c>{code}</c> / <c>{Code}</c> / <c>{CODE}</c> / <c>{name}</c> 令牌）。</summary>
    public string To { get; set; } = string.Empty;
}

/// <summary>
/// 插件配置：模板对照、参考流水线、仓库命名与推送分支约定。
/// </summary>
/// <remarks>
/// <para>
/// 取值顺序（PRD §4.5 的「来自插件配置」）：
/// ① 包内 <c>newproject.settings.json</c> 默认值（随插件包分发，现场改不需要重发插件时可用）
/// ② 宿主配置节点覆盖（<c>IPluginHost.GetConfig</c>，P2 起接入）。
/// </para>
/// <para>
/// 该文件允许 <c>//</c> 行注释，便于就地标注「待 M6 真机联调填入」的占位项。
/// </para>
/// <para>
/// 文件中的「端」写**字符串枚举**（如 <c>"Service"</c>），由
/// <see cref="Serialization.PluginJsonContext"/> 的 <c>UseStringEnumConverter</c> 承载；
/// 读写一律经该上下文（源生成），不得使用反射式 <c>JsonSerializer</c>（见其注释中的 ALC 硬约束）。
/// </para>
/// </remarks>
public sealed class NewProjectSettings
{
    /// <summary>默认可见性（PRD §4.5：默认＝组织内公开）。</summary>
    public string DefaultVisibility { get; set; } = "internal";

    /// <summary>Step 6 统一推送的分支（PRD §6 #17）。</summary>
    public string PushBranch { get; set; } = "main";

    /// <summary>目标仓库名格式，占位符 <c>{code}</c> / <c>{end}</c>。</summary>
    public string RepositoryNameFormat { get; set; } = "{code}-{end}";

    /// <summary>
    /// 项目代码组名格式（运行期自动建组）：占位符 <c>{code}</c> / <c>{Code}</c> / <c>{CODE}</c> / <c>{name}</c>，
    /// 默认 <c>{CODE}</c>（如缩写 <c>jnms</c> → 组名 <c>JNMS</c>，与模板组 <c>TJCES</c> 的写法一致）。
    /// </summary>
    /// <remarks>
    /// 目标分组按该名字在组织内<b>先查后建</b>：命中即复用，未命中才创建（PRD §4.3 Step 3）。
    /// 组名同时用作云效 <c>path</c>，故不要包含 <c>/</c> 等路径分隔符。
    /// </remarks>
    public string GroupNameFormat { get; set; } = "{CODE}";

    /// <summary>
    /// 项目代码组的父级组 ID（可选）：留空＝直接建在组织根下（云效文档语义：<c>parentId</c> 空即在组织路径下创建）。
    /// </summary>
    /// <remarks>云效侧是数字 ID，此处用字符串承载是为了允许配置里留空；解析失败即视为未登记。</remarks>
    public string? GroupParentId { get; set; }

    /// <summary>
    /// Step 6 提交身份兜底（F5=A）：本机 <c>git config user.name/user.email</c> 缺失时使用，
    /// 避免在未配置 git 身份的机器上推送直接失败。
    /// </summary>
    public string CommitAuthorName { get; set; } = "LuBan Agent";

    /// <summary>Step 6 提交邮箱兜底（与 <see cref="CommitAuthorName"/> 配套）。</summary>
    public string CommitAuthorEmail { get; set; } = "luban-agent@local";

    /// <summary>占位符替换规则（F3=A）；为空时使用内置默认（三端模板缩写的大小写变体）。</summary>
    public List<PlaceholderRule> PlaceholderRules { get; set; } = new();

    /// <summary>模板对照。</summary>
    public List<TemplateBinding> Templates { get; set; } = new();

    /// <summary>参考流水线清单。</summary>
    public List<PipelineReference> PipelineReferences { get; set; } = new();

    /// <summary>
    /// 包内配置文件的绝对路径（与插件包中 <c>lib/</c> 同级）。
    /// </summary>
    public static string DefaultFilePath
    {
        get
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(assemblyDir, "..", "newproject.settings.json"));
        }
    }

    /// <summary>
    /// 读取包内默认配置；文件缺失或损坏时退回内置默认值（不抛异常，避免拖垮插件初始化）。
    /// </summary>
    /// <returns>配置实例。</returns>
    public static NewProjectSettings LoadDefault()
    {
        try
        {
            var path = DefaultFilePath;
            if (!File.Exists(path))
            {
                Logger.Warn($"[newproject] 未找到包内配置 {path}，改用内置默认值。");
                return CreateBuiltIn();
            }

            var settings = JsonSerializer.Deserialize(File.ReadAllText(path), PluginJsonContext.Default.NewProjectSettings);
            return settings ?? CreateBuiltIn();
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 读取包内配置失败，改用内置默认值。", ex);
            return CreateBuiltIn();
        }
    }

    /// <summary>
    /// 按端取模板绑定。
    /// </summary>
    /// <param name="end">端。</param>
    /// <returns>绑定；未配置时返回 <c>null</c>。</returns>
    public TemplateBinding? FindTemplate(ProjectEnd end)
        => Templates.FirstOrDefault(item => item.End == end);

    /// <summary>
    /// 目标仓库名：<c>{code}-{end}</c>（如 <c>jnms-Service</c>）。
    /// </summary>
    /// <param name="code">项目英文缩写。</param>
    /// <param name="end">端。</param>
    /// <returns>仓库名。</returns>
    public string BuildRepositoryName(string code, ProjectEnd end)
        => RepositoryNameFormat
            .Replace("{code}", code, StringComparison.OrdinalIgnoreCase)
            .Replace("{end}", EndSuffix(end), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 目标代码组名（运行期自动建组的名字）：按 <see cref="GroupNameFormat"/> 展开令牌。
    /// </summary>
    /// <param name="code">项目英文缩写。</param>
    /// <param name="name">项目中文名。</param>
    /// <returns>组名（格式为空或展开结果为空时退回缩写的全大写形式）。</returns>
    public string BuildGroupName(string code, string name)
    {
        var format = string.IsNullOrWhiteSpace(GroupNameFormat) ? "{CODE}" : GroupNameFormat;
        var expanded = ExpandTokens(format, code, name).Trim();
        return expanded.Length > 0 ? expanded : (code ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>
    /// 解析 <see cref="GroupParentId"/>（留空 / 非数字即视为「组织根」）。
    /// </summary>
    /// <returns>父级组 ID；未登记时为 <c>null</c>。</returns>
    public long? TryGetGroupParentId()
        => long.TryParse(GroupParentId?.Trim(), out var parsed) && parsed > 0 ? parsed : null;

    /// <summary>
    /// 端的英文后缀（Service / Admin / Mobile）。
    /// </summary>
    /// <param name="end">端。</param>
    /// <returns>后缀。</returns>
    public static string EndSuffix(ProjectEnd end) => end.ToString();

    /// <summary>
    /// 展开占位符令牌：<c>{code}</c> / <c>{Code}</c> / <c>{CODE}</c> / <c>{name}</c>。
    /// </summary>
    /// <param name="template">含令牌的文本。</param>
    /// <param name="code">项目英文缩写（小写字母开头）。</param>
    /// <param name="name">项目中文名。</param>
    /// <returns>展开后的文本。</returns>
    /// <remarks>
    /// 区分大小写：<c>{code}</c> 与 <c>{Code}</c> / <c>{CODE}</c> 互不匹配（令牌本身也是字面量）。
    /// </remarks>
    public static string ExpandTokens(string? template, string code, string name)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var capitalized = string.IsNullOrEmpty(code)
            ? string.Empty
            : char.ToUpperInvariant(code[0]) + code[1..];

        return template
            .Replace("{code}", code ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Code}", capitalized, StringComparison.Ordinal)
            .Replace("{CODE}", (code ?? string.Empty).ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("{name}", name ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// 生成本次项目的占位符替换表（已展开令牌、按 <c>From</c> 长度降序，避免短串先替换吃掉长串）。
    /// </summary>
    /// <param name="code">项目英文缩写。</param>
    /// <param name="name">项目中文名。</param>
    /// <returns>替换表（<c>From</c> 为空或 <c>To</c> 展开后为空的规则被丢弃）。</returns>
    public IReadOnlyList<KeyValuePair<string, string>> BuildReplacements(string code, string name)
    {
        var rules = PlaceholderRules.Count > 0 ? PlaceholderRules : CreateDefaultPlaceholderRules();
        var pairs = new List<KeyValuePair<string, string>>();

        foreach (var rule in rules)
        {
            var from = rule.From?.Trim() ?? string.Empty;
            if (from.Length == 0)
                continue;

            var to = ExpandTokens(rule.To, code, name);
            if (to.Length == 0 || string.Equals(from, to, StringComparison.Ordinal))
                continue;

            pairs.Add(new KeyValuePair<string, string>(from, to));
        }

        return pairs
            .OrderByDescending(pair => pair.Key.Length)
            .ToArray();
    }

    /// <summary>
    /// 内置默认替换规则（PRD §4.5 模板缩写 <c>TJCES</c> 的三种大小写写法）。
    /// </summary>
    /// <returns>规则列表。</returns>
    private static List<PlaceholderRule> CreateDefaultPlaceholderRules() => new()
    {
        new() { From = "TJCES", To = "{CODE}" },
        new() { From = "Tjces", To = "{Code}" },
        new() { From = "tjces", To = "{code}" }
    };

    /// <summary>
    /// 内置默认（PRD §4.5 模板对照固定值）。
    /// </summary>
    /// <returns>配置实例。</returns>
    private static NewProjectSettings CreateBuiltIn() => new()
    {
        Templates = new List<TemplateBinding>
        {
            new() { End = ProjectEnd.Service, Repository = "TJCES-Service" },
            new() { End = ProjectEnd.Admin, Repository = "TJCES-Admin" },
            new() { End = ProjectEnd.Mobile, Repository = "TJCES-Mobile" }
        },
        PipelineReferences = new List<PipelineReference>
        {
            new() { End = ProjectEnd.Service },
            new() { End = ProjectEnd.Admin },
            new() { End = ProjectEnd.Mobile }
        },
        PlaceholderRules = CreateDefaultPlaceholderRules()
    };
}