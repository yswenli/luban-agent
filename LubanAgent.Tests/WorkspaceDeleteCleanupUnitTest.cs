/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WorkspaceDeleteCleanupUnitTest
*创建时间：2026/11/12
*描述：删除工作区/知识库时的目录清理——
*      只删根目录下的 .luban-agent 配置目录，工作区目录本身与用户文件必须保留；
*      目录缺失或不可删时不得抛异常（数据库清理仍要继续）。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class WorkspaceDeleteCleanupUnitTest
{
    private readonly List<string> _tempRoots = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var root in _tempRoots)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // 清理测试残留失败不影响结论
            }
        }

        _tempRoots.Clear();
    }

    /// <summary>
    /// 建一个临时工作区根目录，含用户文件与 Agent 配置目录。
    /// </summary>
    private string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-ws-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);

        File.WriteAllText(Path.Combine(root, "readme.txt"), "user file");

        var configDir = Path.Combine(root, ".luban-agent");
        Directory.CreateDirectory(Path.Combine(configDir, "skills"));
        Directory.CreateDirectory(Path.Combine(configDir, "temp"));
        File.WriteAllText(Path.Combine(configDir, "skills", "demo.md"), "skill");
        File.WriteAllText(Path.Combine(configDir, "temp", "runtime.bin"), "runtime");
        File.WriteAllText(Path.Combine(configDir, "config.json"), "{}");

        return root;
    }

    [TestMethod]
    public void DeleteConfigDirectory_删除配置目录_保留根目录与用户文件()
    {
        var root = CreateWorkspaceRoot();

        WorkspaceManager.DeleteConfigDirectory(root);

        Assert.IsFalse(Directory.Exists(Path.Combine(root, ".luban-agent")),
            ".luban-agent 配置目录应被递归删除");
        Assert.IsTrue(Directory.Exists(root), "工作区根目录必须保留");
        Assert.IsTrue(File.Exists(Path.Combine(root, "readme.txt")), "根目录下的用户文件必须保留");
    }

    [TestMethod]
    public void DeleteConfigDirectory_配置目录不存在_不抛异常()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-ws-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);

        WorkspaceManager.DeleteConfigDirectory(root);

        Assert.IsTrue(Directory.Exists(root));
    }

    [TestMethod]
    public void DeleteConfigDirectory_重复调用_幂等()
    {
        var root = CreateWorkspaceRoot();

        WorkspaceManager.DeleteConfigDirectory(root);
        WorkspaceManager.DeleteConfigDirectory(root);

        Assert.IsFalse(Directory.Exists(Path.Combine(root, ".luban-agent")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "readme.txt")));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void DeleteConfigDirectory_空路径_不抛异常(string? rootPath)
    {
        WorkspaceManager.DeleteConfigDirectory(rootPath);
    }

    [TestMethod]
    public void DeleteConfigDirectory_根路径不存在_不抛异常()
    {
        WorkspaceManager.DeleteConfigDirectory(
            Path.Combine(Path.GetTempPath(), "luban-ws-missing-" + Guid.NewGuid().ToString("N")));
    }
}