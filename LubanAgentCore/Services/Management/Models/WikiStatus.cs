namespace LubanAgentCore.Services.Management.Models;

/// <summary>LLM Wiki 索引状态。</summary>
/// <param name="PageCount">wiki 页面数（不含 log.md）。</param>
/// <param name="LastRebuildSummary">最近一次重建摘要；未重建过为 null。</param>
/// <param name="IndexedFiles">已索引文件数（llmwiki_file 有效行）。</param>
/// <param name="IndexedChunks">已索引分块数（llmwiki_chunk 有效行）。</param>
/// <remarks>
/// 前两项对齐 <c>/llmwiki -status</c> 的实际输出；后两项保留骨架原有的索引计数语义，
/// 供后续界面/插件复用。原骨架的 <c>IsAvailable</c>、<c>Message</c> 无数据来源亦无消费者，已移除。
/// </remarks>
public sealed record WikiStatus(
    int PageCount,
    string? LastRebuildSummary,
    int IndexedFiles,
    int IndexedChunks);