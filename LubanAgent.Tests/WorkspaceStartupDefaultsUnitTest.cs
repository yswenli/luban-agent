/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WorkspaceStartupDefaultsUnitTest
*创建时间：2026/11/6
*描述：首次启动工作区根目录建议——锁死「不得把宿主输出目录当工作区」：
*      进程当前目录常为 bin\Debug\net10.0，直接建工作区会让文件工具一直读构建产物。
*
*****************************************************************************/

using LubanAgentCore.Hosting;

namespace LubanAgent.Tests;

[TestClass]
public class WorkspaceStartupDefaultsUnitTest
{
    [TestMethod]
    public void IsUserFacingDirectory_HostOutputDirectory_ReturnsFalse()
    {
        var appBase = AppContext.BaseDirectory;

        Assert.IsFalse(WorkspaceStartupDefaults.IsUserFacingDirectory(appBase),
            $"宿主输出目录不应作为用户工作区: {appBase}");
    }

    [TestMethod]
    public void IsUserFacingDirectory_DriveRoot_ReturnsFalse()
    {
        var root = Path.GetPathRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(root);

        Assert.IsFalse(WorkspaceStartupDefaults.IsUserFacingDirectory(root),
            $"盘符根不应作为用户工作区: {root}");
    }

    [TestMethod]
    public void IsUserFacingDirectory_TempDirectory_ReturnsTrue()
    {
        var temp = Path.GetTempPath();

        Assert.IsTrue(WorkspaceStartupDefaults.IsUserFacingDirectory(temp),
            $"临时目录是普通用户目录，应可作为工作区: {temp}");
    }

    [TestMethod]
    public void SuggestRootPath_ReturnsAbsolutePath_NotHostOutputDirectory()
    {
        var suggestion = WorkspaceStartupDefaults.SuggestRootPath();

        Assert.IsTrue(Path.IsPathFullyQualified(suggestion), $"建议路径必须是绝对路径: {suggestion}");
        Assert.IsTrue(WorkspaceStartupDefaults.IsUserFacingDirectory(suggestion),
            $"建议路径必须可用作用户工作区: {suggestion}");

        var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (WorkspaceStartupDefaults.IsUserFacingDirectory(cwd))
        {
            // 测试进程当前目录本身就是普通用户目录时（如从解决方案根目录运行），沿用当前目录是预期行为
            Assert.AreEqual(cwd, suggestion, ignoreCase: true);
        }
        else
        {
            Assert.AreNotEqual(cwd, suggestion, ignoreCase: true,
                "当前目录是宿主输出目录，不应被当作工作区根目录");
        }
    }

    [TestMethod]
    public void SuggestExistingDirectory_ReturnsExistingDirectory()
    {
        var existing = WorkspaceStartupDefaults.SuggestExistingDirectory();

        Assert.IsTrue(Directory.Exists(existing), $"预填目录必须已存在（创建表单要求）: {existing}");
    }
}