/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebAttachmentLimits
*描述：上传附件的体积与像素上限（纯逻辑，便于单测）

*****************************************************************************/

using SkiaSharp;

namespace LubanAgentWebHost.Services;

/// <summary>
/// 上传附件的硬上限。
/// </summary>
/// <remarks>
/// <para>
/// 这里只拦「不该进入解码流程」的输入：真正的缩放与重编码由框架附件处理器负责
/// （图片 5MB / 2000px 触发）。解码一张图要占 <c>宽×高×4</c> 字节，
/// 超大图会把宿主进程直接拖进 OOM，所以在落盘前先按体积与像素数拒绝。
/// </para>
/// <para>
/// 只读图片头取尺寸，不做整幅解码；判断失败按「无法解码」处理，与发送阶段报错相比更早给出提示。
/// </para>
/// </remarks>
public static class WebAttachmentLimits
{
    /// <summary>文本类附件单文件上限（字节），与框架附件处理器的阈值一致。</summary>
    public const long MaxTextBytes = 10L * 1024 * 1024;

    /// <summary>图片单文件上限（字节）。</summary>
    public const long MaxImageBytes = 20L * 1024 * 1024;

    /// <summary>图片像素总数上限（宽×高）。</summary>
    public const long MaxImagePixels = 50_000_000;

    /// <summary>
    /// 单次请求所有附件解码后字节数之和的上限。
    /// </summary>
    /// <remarks>
    /// 浏览器每次请求只带一个文件（图片已压到 5MB 以内），25MB 足够覆盖 5 张已压缩图片；
    /// 超过说明调用方未按协议分片，直接拒绝，免得把整包载荷逐个解码后才发现超限。
    /// </remarks>
    public const long MaxRequestUploadBytes = 25L * 1024 * 1024;

    /// <summary>
    /// 前端可选的图片扩展名（与 <c>MediaTypeUtil.ImageExtensions</c> 同源）。
    /// </summary>
    public static IReadOnlyList<string> ImageExtensions { get; } =
        MediaTypeUtil.ImageExtensions.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// 前端应提前拦截的扩展名：HEIC 类与 TIFF。
    /// </summary>
    /// <remarks>
    /// TIFF 不在 Skia 上游的编解码器里，<c>SKCodec.Create</c> 对它返回 <c>null</c>，
    /// 上传后必然被 <see cref="ValidateImage"/> 拒绝；提前拦下省掉一次往返。
    /// </remarks>
    public static IReadOnlyList<string> UndecodableExtensions { get; } =
        MediaTypeUtil.HeicLikeExtensions.Concat([".tif", ".tiff"])
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 按 Base64 字符串长度估算解码后的字节数。
    /// </summary>
    /// <remarks>
    /// 估算值只用于在 <c>Convert.FromBase64String</c> 之前拦下明显超限的请求，
    /// 避免为几十 MB 的载荷先分配解码缓冲；精确值仍在解码后再判一次。
    /// </remarks>
    /// <param name="base64">Base64 文本。</param>
    /// <returns>估算的原始字节数。</returns>
    public static long EstimateDecodedBytes(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            return 0;
        }

        // 末尾 '=' 是填充位，不属于原始数据；不扣掉会让「恰好等于上限」的文件被误拒
        var padding = base64[^1] == '='
            ? (base64.Length > 1 && base64[^2] == '=' ? 2 : 1)
            : 0;
        return base64.Length / 4 * 3L - padding;
    }

    /// <summary>按是否图片给出单文件上限（字节）。</summary>
    /// <param name="isImage">是否图片附件。</param>
    /// <returns>上限字节数。</returns>
    public static long MaxBytesOf(bool isImage) => isImage ? MaxImageBytes : MaxTextBytes;

    /// <summary>超限提示。</summary>
    /// <param name="isImage">是否图片附件。</param>
    /// <returns>可直接展示给用户的拒绝原因。</returns>
    public static string OverLimitReason(bool isImage) => isImage
        ? $"图片超过 {MaxImageBytes / 1024 / 1024}MB"
        : "文本类附件超过 10MB";

    /// <summary>单次请求总量超限提示。</summary>
    /// <returns>可直接展示给用户的拒绝原因。</returns>
    public static string OverRequestLimitReason()
        => $"单次上传总量超过 {MaxRequestUploadBytes / 1024 / 1024}MB，请分批上传";

    /// <summary>
    /// 按宽高校验像素总数。
    /// </summary>
    /// <param name="width">图片宽。</param>
    /// <param name="height">图片高。</param>
    /// <returns>超限或尺寸无效时的原因；通过返回 <c>null</c>。</returns>
    public static string? ValidateImageDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return "图片尺寸无效";
        }

        if ((long)width * height <= MaxImagePixels)
        {
            return null;
        }

        return $"图片像素过大（{width}×{height}，上限 {MaxImagePixels / 10_000} 万像素）";
    }

    /// <summary>
    /// 读图片头校验像素总数。
    /// </summary>
    /// <param name="bytes">图片原始字节。</param>
    /// <returns>无法解码或超限时的原因；通过返回 <c>null</c>。</returns>
    public static string? ValidateImage(byte[] bytes)
    {
        try
        {
            // 只读图片头：SKCodec 取尺寸不需要整幅解码，也不额外复制字节
            using var stream = new MemoryStream(bytes, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                return "图片无法解码，请转换为 PNG 或 JPEG";
            }

            return ValidateImageDimensions(codec.Info.Width, codec.Info.Height);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "图片无法解码，请转换为 PNG 或 JPEG";
        }
    }
}