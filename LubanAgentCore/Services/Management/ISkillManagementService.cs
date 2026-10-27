using LuBan.AIAgent.Tools.Skill;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// Skill 管理（工作区自定义 + 内置）。
/// 方法面 = 现有 <c>/skill</c> 命令与 Codex 设置页的能力，不新增能力。
/// </summary>
public interface ISkillManagementService
{
    /// <summary>Skill 列表（含启用状态）。</summary>
    Task<IReadOnlyList<SkillInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>从源路径导入 Skill（目录或单个文件）。</summary>
    Task ImportAsync(string sourcePath, CancellationToken ct = default);

    /// <summary>删除自定义 Skill。</summary>
    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>启用/停用 Skill（内置走禁用表，自定义走配置项）。</summary>
    Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);
}