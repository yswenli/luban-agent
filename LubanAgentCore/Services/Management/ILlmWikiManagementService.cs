using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// LLM Wiki 索引管理。
/// 方法面 = 现有 <c>/wiki</c> 命令的能力，不新增能力。
/// </summary>
public interface ILlmWikiManagementService
{
    /// <summary>当前工作区的索引状态。</summary>
    Task<WikiStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>重建指定工作区的索引（带进度回报）。</summary>
    Task RebuildAsync(string workspaceId, IProgress<double>? progress, CancellationToken ct = default);

    /// <summary>清空指定工作区的索引。</summary>
    Task ClearAsync(string workspaceId, CancellationToken ct = default);
}