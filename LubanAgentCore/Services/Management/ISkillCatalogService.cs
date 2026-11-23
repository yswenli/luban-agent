/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： ISkillCatalogService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/23
*描述：当前会话可用技能查询（三端 @ 技能候选的唯一数据源）。
*
*=================================================
*修改标记
*修改时间：2026/11/23
*修改人： yswenli
*版本号： V1.0.0.0
*描述：当前会话可用技能查询（三端 @ 技能候选的唯一数据源）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 当前会话可用技能查询。
/// </summary>
/// <remarks>
/// 数据源是会话作用域的 <see cref="LuBan.AIAgent.Skills.SkillRegistry"/>（不是设置页的
/// <see cref="ISkillManagementService"/>）：先按会话绑定的工作区执行一次
/// <c>LoadFromWorkspace</c>，再取合并视图。这样三端 <c>@</c> 弹层展示的集合，
/// 与系统提示词里的技能目录、<c>ListSkills</c> / <c>UseSkill</c> 工具保持一致：
/// 只含启用项，包含用户级、工作区级文件技能与插件贡献的技能。
/// </remarks>
public interface ISkillCatalogService
{
    /// <summary>
    /// 列出当前可用技能。
    /// </summary>
    /// <param name="sessionId">
    /// 会话标识；非空时按该会话绑定的工作区加载，为空或会话未绑定工作区时回退当前工作区。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已启用技能清单（按注册表合并顺序）。</returns>
    Task<IReadOnlyList<AvailableSkillItem>> ListAvailableAsync(string? sessionId = null, CancellationToken ct = default);
}