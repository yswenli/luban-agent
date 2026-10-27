using LuBan.AIAgent.Configuration;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 规则管理（内置 + 文件型 + config 自定义）。
/// 方法面 = 现有 <c>/rule</c> 命令与 Codex 设置页 Rule 页的能力，不新增能力。
/// </summary>
/// <remarks>
/// 三个来源对应三个写回后端：
/// <list type="bullet">
/// <item><see cref="ManagedOrigin.Builtin"/>：硬编码，只可启停（写 <c>DisabledBuiltinRules</c>）。</item>
/// <item><see cref="ManagedOrigin.File"/>：<c>rules/&lt;id&gt;.json</c>（全局 <c>~/.luban-agent/rules</c> 或工作区 <c>.luban-agent/rules</c>）。</item>
/// <item><see cref="ManagedOrigin.Config"/>：config.json 的 <c>CustomRules</c>，只可启停（文件型无法在此启停）。</item>
/// </list>
/// 文件后端方法为同步方法（理由同 <see cref="ISkillManagementService"/>）。
/// </remarks>
public interface IRuleManagementService
{
    /// <summary>三个来源合并后的规则列表。</summary>
    Task<IReadOnlyList<RuleInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>新增 config 自定义规则；Id 重复时抛异常（消息面向用户）。</summary>
    Task AddAsync(CustomRuleConfig rule, CancellationToken ct = default);

    /// <summary>更新 config 自定义规则的可编辑字段。</summary>
    Task UpdateAsync(CustomRuleConfig rule, CancellationToken ct = default);

    /// <summary>按来源删除（内置不可删；<paramref name="workspaceRoot"/> 为 null 表示全局目录）。</summary>
    Task DeleteAsync(string id, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default);

    /// <summary>启用/停用（内置走禁用表，自定义走配置项；文件型不支持）。</summary>
    Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default);

    /// <summary>列出文件型规则 Id（文件名去扩展名），按名称排序。</summary>
    IReadOnlyList<string> ListFileIds(string? workspaceRoot);

    /// <summary>读取文件型规则；不存在时返回 null。</summary>
    CustomRuleConfig? ReadFile(string id, string? workspaceRoot);

    /// <summary>
    /// 写入文件型规则。<paramref name="originalId"/> 与 <see cref="CustomRuleConfig.Id"/> 不同时按改名处理
    /// （删除旧文件）。
    /// </summary>
    void SaveFile(string? originalId, CustomRuleConfig rule, string? workspaceRoot);
}