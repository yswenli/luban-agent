namespace LubanAgentCore.Services.Management.Models;

/// <summary>已索引文件条目（<c>/llmwiki -list</c> 的一行）。</summary>
/// <param name="FilePath">文件路径（索引时的原始路径）。</param>
/// <param name="Language">语言标识；未知为 null。</param>
/// <param name="ChunkCount">切块数。</param>
/// <param name="IndexedTime">索引时间。</param>
public sealed record WikiFileInfo(
    string FilePath,
    string? Language,
    int ChunkCount,
    DateTime IndexedTime);