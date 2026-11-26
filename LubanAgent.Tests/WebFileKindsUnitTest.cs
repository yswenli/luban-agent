/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebFileKindsUnitTest
*描述：工作区文件预览的类型白名单与体积上限
*
*****************************************************************************/

using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

[TestClass]
public class WebFileKindsUnitTest
{
    [TestMethod]
    [DataRow("README.md")]
    [DataRow("logs/agent.log")]
    [DataRow("notes.txt")]
    [DataRow("src/Program.cs")]
    [DataRow("web/src/lib/markdown.ts")]
    [DataRow("appsettings.json")]
    [DataRow("Dockerfile")]
    [DataRow(".gitignore")]
    [DataRow("page.html")]
    [DataRow("index.htm")]
    public void Classify_TextFiles_ReturnText(string path)
    {
        Assert.AreEqual(WebFilePreviewKind.Text, WebFileKinds.Classify(path, out var contentType));
        Assert.AreEqual(WebFileKinds.TextContentType, contentType);
    }

    [TestMethod]
    [DataRow("diagram.svg")]
    public void Classify_Svg_IsTextNotImage(string path)
    {
        // 同源直出 SVG 会让其中的脚本以本页权限执行，故按源码文本展示
        Assert.AreEqual(WebFilePreviewKind.Text, WebFileKinds.Classify(path, out _));
    }

    [TestMethod]
    [DataRow("assets/screenshot.png", "image/png")]
    [DataRow("assets/photo.JPG", "image/jpeg")]
    [DataRow("assets/anim.gif", "image/gif")]
    [DataRow("assets/pic.webp", "image/webp")]
    [DataRow("assets/icon.ico", "image/x-icon")]
    public void Classify_Images_ReturnImageWithContentType(string path, string expected)
    {
        Assert.AreEqual(WebFilePreviewKind.Image, WebFileKinds.Classify(path, out var contentType));
        Assert.AreEqual(expected, contentType);
    }

    [TestMethod]
    [DataRow("tool.exe")]
    [DataRow("archive.zip")]
    [DataRow("data.db")]
    [DataRow("lib.dll")]
    [DataRow("MakefileX")]
    [DataRow("no-extension")]
    public void Classify_UnknownFormats_AreUnsupported(string path)
    {
        Assert.AreEqual(WebFilePreviewKind.Unsupported, WebFileKinds.Classify(path, out var contentType));
        Assert.AreEqual(string.Empty, contentType);
    }

    [TestMethod]
    public void Classify_BlankInput_IsUnsupported()
    {
        Assert.AreEqual(WebFilePreviewKind.Unsupported, WebFileKinds.Classify(null, out _));
        Assert.AreEqual(WebFilePreviewKind.Unsupported, WebFileKinds.Classify("   ", out _));
    }

    [TestMethod]
    [DataRow("assets/demo.mp4", "video/mp4")]
    [DataRow("assets/demo.webm", "video/webm")]
    [DataRow("assets/demo.mov", "video/quicktime")]
    public void Classify_Videos_ReturnVideoWithContentType(string path, string expected)
    {
        Assert.AreEqual(WebFilePreviewKind.Video, WebFileKinds.Classify(path, out var contentType));
        Assert.AreEqual(expected, contentType);
    }

    [TestMethod]
    [DataRow("assets/voice.mp3", "audio/mpeg")]
    [DataRow("assets/voice.wav", "audio/wav")]
    [DataRow("assets/voice.flac", "audio/flac")]
    public void Classify_Audio_ReturnAudioWithContentType(string path, string expected)
    {
        Assert.AreEqual(WebFilePreviewKind.Audio, WebFileKinds.Classify(path, out var contentType));
        Assert.AreEqual(expected, contentType);
    }

    [TestMethod]
    [DataRow("movie.mkv")]
    [DataRow("movie.avi")]
    [DataRow("movie.wmv")]
    public void Classify_UnsupportedContainers_AreRejected(string path)
    {
        // 浏览器原生放不出来的封装不进白名单：回 415 比回一个放不出的流更好排查
        Assert.AreEqual(WebFilePreviewKind.Unsupported, WebFileKinds.Classify(path, out _));
    }

    [TestMethod]
    public void LimitOf_MatchesKind()
    {
        Assert.AreEqual(WebFileKinds.MaxTextBytes, WebFileKinds.LimitOf(WebFilePreviewKind.Text));
        Assert.AreEqual(WebFileKinds.MaxImageBytes, WebFileKinds.LimitOf(WebFilePreviewKind.Image));
        Assert.AreEqual(WebFileKinds.MaxAudioBytes, WebFileKinds.LimitOf(WebFilePreviewKind.Audio));
        Assert.AreEqual(WebFileKinds.MaxVideoBytes, WebFileKinds.LimitOf(WebFilePreviewKind.Video));
        Assert.AreEqual(0, WebFileKinds.LimitOf(WebFilePreviewKind.Unsupported));
    }
}