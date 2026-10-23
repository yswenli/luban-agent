/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Configuration
*文件名： NewProjectSettings
*创建时间：2026/10/16
*描述：插件配置（命名与分支约定），包内默认值 + 宿主可覆盖
*****************************************************************************/

using System.Reflection;
using System.Text.Json;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Serialization;

namespace LubanAgent.Plugin.NewProject.Configuration;

/// <summary>
/// 插件配置：仓库命名、代码组命名与推送分支约定（v0.33 起只留 UI 预填用的短格式值）。
/// </summary>
/// <remarks>
/// <para>
/// 取值顺序（PRD §4.5 的「来自插件配置」）：
/// ① 包内 <c>newproject.settings.json</c> 默认值（随插件包分发，现场改不需要重发插件时可用）
/// ② 宿主配置节点覆盖（<c>IPluginHost.GetConfig</c>，P2 起接入）。
/// </para>
/// <para>
/// <b>v0.33 瘦身口径</b>：组织专有值（模板仓对照 / 三端流水线默认值 / 参考流水线 id / 占位符替换规则 /
/// 代码组父级组 ID）全部迁出到随包分发的 <c>docs/</c>（见 <c>docs/org-reference.md</c>、
/// <c>docs/pipeline-conventions.md</c>），Step 3/7 由助手按那里的事实判定；本文件只留
/// 「建单前面板就要回显 / 预填的格式与兜底值」，不含任何组织敏感值。
/// </para>
/// <para>
/// 该文件允许 <c>//</c> 行注释，便于就地说明取值口径。
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

    /// <summary>Step 6 统一推送的分支（PRD §6 #17；v0.28 起默认 <c>develop</c>，与 Step 3 克隆 pin 的择优结果对齐）。</summary>
    /// <remarks>
    /// 该值同时用作云效建仓的默认分支（<c>DefaultBranch</c>），故 Step 3 与 Step 6 口径一致；
    /// 这里的默认值只是<b>兜底</b>——正常路径取包内 <c>newproject.settings.json</c>（同样是 <c>develop</c>），
    /// 仅在配置文件缺失/损坏时才会命中本默认值。留空则退回各端仓库默认分支（仍为空则 <c>main</c>）。
    /// </remarks>
    public string PushBranch { get; set; } = "develop";

    /// <summary>目标仓库名格式，占位符 <c>{code}</c> / <c>{end}</c>。</summary>
    public string RepositoryNameFormat { get; set; } = "{Code}-{end}";

    /// <summary>
    /// 项目代码组名格式（运行期由助手建组）：占位符 <c>{code}</c> / <c>{Code}</c> / <c>{CODE}</c> / <c>{name}</c>，
    /// 默认 <c>{CODE}</c>（如缩写 <c>jnms</c> → 组名 <c>JNMS</c>，与模板组 <c>TJCES</c> 的写法一致）。
    /// </summary>
    /// <remarks>
    /// 目标分组由助手在 Step 3 按该名字<b>先查后建</b>：命中即复用，未命中才创建；
    /// 组名同时用作云效 <c>path</c>，故不要包含 <c>/</c> 等路径分隔符。
    /// </remarks>
    public string GroupNameFormat { get; set; } = "{CODE}";

    /// <summary>
    /// Step 6 提交身份兜底（F5=A）：本机 <c>git config user.name/user.email</c> 缺失时使用，
    /// 避免在未配置 git 身份的机器上推送直接失败。
    /// </summary>
    public string CommitAuthorName { get; set; } = "LuBan Agent";

    /// <summary>Step 6 提交邮箱兜底（与 <see cref="CommitAuthorName"/> 配套）。</summary>
    public string CommitAuthorEmail { get; set; } = "luban-agent@local";

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
                return new NewProjectSettings();
            }

            var settings = JsonSerializer.Deserialize(File.ReadAllText(path), PluginJsonContext.Default.NewProjectSettings);
            return settings ?? new NewProjectSettings();
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 读取包内配置失败，改用内置默认值。", ex);
            return new NewProjectSettings();
        }
    }

    /// <summary>
    /// 目标仓库名：<c>{Code}-{end}</c>（如 <c>Csxmy-Service</c>——<b>首字母大写</b>，与代码组名的全大写区分）。
    /// </summary>
    /// <param name="code">项目英文缩写（小写字母）。</param>
    /// <param name="end">端。</param>
    /// <returns>仓库名（格式留空时退回 <c>{Code}-{end}</c>）。</returns>
    /// <remarks>
    /// 令牌<b>区分大小写</b>：<c>{code}</c>＝缩写原文 / <c>{Code}</c>＝首字母大写（默认）/ <c>{CODE}</c>＝全大写；
    /// 另支持 <c>{end}</c>＝Service / Admin / Mobile。
    /// </remarks>
    public string BuildRepositoryName(string code, ProjectEnd end)
    {
        var format = string.IsNullOrWhiteSpace(RepositoryNameFormat) ? "{Code}-{end}" : RepositoryNameFormat;
        return ExpandTokens(format, code, string.Empty)
            .Replace("{end}", EndSuffix(end), StringComparison.Ordinal);
    }

    /// <summary>
    /// 目标代码组名（助手建组的名字）：按 <see cref="GroupNameFormat"/> 展开令牌。
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
    /// 端的英文后缀（Service / Admin / Mobile）。
    /// </summary>
    /// <param name="end">端。</param>
    /// <returns>后缀。</returns>
    public static string EndSuffix(ProjectEnd end) => end.ToString();

    /// <summary>
    /// 展开占位符令牌：<c>{code}</c> / <c>{Code}</c> / <c>{CODE}</c> / <c>{name}</c>。
    /// </summary>
    /// <param name="template">含令牌的文本。</param>
    /// <param name="code">项目英文缩写（只允许小写字母 a-z）。</param>
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
}