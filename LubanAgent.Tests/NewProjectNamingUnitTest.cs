/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectNamingUnitTest
*描述：项目命名令牌单测（PRD §4.3 Step 1/Step 3：缩写**首字母大写** → 代码库名 Csxmy-Service；
*      缩写**全大写** → 代码组名 CSXMY；令牌 {code}/{Code}/{CODE} 区分大小写）
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Configuration;

namespace LubanAgent.Tests;

/// <summary>
/// 项目命名令牌（<see cref="NewProjectSettings.BuildRepositoryName"/> / <see cref="NewProjectSettings.BuildGroupName"/> /
/// <see cref="NewProjectSettings.ExpandTokens"/>）单测：锁定「代码库名首字母大写、代码组名全大写」的默认口径。
/// </summary>
/// <remarks>
/// 回归护栏：<c>BuildRepositoryName</c> 曾用 <c>Replace("{code}", …, OrdinalIgnoreCase)</c>——它会连带把
/// <c>{Code}</c> / <c>{CODE}</c> 也替换成小写原文。这些用例保证三种令牌<b>互不串味</b>。
/// </remarks>
[TestClass]
public class NewProjectNamingUnitTest
{
    [TestMethod]
    public void BuildRepositoryName_DefaultFormat_CapitalizesFirstLetter()
    {
        var settings = new NewProjectSettings();

        Assert.AreEqual("Csxmy-Service", settings.BuildRepositoryName("csxmy", ProjectEnd.Service));
        Assert.AreEqual("Csxmy-Admin", settings.BuildRepositoryName("csxmy", ProjectEnd.Admin));
        Assert.AreEqual("Csxmy-Mobile", settings.BuildRepositoryName("csxmy", ProjectEnd.Mobile));
    }

    [TestMethod]
    public void BuildRepositoryName_LowercaseFormat_KeepsOriginalCode()
    {
        var settings = new NewProjectSettings { RepositoryNameFormat = "{code}-{end}" };

        Assert.AreEqual("csxmy-Service", settings.BuildRepositoryName("csxmy", ProjectEnd.Service));
    }

    [TestMethod]
    public void BuildRepositoryName_UppercaseFormat_UsesAllCaps()
    {
        var settings = new NewProjectSettings { RepositoryNameFormat = "{CODE}-{end}" };

        Assert.AreEqual("CSXMY-Service", settings.BuildRepositoryName("csxmy", ProjectEnd.Service));
    }

    [TestMethod]
    public void BuildRepositoryName_BlankFormat_FallsBackToCapitalized()
    {
        var settings = new NewProjectSettings { RepositoryNameFormat = "   " };

        Assert.AreEqual("Csxmy-Service", settings.BuildRepositoryName("csxmy", ProjectEnd.Service));
    }

    [TestMethod]
    public void BuildGroupName_DefaultFormat_UsesAllCaps()
    {
        Assert.AreEqual("CSXMY", new NewProjectSettings().BuildGroupName("csxmy", "测试项目"));
    }

    [TestMethod]
    public void ExpandTokens_IsCaseSensitive()
    {
        Assert.AreEqual("csxmy|Csxmy|CSXMY", NewProjectSettings.ExpandTokens("{code}|{Code}|{CODE}", "csxmy", "x"));
    }
}