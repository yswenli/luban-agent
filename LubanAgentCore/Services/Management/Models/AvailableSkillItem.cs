/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： AvailableSkillItem
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/23
*描述：当前会话可用技能（运行期合并视图，只含启用项）。
*
*=================================================
*修改标记
*修改时间：2026/11/23
*修改人： yswenli
*版本号： V1.0.0.0
*描述：当前会话可用技能（运行期合并视图，只含启用项）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management.Models;

/// <summary>
/// 当前会话可用技能（<see cref="LuBan.AIAgent.Skills.SkillRegistry.GetAll()"/> 的运行期合并视图）。
/// </summary>
/// <remarks>
/// 与设置页的 <see cref="SkillListItem"/> 不同：本模型只包含「已启用」项
/// （已禁用的内置 / 自定义技能不会出现），且包含用户级与工作区级文件技能，
/// 与系统提示词里的技能目录、<c>ListSkills</c> / <c>UseSkill</c> 工具完全同源。
/// </remarks>
/// <param name="Id">技能标识（输入 @ 后插入的文本）。</param>
/// <param name="Name">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="Category">分类。</param>
/// <param name="IsBuiltin">是否内置技能。</param>
public sealed record AvailableSkillItem(
    string Id,
    string Name,
    string Description,
    string Category,
    bool IsBuiltin);