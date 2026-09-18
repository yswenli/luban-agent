namespace LubanAgentCore.Models;

/// <summary>UI 展示用的附件信息。</summary>
/// <param name="FileName">文件名。</param>
/// <param name="MediaType">MIME 类型。</param>
/// <param name="FileSize">字节数。</param>
/// <param name="ThumbnailPath">缩略图路径（仅 Codex）。</param>
/// <param name="IsImage">是否图片。</param>
/// <param name="PixelSize">图片像素尺寸。</param>
public record AttachmentDisplayInfo(
    string FileName, string MediaType, long FileSize,
    string? ThumbnailPath, bool IsImage, string? PixelSize);
