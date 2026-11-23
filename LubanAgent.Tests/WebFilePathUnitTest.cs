/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebFilePathUnitTest
*描述：会话链接相对路径 → 工作区内绝对路径的解析与越界拦截
*
*****************************************************************************/

using System.Diagnostics;
using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

[TestClass]
public class WebFilePathUnitTest
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"luban-webfile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "wiki", "entities"));
        File.WriteAllText(Path.Combine(_root, "README.md"), "# root");
        File.WriteAllText(Path.Combine(_root, "wiki", "overview.md"), "# wiki");
        File.WriteAllText(Path.Combine(_root, "wiki", "entities", "有 空格.md"), "# entity");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
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
    public void TryResolve_RootRelativeFile_Hits()
    {
        Assert.IsTrue(WebFilePath.TryResolve(_root, "README.md", out var full));
        Assert.AreEqual(Path.Combine(_root, "README.md"), full);
    }

    [TestMethod]
    public void TryResolve_WikiPage_FallsBackToWikiDirectory()
    {
        Assert.IsTrue(WebFilePath.TryResolve(_root, "overview.md", out var full));
        Assert.AreEqual(Path.Combine(_root, "wiki", "overview.md"), full);
    }

    [TestMethod]
    public void TryResolve_WikiPrefixedLink_Hits()
    {
        Assert.IsTrue(WebFilePath.TryResolve(_root, "wiki/overview.md", out var full));
        Assert.AreEqual(Path.Combine(_root, "wiki", "overview.md"), full);
    }

    [TestMethod]
    public void TryResolve_DotPrefixAndAnchor_AreIgnored()
    {
        Assert.IsTrue(WebFilePath.TryResolve(_root, "./wiki/overview.md#概览", out var full));
        Assert.AreEqual(Path.Combine(_root, "wiki", "overview.md"), full);
    }

    [TestMethod]
    public void TryResolve_PercentEncodedPath_IsDecoded()
    {
        Assert.IsTrue(WebFilePath.TryResolve(_root, "entities/%E6%9C%89%20%E7%A9%BA%E6%A0%BC.md", out var full));
        Assert.AreEqual(Path.Combine(_root, "wiki", "entities", "有 空格.md"), full);
    }

    [TestMethod]
    public void TryResolve_MissingFile_ReturnsFalse()
    {
        Assert.IsFalse(WebFilePath.TryResolve(_root, "queries/missing.md", out var full));
        Assert.AreEqual(string.Empty, full);
    }

    [TestMethod]
    public void TryResolve_ParentTraversal_ReturnsFalse()
    {
        Assert.IsFalse(WebFilePath.TryResolve(_root, "../../windows/win.ini", out _));
    }

    [TestMethod]
    public void TryResolve_AbsolutePath_ReturnsFalse()
    {
        Assert.IsFalse(WebFilePath.TryResolve(_root, Path.Combine(_root, "README.md"), out _));
    }

    [TestMethod]
    public void TryResolve_BlankInputs_ReturnFalse()
    {
        Assert.IsFalse(WebFilePath.TryResolve(null, "README.md", out _));
        Assert.IsFalse(WebFilePath.TryResolve(_root, "  ", out _));
    }

    [TestMethod]
    public void IsWithin_SiblingDirectory_WithSharedPrefix_ReturnsFalse()
    {
        var sibling = _root + "-other";
        Assert.IsFalse(WebFilePath.IsWithin(_root, Path.Combine(sibling, "README.md")));
    }

    [TestMethod]
    public void TryResolve_JunctionToOutside_ReturnsFalse()
    {
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.md"), "secret");

        var link = Path.Combine(_root, "leak");
        if (!TryCreateDirectoryLink(link, outside))
        {
            Directory.Delete(outside, recursive: true);
            Assert.Inconclusive("当前环境无法创建目录链接，跳过越界用例");
            return;
        }

        try
        {
            // 字符串上看仍在根内，真实落点却在根外：必须拒绝
            Assert.IsFalse(WebFilePath.TryResolve(_root, "leak/secret.md", out var full));
            Assert.AreEqual(string.Empty, full);
        }
        finally
        {
            DeleteLink(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    [TestMethod]
    public void TryResolve_JunctionInsideRoot_IsStillReadable()
    {
        var link = Path.Combine(_root, "alias");
        if (!TryCreateDirectoryLink(link, Path.Combine(_root, "wiki")))
        {
            Assert.Inconclusive("当前环境无法创建目录链接，跳过该用例");
            return;
        }

        try
        {
            Assert.IsTrue(WebFilePath.TryResolve(_root, "alias/overview.md", out var full));
            // 回传真实路径：调用方读取时不再穿过链接
            Assert.AreEqual(Path.Combine(_root, "wiki", "overview.md"), full);
        }
        finally
        {
            DeleteLink(link);
        }
    }

    [TestMethod]
    public void TryGetRelative_FileUnderRoot_ReturnsForwardSlashedRelativePath()
    {
        var full = Path.Combine(_root, "wiki", "entities", "有 空格.md");

        Assert.IsTrue(WebFilePath.TryGetRelative(_root, full, out var relative));
        Assert.AreEqual("wiki/entities/有 空格.md", relative);
    }

    [TestMethod]
    public void TryGetRelative_OutsideRoot_ReturnsFalse()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"luban-outside-{Guid.NewGuid():N}.png");

        Assert.IsFalse(WebFilePath.TryGetRelative(_root, outside, out var relative));
        Assert.AreEqual(string.Empty, relative);
    }

    [TestMethod]
    public void TryGetRelative_RelativeInput_IsResolvedAgainstCurrentDirectory()
    {
        // 相对路径会被 GetFullPath 按进程当前目录展开；展开后不在工作区内，必须拒绝
        Assert.IsFalse(WebFilePath.TryGetRelative(_root, "README.md", out _));
    }

    [TestMethod]
    public void TryGetRelative_AlternateDataStream_ReturnsFalse()
    {
        var file = Path.Combine(_root, "README.md");
        const string stream = "secret";
        if (!TryWriteAlternateDataStream($"{file}:{stream}", "ads-secret"))
        {
            Assert.Inconclusive("当前文件系统不支持 NTFS 数据流，跳过该用例");
            return;
        }

        // 反方向同样要拦：WebPath 一旦带冒号，前端拼出的 /file/read 就能读到数据流
        Assert.IsFalse(WebFilePath.TryGetRelative(_root, $"{file}:{stream}", out _));
    }

    [TestMethod]
    public void TryResolve_AlternateDataStream_ReturnsFalse()
    {
        var file = Path.Combine(_root, "README.md");
        const string stream = "secret";
        if (!TryWriteAlternateDataStream($"{file}:{stream}", "ads-secret"))
        {
            Assert.Inconclusive("当前文件系统不支持 NTFS 数据流，跳过该用例");
            return;
        }

        // 数据流确实写进去了：不拦冒号时 File.Exists 能命中，等于绕开「只读正文」的限制
        Assert.IsTrue(File.Exists($"{file}:{stream}"), "数据流未创建，用例前提不成立");
        Assert.IsFalse(WebFilePath.TryResolve(_root, $"README.md:{stream}", out _));
        Assert.IsFalse(WebFilePath.TryResolve(_root, $"README.md%3A{stream}", out _));
    }

    /// <summary>创建 NTFS 数据流（.NET 的 Path 校验不接受冒号，只能借 cmd 写入）。</summary>
    /// <param name="streamPath">带 <c>:流名</c> 后缀的完整路径。</param>
    /// <param name="content">写入内容。</param>
    /// <returns>写入成功返回 true。</returns>
    private static bool TryWriteAlternateDataStream(string streamPath, string content)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c echo {content}> \"{streamPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return false;
            process.WaitForExit(5000);
            return File.Exists(streamPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>创建目录链接（符号链接或 Windows junction）；环境不支持时返回 false。</summary>
    /// <param name="linkPath">链接路径。</param>
    /// <param name="targetPath">目标目录。</param>
    /// <returns>链接创建成功返回 true。</returns>
    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Windows 未开开发者模式时符号链接需要提权，退回 junction（普通用户可建）
        }

        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return false;
            process.WaitForExit(5000);
            return Directory.Exists(linkPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>删除目录链接本身（不触碰目标目录）。</summary>
    /// <param name="linkPath">链接路径。</param>
    private static void DeleteLink(string linkPath)
    {
        try
        {
            if (Directory.Exists(linkPath)) Directory.Delete(linkPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响断言结论
        }
    }
}