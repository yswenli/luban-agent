/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebFileIgnoreUnitTest
*描述：工作区目录列举的默认忽略规则
*
*****************************************************************************/

using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

[TestClass]
public class WebFileIgnoreUnitTest
{
    [TestMethod]
    [DataRow("node_modules")]
    [DataRow("bin")]
    [DataRow("obj")]
    [DataRow("dist")]
    [DataRow("target")]
    [DataRow("TestResults")]
    public void IsIgnored_KnownBuildDirectories_AreSkipped(string name)
    {
        Assert.IsTrue(WebFileIgnore.IsIgnored(name, isDirectory: true));
    }

    [TestMethod]
    [DataRow(".git")]
    [DataRow(".luban-agent")]
    public void IsIgnored_HiddenDirectories_AreSkipped(string name)
    {
        Assert.IsTrue(WebFileIgnore.IsIgnored(name, isDirectory: true));
    }

    [TestMethod]
    [DataRow(".gitignore")]
    [DataRow(".env")]
    public void IsIgnored_HiddenFiles_AreKept(string name)
    {
        // 点开头的配置文件常是要看的，只隐藏点开头目录
        Assert.IsFalse(WebFileIgnore.IsIgnored(name, isDirectory: false));
    }

    [TestMethod]
    [DataRow("src")]
    [DataRow("Entities")]
    [DataRow("node_modules_backup")]
    public void IsIgnored_NormalDirectories_AreKept(string name)
    {
        Assert.IsFalse(WebFileIgnore.IsIgnored(name, isDirectory: true));
    }

    [TestMethod]
    public void IsIgnored_RegularFiles_AreNeverSkipped()
    {
        // 文件名与忽略目录同名时仍要显示
        Assert.IsFalse(WebFileIgnore.IsIgnored("build", isDirectory: false));
    }
}