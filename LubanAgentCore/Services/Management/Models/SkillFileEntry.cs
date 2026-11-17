/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： SkillFileEntry
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/12
*描述：文件型 Skill 的列表项（目录名与显示名分离）与 ZIP 导入结果。
*
*=================================================
*修改标记
*修改时间：2026/11/12
*修改人： yswenli
*版本号： V1.0.0.0
*描述：文件型 Skill 的列表项（目录名与显示名分离）与 ZIP 导入结果。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management.Models;

/// <summary>
/// 文件型 Skill 的列表项：目录名（<see cref="Id"/>）与 frontmatter 里的显示名（<see cref="Name"/>）是两回事。
/// </summary>
/// <param name="Id">目录名，作为接口与删除操作的键。</param>
/// <param name="Name">frontmatter 的 name；缺失时回落为目录名。</param>
/// <param name="Category">frontmatter 的 category；缺失时为空串。</param>
public sealed record SkillFileEntry(string Id, string Name, string Category);

/// <summary>
/// ZIP 导入结果。
/// </summary>
/// <param name="Imported">成功写入的目录名（同名目录以新导入内容覆盖）。</param>
/// <param name="Skipped">命中技能结构但被拒的条目（目录名非法、SKILL.md 超限）。</param>
/// <param name="Ignored">未导入的非 SKILL.md 文件（伴随脚本 / 文档等，当前不落盘）。</param>
public sealed record SkillImportResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Ignored);