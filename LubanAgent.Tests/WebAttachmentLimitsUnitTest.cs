/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebAttachmentLimitsUnitTest
*描述：附件上传的体积与像素上限

*****************************************************************************/

using LubanAgentWebHost.Services;
using LuBan.Common.IO;
using SkiaSharp;

namespace LubanAgent.Tests;

[TestClass]
public class WebAttachmentLimitsUnitTest
{
    [TestMethod]
    public void 估算_空载荷为零()
    {
        Assert.AreEqual(0L, WebAttachmentLimits.EstimateDecodedBytes(null));
        Assert.AreEqual(0L, WebAttachmentLimits.EstimateDecodedBytes(string.Empty));
    }

    [TestMethod]
    public void 估算_Base64长度按四分之三折算()
    {
        // 4 个 Base64 字符对应 3 字节
        Assert.AreEqual(3L, WebAttachmentLimits.EstimateDecodedBytes("AAAA"));
        Assert.AreEqual(3_000L, WebAttachmentLimits.EstimateDecodedBytes(new string('A', 4_000)));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void 估算_扣掉填充位且等于真实长度(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(i => (byte)(i + 1)).ToArray();
        var base64 = Convert.ToBase64String(bytes);

        Assert.AreEqual((long)length, WebAttachmentLimits.EstimateDecodedBytes(base64));
    }

    [TestMethod]
    public void 估算_恰好等于上限的文件不被误拒()
    {
        // 10MB 文本：估算值必须等于而不大于上限，否则边界文件会被提前拒绝
        var bytes = new byte[WebAttachmentLimits.MaxTextBytes];

        Assert.AreEqual(WebAttachmentLimits.MaxTextBytes, WebAttachmentLimits.EstimateDecodedBytes(Convert.ToBase64String(bytes)));
    }

    [TestMethod]
    public void 上限_图片与文本分开()
    {
        Assert.AreEqual(WebAttachmentLimits.MaxImageBytes, WebAttachmentLimits.MaxBytesOf(isImage: true));
        Assert.AreEqual(WebAttachmentLimits.MaxTextBytes, WebAttachmentLimits.MaxBytesOf(isImage: false));
    }

    [TestMethod]
    public void 超限提示_带上具体上限()
    {
        StringAssert.Contains(WebAttachmentLimits.OverLimitReason(isImage: true), "20MB");
        StringAssert.Contains(WebAttachmentLimits.OverLimitReason(isImage: false), "10MB");
        StringAssert.Contains(WebAttachmentLimits.OverRequestLimitReason(), "25MB");
    }

    [TestMethod]
    public void 请求总量上限_覆盖单张图片上限()
    {
        // 单请求上限必须不小于单文件上限，否则单张大图会被总量校验抢先拒掉
        Assert.IsTrue(WebAttachmentLimits.MaxRequestUploadBytes >= WebAttachmentLimits.MaxImageBytes);
    }

    [TestMethod]
    public void 不可解码扩展名_含HEIC与TIFF()
    {
        var extensions = WebAttachmentLimits.UndecodableExtensions;

        CollectionAssert.Contains(extensions.ToList(), ".heic");
        CollectionAssert.Contains(extensions.ToList(), ".tif");
        CollectionAssert.Contains(extensions.ToList(), ".tiff");
        CollectionAssert.DoesNotContain(extensions.ToList(), ".png");
    }

    [TestMethod]
    public void 图片扩展名_与框架判定同源()
    {
        var extensions = WebAttachmentLimits.ImageExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        CollectionAssert.AreEquivalent(MediaTypeUtil.ImageExtensions.ToList(), extensions.ToList());
        // SVG 仍按文本处理，不能被当成图片走解码校验
        CollectionAssert.DoesNotContain(extensions.ToList(), ".svg");
    }

    [TestMethod]
    public void 像素_恰好到上限通过()
    {
        // 上限 5000 万像素：5000×10000 正好命中
        Assert.IsNull(WebAttachmentLimits.ValidateImageDimensions(5_000, 10_000));
    }

    [TestMethod]
    public void 像素_超过上限被拒()
    {
        var reason = WebAttachmentLimits.ValidateImageDimensions(5_000, 10_001);

        Assert.IsNotNull(reason);
        StringAssert.Contains(reason!, "万像素");
    }

    [TestMethod]
    [DataRow(0, 100)]
    [DataRow(100, 0)]
    [DataRow(-1, -1)]
    public void 像素_非正尺寸被拒(int width, int height)
    {
        Assert.IsNotNull(WebAttachmentLimits.ValidateImageDimensions(width, height));
    }

    [TestMethod]
    public void 图片头_正常小图通过()
    {
        var png = CreatePng(8, 8);

        Assert.IsNull(WebAttachmentLimits.ValidateImage(png));
    }

    [TestMethod]
    public void 图片头_损坏数据被拒()
    {
        var reason = WebAttachmentLimits.ValidateImage("not an image"u8.ToArray());

        Assert.IsNotNull(reason);
        StringAssert.Contains(reason!, "无法解码");
    }

    /// <summary>生成指定尺寸的 PNG，仅用于读取头部。</summary>
    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}