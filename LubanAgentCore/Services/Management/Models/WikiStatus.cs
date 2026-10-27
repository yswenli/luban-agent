namespace LubanAgentCore.Services.Management.Models;

/// <summary>LLM Wiki 索引状态。</summary>
/// <param name="IsAvailable">当前工作区是否具备索引能力（未配置嵌入模型时为 false）。</param>
/// <param name="IndexedFiles">已索引文件数。</param>
/// <param name="IndexedChunks">已索引分块数。</param>
/// <param name="Message">状态说明（不可用原因等）。</param>
public sealed record WikiStatus(bool IsAvailable, int IndexedFiles, int IndexedChunks, string Message);