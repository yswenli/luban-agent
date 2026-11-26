/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WorkspaceGitUnitTest
*描述：工作区根目录的 Git 仓库标记判定
*
*****************************************************************************/

using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

[TestClass]
public class WorkspaceGitUnitTest
{
    private string _root = "";

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "luban-workspace-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录清理失败不影响断言结论
        }
    }

    [TestMethod]
    public void IsRepository_DotGitDirectory_ReturnsTrue()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));

        Assert.IsTrue(WorkspaceGit.IsRepository(_root));
    }

    [TestMethod]
    public void IsRepository_DotGitFile_ReturnsTrue()
    {
        // worktree / submodule 的 .git 是指向真实元数据目录的文件
        File.WriteAllText(Path.Combine(_root, ".git"), "gitdir: ../.git/worktrees/demo");

        Assert.IsTrue(WorkspaceGit.IsRepository(_root));
    }

    [TestMethod]
    public void IsRepository_NoGitEntry_ReturnsFalse()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "README.md"), "# demo");

        Assert.IsFalse(WorkspaceGit.IsRepository(_root));
    }

    [TestMethod]
    public void IsRepository_OnlyNestedRepo_ReturnsFalse()
    {
        Directory.CreateDirectory(Path.Combine(_root, "packages", "lib", ".git"));

        Assert.IsFalse(WorkspaceGit.IsRepository(_root));
    }

    [TestMethod]
    public void IsRepository_BlankOrMissingPath_ReturnsFalse()
    {
        Assert.IsFalse(WorkspaceGit.IsRepository(null));
        Assert.IsFalse(WorkspaceGit.IsRepository(""));
        Assert.IsFalse(WorkspaceGit.IsRepository("   "));
        Assert.IsFalse(WorkspaceGit.IsRepository(Path.Combine(_root, "missing")));
    }
}