/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Models
*文件名： AttachmentDisplayInfo
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/18
*描述：UI 展示用的附件信息。
*
*=================================================
*修改标记
*修改时间：2026/9/18
*修改人： yswenli
*版本号： V1.0.0.0
*描述：UI 展示用的附件信息。
*
*****************************************************************************/

namespace LubanAgentCore.Models;

/// <summary>UI 展示用的附件信息。</summary>
/// <param name="FileName">文件名。</param>
/// <param name="MediaType">MIME 类型。</param>
/// <param name="FileSize">字节数。</param>
/// <param name="ThumbnailPath">缩略图路径（仅 Codex）。</param>
/// <param name="IsImage">是否图片。</param>
/// <param name="PixelSize">图片像素尺寸。</param>
/// <param name="SourcePath">源文件绝对路径（用于点击查看大图；历史回放可能已不存在）。</param>
public record AttachmentDisplayInfo(
    string FileName, string MediaType, long FileSize,
    string? ThumbnailPath, bool IsImage, string? PixelSize,
    string? SourcePath = null);
