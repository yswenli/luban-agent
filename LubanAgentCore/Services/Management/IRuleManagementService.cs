using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 规则管理（内置 + 自定义）。
/// 方法面 = 现有 <c>/rule</c> 命令与 Codex 设置页的能力，不新增能力。
/// </summary>
public interface IRuleManagementService
{
    /// <summary>规则列表（含启用状态）。</summary>
    Task<IReadOnlyList<RuleInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>新增自定义规则。</summary>
    Task AddAsync(string name, string content, CancellationToken ct = default);

    /// <summary>删除自定义规则。</summary>
    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>启用/停用规则（内置走禁用表，自定义走配置项）。</summary>
    Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);
}