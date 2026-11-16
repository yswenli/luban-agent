/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebFileLinkUnitTest
*描述：取文件链接工具：绝对地址拼装、工作区边界与基地址归一化
*
*****************************************************************************/

using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

[TestClass]
public class WebFileLinkUnitTest
{
    private const string BaseAddress = "http://192.168.3.215:18080";
    private const string WorkspaceId = "07656df111f33eea9bf4458c1cdbac8f";

    private string _root = null!;
    private string _outside = null!;

    [TestInitialize]
    public void Setup()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"luban-filelink-{Guid.NewGuid():N}");
        _root = Path.Combine(parent, "workspace");
        _outside = Path.Combine(parent, "outside");

        Directory.CreateDirectory(Path.Combine(_root, "entities"));
        Directory.CreateDirectory(Path.Combine(_root, "wiki", "entities"));
        Directory.CreateDirectory(_outside);

        File.WriteAllText(Path.Combine(_root, "README.md"), "# root");
        File.WriteAllText(Path.Combine(_root, "entities", "实体.md"), "# entity");
        File.WriteAllText(Path.Combine(_root, "wiki", "overview.md"), "# wiki");
        File.WriteAllText(Path.Combine(_root, "wiki", "entities", "有 空格.md"), "# spaced");
        File.WriteAllText(Path.Combine(_outside, "secret.md"), "# outside");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结论
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    [TestMethod]
    public void TryBuild_RelativePath_ProducesAbsoluteLinkWithWorkspace()
    {
        Assert.IsTrue(WebFileLink.TryBuild(
            BaseAddress, WorkspaceId, _root, "entities/实体.md", out var link, out var relative, out var error));

        Assert.AreEqual(string.Empty, error);
        Assert.AreEqual("entities/实体.md", relative);
        Assert.AreEqual(
            $"{BaseAddress}/file/read?workspaceId={WorkspaceId}&path={Uri.EscapeDataString("entities/实体.md")}",
            link);
    }

    [TestMethod]
    public void TryBuild_WikiFallback_ReportsResolvedRelativePath()
    {
        // 模型只写页面名（LLM Wiki 的链接习惯），落地物理位置在 wiki/ 下
        Assert.IsTrue(WebFileLink.TryBuild(
            BaseAddress, WorkspaceId, _root, "overview.md", out var link, out var relative, out var error));

        Assert.AreEqual(string.Empty, error);
        Assert.AreEqual("wiki/overview.md", relative);
        StringAssert.Contains(link, "path=wiki%2Foverview.md");
    }

    [TestMethod]
    public void TryBuild_AbsolutePathInsideWorkspace_IsAccepted()
    {
        var absolute = Path.Combine(_root, "README.md");

        Assert.IsTrue(WebFileLink.TryBuild(
            BaseAddress, WorkspaceId, _root, absolute, out var link, out var relative, out var error));

        Assert.AreEqual("README.md", relative);
        StringAssert.Contains(link, "path=README.md");
    }

    [TestMethod]
    public void TryBuild_PathOutsideWorkspace_IsRejected()
    {
        var outside = Path.Combine(_outside, "secret.md");

        Assert.IsFalse(WebFileLink.TryBuild(
            BaseAddress, WorkspaceId, _root, outside, out var link, out _, out var error));

        Assert.AreEqual(string.Empty, link);
        StringAssert.Contains(error, "工作区外");
    }

    [TestMethod]
    public void TryBuild_TraversalAttempt_IsRejected()
    {
        Assert.IsFalse(WebFileLink.TryBuild(
            BaseAddress, WorkspaceId, _root, "../outside/secret.md", out _, out _, out var error));

        StringAssert.Contains(error, "工作区外");
    }

    [TestMethod]
    public void TryBuild_MissingFile_IsRejected()
    {
        Assert.IsFalse(WebFileLink.TryBuild(
            BaseAddress, WorkspaceId, _root, "wiki/queries/missing.md", out _, out _, out var error));

        StringAssert.Contains(error, "找不到");
    }

    [TestMethod]
    public void TryBuild_BlankPath_IsRejected()
    {
        Assert.IsFalse(WebFileLink.TryBuild(
            BaseAddress, WorkspaceId, _root, "   ", out _, out _, out var error));

        StringAssert.Contains(error, "path 不能为空");
    }

    [TestMethod]
    public void TryBuild_WithoutBaseAddress_IsRejected()
    {
        Assert.IsFalse(WebFileLink.TryBuild(
            null, WorkspaceId, _root, "README.md", out _, out _, out var error));

        StringAssert.Contains(error, "宿主地址");
    }

    [TestMethod]
    public void TryBuild_WithoutWorkspaceRoot_IsRejected()
    {
        Assert.IsFalse(WebFileLink.TryBuild(
            BaseAddress, WorkspaceId, null, "README.md", out _, out _, out var error));

        StringAssert.Contains(error, "工作区根目录");
    }

    [TestMethod]
    public void TryBuild_BaseAddressTrailingSlash_IsTrimmed()
    {
        Assert.IsTrue(WebFileLink.TryBuild(
            BaseAddress + "/", WorkspaceId, _root, "README.md", out var link, out _, out _));

        StringAssert.StartsWith(link, BaseAddress + "/file/read?");
    }

    [TestMethod]
    public void Normalize_HostHeader_KeepsPortAndDefaultsToHttp()
    {
        Assert.AreEqual(BaseAddress, WebRequestAddress.Normalize(null, "192.168.3.215:18080"));
    }

    [TestMethod]
    public void Normalize_ForwardedProto_IsHonoured()
    {
        Assert.AreEqual("https://agent.example.com", WebRequestAddress.Normalize("https", "agent.example.com"));
    }

    [TestMethod]
    public void Normalize_HostWithPath_IsRejected()
    {
        Assert.IsNull(WebRequestAddress.Normalize(null, "192.168.3.215/file/read"));
    }

    [TestMethod]
    public void Normalize_BlankHost_IsRejected()
    {
        Assert.IsNull(WebRequestAddress.Normalize(null, "  "));
    }

    [TestMethod]
    public void RawHeaderValue_HostInRawRequestText_IsRead()
    {
        // SAEA 解析后字典里的 Host 是空值，原始报文才是唯一来源
        const string raw = "POST /agent/chat HTTP/1.1\r\nHost: localhost:18080\r\nContent-Length: 12\r\n\r\n";

        Assert.AreEqual("localhost:18080", WebRequestAddress.RawHeaderValue(raw, "Host"));
    }

    [TestMethod]
    public void RawHeaderValue_HeaderNameIsCaseInsensitive()
    {
        const string raw = "GET / HTTP/1.1\r\nhost: 192.168.3.215:18080\r\n\r\n";

        Assert.AreEqual("192.168.3.215:18080", WebRequestAddress.RawHeaderValue(raw, "Host"));
    }

    [TestMethod]
    public void RawHeaderValue_MissingHeader_ReturnsNull()
    {
        const string raw = "GET / HTTP/1.1\r\nAuthorization: Bearer x\r\n\r\n";

        Assert.IsNull(WebRequestAddress.RawHeaderValue(raw, "Host"));
    }

    [TestMethod]
    public void RawHeaderValue_BlankHeaderText_ReturnsNull()
    {
        Assert.IsNull(WebRequestAddress.RawHeaderValue(null, "Host"));
        Assert.IsNull(WebRequestAddress.RawHeaderValue("   ", "Host"));
    }

    [TestMethod]
    public void RawHeaderValue_EmptyValue_ReturnsNull()
    {
        Assert.IsNull(WebRequestAddress.RawHeaderValue("GET / HTTP/1.1\r\nHost:\r\n\r\n", "Host"));
    }
}