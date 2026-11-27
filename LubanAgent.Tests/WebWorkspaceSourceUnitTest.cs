/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebWorkspaceSourceUnitTest
*描述：新建项目的仓库地址校验与克隆目录名推导
*
*****************************************************************************/

using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

/// <summary>
/// git 建项目时的输入收敛：地址必须能安全交给 <c>git clone</c>，目录名必须是单层合法名。
/// </summary>
[TestClass]
public class WebWorkspaceSourceUnitTest
{
    [TestMethod]
    [DataRow("https://git.example.com/group/repo.git")]
    [DataRow("http://git.example.com/group/repo.git")]
    [DataRow("ssh://git@git.example.com/group/repo.git")]
    [DataRow("git://git.example.com/group/repo.git")]
    [DataRow("file:///D:/repos/demo.git")]
    [DataRow("git@git.example.com:group/repo.git")]
    public void IsSafeGitUrl_AcceptsSupportedForms(string url)
        => Assert.IsTrue(WebWorkspaceService.IsSafeGitUrl(url));

    [TestMethod]
    [DataRow("-oProxyCommand=calc.exe")]
    [DataRow("--upload-pack=calc.exe")]
    [DataRow("D:\\repos\\demo")]
    [DataRow("repo.git")]
    [DataRow("https://git.example.com/group/repo.git\r\nHost: evil")]
    [DataRow("")]
    public void IsSafeGitUrl_RejectsOptionLikeAndBarePaths(string url)
    {
        // 以 - 开头会被 git 当成选项（参数注入），裸路径 / 换行同样不接受
        Assert.IsFalse(WebWorkspaceService.IsSafeGitUrl(url));
    }

    [TestMethod]
    [DataRow("https://git.example.com/group/repo.git", "repo")]
    [DataRow("https://git.example.com/group/repo", "repo")]
    [DataRow("git@git.example.com:group/repo.git", "repo")]
    [DataRow("ssh://git@git.example.com/group/repo.git/", "repo")]
    public void FolderFromGitUrl_TakesLastSegmentWithoutGitSuffix(string url, string expected)
        => Assert.AreEqual(expected, WebWorkspaceService.FolderFromGitUrl(url));

    [TestMethod]
    public void ToFolderName_StripsSeparatorsAndParentReferences()
    {
        Assert.AreEqual("my-project", WebWorkspaceService.ToFolderName("my-project"));
        Assert.AreEqual("my-project", WebWorkspaceService.ToFolderName("  my-project  "));
        Assert.AreEqual("a-b", WebWorkspaceService.ToFolderName("a/b"));
        Assert.AreEqual("a-b", WebWorkspaceService.ToFolderName("a\\b"));
        Assert.AreEqual(string.Empty, WebWorkspaceService.ToFolderName(".."));
        Assert.AreEqual(string.Empty, WebWorkspaceService.ToFolderName("."));
        Assert.AreEqual(string.Empty, WebWorkspaceService.ToFolderName("   "));
    }

    [TestMethod]
    public void ToFolderName_RemovesTrailingDot()
    {
        Assert.AreEqual("demo", WebWorkspaceService.ToFolderName("demo."));
    }
}