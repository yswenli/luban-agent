namespace LubanAgentCore.Services.Management;

/// <summary>
/// 骨架期专用：统一「尚未实现」异常，明确后续任务与业务来源，
/// 避免各实现类各写一句 <c>throw new NotImplementedException()</c> 丢失上下文。
/// </summary>
internal static class NotYetImplemented
{
    /// <summary>构造异常。</summary>
    /// <param name="task">计划任务号，如 <c>Task 14</c>。</param>
    /// <param name="source">业务逻辑来源（原命令 / 原窗口）。</param>
    internal static NotImplementedException For(string task, string source)
        => new($"{task} 未实现：业务逻辑待从 {source} 搬入"
               + "（见 docs/superpowers/plans/2026-10-26-three-layer-refactor.md）。");
}