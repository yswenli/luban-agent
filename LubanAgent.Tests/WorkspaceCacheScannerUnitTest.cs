/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WorkspaceCacheScannerUnitTest
*描述：工作区缓存扫描与回收口径
*
*****************************************************************************/

using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

[TestClass]
public class WorkspaceCacheScannerUnitTest
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "luban-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "uploads", "20260101"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言结果
        }
    }

    private string WriteFile(string relative, int bytes, int ageDays = 0)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        if (ageDays > 0) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
        return path;
    }

    [TestMethod]
    public void Scan_SeparatesTempUploadsAndReferenced()
    {
        WriteFile(Path.Combine("thumb.png"), 100, ageDays: 10);
        var orphan = WriteFile(Path.Combine("uploads", "20260101", "orphan.png"), 200, ageDays: 10);
        var referenced = WriteFile(Path.Combine("uploads", "20260101", "used.png"), 300, ageDays: 10);

        var result = WorkspaceCacheScanner.Scan(_root, keepDays: 7, new[] { referenced });

        Assert.AreEqual(100, result.TempBytes);
        Assert.AreEqual(1, result.TempFiles);
        Assert.AreEqual(500, result.UploadBytes);
        Assert.AreEqual(2, result.UploadFiles);
        Assert.AreEqual(300, result.ReferencedBytes);
        Assert.AreEqual(1, result.ReferencedFiles);
        // 可回收 = 临时文件 + 未引用附件（已引用的默认保留）
        Assert.AreEqual(300, result.ReclaimableBytes);
        Assert.AreEqual(2, result.ReclaimableFiles);
        Assert.IsTrue(File.Exists(orphan));
    }

    [TestMethod]
    public void Scan_KeepsFilesInsideRetentionWindow()
    {
        WriteFile("recent.tmp", 50, ageDays: 0);
        WriteFile("old.tmp", 70, ageDays: 10);

        var result = WorkspaceCacheScanner.Scan(_root, keepDays: 7, Array.Empty<string>());

        Assert.AreEqual(120, result.TempBytes);
        Assert.AreEqual(70, result.ReclaimableBytes);
        Assert.AreEqual(1, result.ReclaimableFiles);
    }

    [TestMethod]
    public void Clean_DefaultOptions_KeepsReferencedUploads()
    {
        WriteFile("thumb.png", 10, ageDays: 10);
        WriteFile(Path.Combine("uploads", "20260101", "orphan.png"), 20, ageDays: 10);
        var referenced = WriteFile(Path.Combine("uploads", "20260101", "used.png"), 30, ageDays: 10);

        var result = WorkspaceCacheScanner.Clean(
            _root,
            keepDays: 7,
            new[] { referenced },
            includeTempFiles: true,
            includeOrphanUploads: true,
            includeReferencedUploads: false);

        Assert.AreEqual(30, result.FreedBytes);
        Assert.AreEqual(2, result.FreedFiles);
        Assert.AreEqual(0, result.FailedFiles);
        Assert.IsTrue(File.Exists(referenced));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "thumb.png")));
    }

    [TestMethod]
    public void Clean_ReferencedOption_RemovesReferencedUploads()
    {
        var referenced = WriteFile(Path.Combine("uploads", "20260101", "used.png"), 30, ageDays: 10);

        var result = WorkspaceCacheScanner.Clean(
            _root,
            keepDays: 7,
            new[] { referenced },
            includeTempFiles: false,
            includeOrphanUploads: false,
            includeReferencedUploads: true);

        Assert.AreEqual(30, result.FreedBytes);
        Assert.AreEqual(1, result.FreedFiles);
        Assert.IsFalse(File.Exists(referenced));
    }

    [TestMethod]
    public void Clean_RespectsSelectedCategories()
    {
        WriteFile("thumb.png", 10, ageDays: 10);
        WriteFile(Path.Combine("uploads", "20260101", "orphan.png"), 20, ageDays: 10);

        var result = WorkspaceCacheScanner.Clean(
            _root,
            keepDays: 7,
            Array.Empty<string>(),
            includeTempFiles: false,
            includeOrphanUploads: false,
            includeReferencedUploads: false);

        Assert.AreEqual(0, result.FreedBytes);
        Assert.AreEqual(0, result.FreedFiles);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "uploads", "20260101", "orphan.png")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "thumb.png")));
    }

    [TestMethod]
    public void Clean_NoRetention_RemovesRecentFilesToo()
    {
        WriteFile("recent.tmp", 10, ageDays: 0);

        var result = WorkspaceCacheScanner.Clean(
            _root,
            keepDays: 0,
            Array.Empty<string>(),
            includeTempFiles: true,
            includeOrphanUploads: true,
            includeReferencedUploads: false);

        Assert.AreEqual(10, result.FreedBytes);
        Assert.AreEqual(1, result.FreedFiles);
    }

    [TestMethod]
    public void Clean_PrunesEmptyDirectoriesButKeepsRoot()
    {
        WriteFile(Path.Combine("uploads", "20260101", "a.png"), 10, ageDays: 10);

        WorkspaceCacheScanner.Clean(
            _root,
            keepDays: 7,
            Array.Empty<string>(),
            includeTempFiles: true,
            includeOrphanUploads: true,
            includeReferencedUploads: false);

        Assert.IsTrue(Directory.Exists(_root));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "uploads")));
    }

    [TestMethod]
    public void Scan_MissingRoot_ReturnsZero()
    {
        var result = WorkspaceCacheScanner.Scan(Path.Combine(_root, "nope"), 7, Array.Empty<string>());

        Assert.AreEqual(0, result.TempBytes);
        Assert.AreEqual(0, result.UploadBytes);
        Assert.AreEqual(0, result.ReclaimableBytes);
    }
}