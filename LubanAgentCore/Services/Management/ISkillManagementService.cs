/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： ISkillManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：Skill 管理（内置 + 文件型 + config 自定义）。 方法面 = 现有 <c>/skill</c> 命令与 Codex 设置页 Skill 页的能力，不新增能力。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：Skill 管理（内置 + 文件型 + config 自定义）。 方法面 = 现有 <c>/skill</c> 命令与 Codex 设置页 Skill 页的能力，不新增能力。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// Skill 管理（内置 + 文件型 + config 自定义）。
/// 方法面 = 现有 <c>/skill</c> 命令与 Codex 设置页 Skill 页的能力，不新增能力。
/// </summary>
/// <remarks>
/// 三个来源对应三个写回后端：
/// <list type="bullet">
/// <item><see cref="ManagedOrigin.Builtin"/>：硬编码，只可启停（写 <c>DisabledBuiltinSkills</c>）。</item>
/// <item><see cref="ManagedOrigin.File"/>：<c>SKILL.md</c> 文件（全局 <c>~/.luban-agent/skills</c> 或工作区 <c>.luban-agent/skills</c>）。</item>
/// <item><see cref="ManagedOrigin.Config"/>：config.json 的 <c>CustomSkills</c>，只可启停（文件型无法在此启停）。</item>
/// </list>
/// 文件后端方法为同步方法：调用方（Codex 设置页）本就跑在 UI 线程的同步文件 IO 上，返回 Task 只会引出
/// <c>GetAwaiter().GetResult()</c> 或死锁风险，故如实声明为同步。
/// </remarks>
public interface ISkillManagementService
{
    /// <summary>三个来源合并后的 Skill 列表（含「已禁用的内置 / 自定义」条目）。</summary>
    Task<IReadOnlyList<SkillListItem>> ListAsync(CancellationToken ct = default);

    /// <summary>新增 config 自定义 Skill；Id 重复或模板为空时抛异常（消息面向用户）。</summary>
    Task AddAsync(CustomSkillConfig skill, CancellationToken ct = default);

    /// <summary>更新 config 自定义 Skill 的可编辑字段（名称/描述/分类/模板/示例）。</summary>
    Task UpdateAsync(CustomSkillConfig skill, CancellationToken ct = default);

    /// <summary>按来源删除（内置不可删；<paramref name="workspaceRoot"/> 为 null 表示全局目录）。</summary>
    Task DeleteAsync(string id, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default);

    /// <summary>启用/停用（内置走禁用表，自定义走配置项；文件型不支持）。</summary>
    Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default);

    /// <summary>列出文件型 Skill Id（目录名），按名称排序。</summary>
    IReadOnlyList<string> ListFileIds(string? workspaceRoot);

    /// <summary>读取文件型 Skill；不存在时返回 null。</summary>
    SkillFileContent? ReadFile(string id, string? workspaceRoot);

    /// <summary>
    /// 写入文件型 Skill。<paramref name="originalId"/> 与 <see cref="SkillFileContent.Name"/> 不同时按改名处理
    /// （移动目录，目标已存在则抛异常）。
    /// </summary>
    void SaveFile(string? originalId, SkillFileContent content, string? workspaceRoot);
}