using LuBan.AIAgent.Tools.Skill;

namespace LubanAgentCore.Services.Management;

/// <summary>Skill 管理实现（工作区自定义 + 内置）。</summary>
public sealed class SkillManagementService : ISkillManagementService
{
    private const string Source = "SkillCommand / Codex 设置页 Skill 页";

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillInfo>> ListAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 15", Source);

    /// <inheritdoc />
    public Task ImportAsync(string sourcePath, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 15", Source);

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 15", Source);

    /// <inheritdoc />
    public Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 15", Source);
}