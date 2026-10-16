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

    /// <summary>模板 pin 的分支 / 标签（PRD §6 #5：不跟随模板升级，找不到即报错阻断）。</summary>
    public string Branch { get; set; } = string.Empty;
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
    /// 端的英文后缀（Service / Admin / Mobile）。
    /// </summary>
    /// <param name="end">端。</param>
    /// <returns>后缀。</returns>
    public static string EndSuffix(ProjectEnd end) => end.ToString();

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
        }
    };
}