namespace LubanAgentCore.Services.Management.Models;

/// <summary>
/// <c>SKILL.md</c> 的可编辑字段（frontmatter 的 name/description/category/triggers + 正文）。
/// </summary>
/// <param name="Name">name（必填，同时作为目录名）。</param>
/// <param name="Description">description（必填）。</param>
/// <param name="Category">category（可空）。</param>
/// <param name="Triggers">triggers（逗号分隔，可空）。</param>
/// <param name="Body">正文 Markdown。</param>
public sealed record SkillFileContent(
    string Name,
    string Description,
    string Category,
    string Triggers,
    string Body);