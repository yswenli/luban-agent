/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectKnowledgeDocsUnitTest
*创建时间：2026/10/23
*描述：方案 A 单测——知识正文同步器（插件包 docs/ → 项目工作区）：
*      ① 正常覆盖式铺装（保留用户手工补充的文件）；
*      ② 插件包缺 docs/、插件包目录不可用、工作区为空 三种不可用情形一律判失败并给出可操作原因。
*      为什么必须锁死：docs 不可用时旧实现静默跳过，助手读不到组织专有口径只能凭记忆执行
*      （真机 Step 3 因此零写动作 + 回 DONE）。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectKnowledgeDocsUnitTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "luban-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响结论。
        }
    }

    private string PackageRoot => Path.Combine(_root, "package");

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private void WritePackageDoc(string relative, string content)
    {
        var path = Path.Combine(PackageRoot, "docs", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [TestMethod]
    public void Sync_铺装全部知识正文并覆盖同名文件_保留用户补充文件()
    {
        WritePackageDoc("org-reference.md", "org-v2");
        WritePackageDoc(Path.Combine("nested", "extra.md"), "nested");

        // 目标目录已有旧版同名文件 + 用户手工补充的文件
        var target = KnowledgeDocs.DirectoryOf(WorkspaceRoot);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "org-reference.md"), "org-v1");
        File.WriteAllText(Path.Combine(target, "user-note.md"), "keep-me");

        var (ok, count, targetDir, error) = KnowledgeDocs.Sync(PackageRoot, WorkspaceRoot);

        Assert.IsTrue(ok, error);
        Assert.IsNull(error);
        Assert.AreEqual(2, count);
        Assert.AreEqual(target, targetDir);
        Assert.AreEqual("org-v2", File.ReadAllText(Path.Combine(target, "org-reference.md")), "同名文件应以插件包为准。");
        Assert.AreEqual("nested", File.ReadAllText(Path.Combine(target, "nested", "extra.md")));
        Assert.IsTrue(File.Exists(Path.Combine(target, "user-note.md")), "不得删除用户手工补充的文件。");
    }

    [TestMethod]
    public void Sync_插件包缺docs目录_判失败并给出可操作原因()
    {
        Directory.CreateDirectory(PackageRoot);

        var (ok, count, target, error) = KnowledgeDocs.Sync(PackageRoot, WorkspaceRoot);

        Assert.IsFalse(ok);
        Assert.AreEqual(0, count);
        Assert.AreEqual(KnowledgeDocs.DirectoryOf(WorkspaceRoot), target);
        StringAssert.Contains(error, "docs");
    }

    [TestMethod]
    public void Sync_插件包目录不可用_判失败()
    {
        var (ok, _, _, error) = KnowledgeDocs.Sync(null, WorkspaceRoot);

        Assert.IsFalse(ok);
        StringAssert.Contains(error, "插件包");
    }

    [TestMethod]
    public void Sync_工作区为空_判失败()
    {
        WritePackageDoc("org-reference.md", "x");

        var (ok, _, _, error) = KnowledgeDocs.Sync(PackageRoot, "  ");

        Assert.IsFalse(ok);
        StringAssert.Contains(error, "工作区");
    }

    [TestMethod]
    public void Sync_插件包docs为空目录_判失败()
    {
        Directory.CreateDirectory(Path.Combine(PackageRoot, "docs"));

        var (ok, _, _, error) = KnowledgeDocs.Sync(PackageRoot, WorkspaceRoot);

        Assert.IsFalse(ok);
        StringAssert.Contains(error, "没有任何知识正文文件");
    }
}