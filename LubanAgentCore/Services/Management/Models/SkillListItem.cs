/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： SkillListItem
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：Skill 展示信息（内置 + 文件型 + config 自定义的统一视图）。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：Skill 展示信息（内置 + 文件型 + config 自定义的统一视图）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management.Models;

/// <summary>
/// Skill 展示信息（内置 + 文件型 + config 自定义的统一视图）。
/// </summary>
/// <param name="Id">Skill 标识。</param>
/// <param name="Name">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="Category">分类（内置项的分类来自实现，文件型来自 frontmatter）。</param>
/// <param name="Examples">示例列表。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Origin">来源（决定写回哪个后端）。</param>
public sealed record SkillListItem(
    string Id,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> Examples,
    bool IsEnabled,
    ManagedOrigin Origin)
{
    /// <summary>是否硬编码内置项。</summary>
    public bool IsBuiltin => Origin == ManagedOrigin.Builtin;

    /// <summary>是否 config.json 自定义项。</summary>
    public bool IsCustom => Origin == ManagedOrigin.Config;
}