/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： SkillFileContent
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：<c>SKILL.md</c> 的可编辑字段（frontmatter 的 name/description/category/triggers + 正文）。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：<c>SKILL.md</c> 的可编辑字段（frontmatter 的 name/description/category/triggers + 正文）。
*
*****************************************************************************/

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