/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectNamingUnitTest
*描述：项目命名令牌单测（对应向导 Step 1 与 Step 3：单仓名 {Code}-Mixed；仓内模块目录 {Code}-{end}；
*      代码组名全大写 CSXMY；令牌 {code}/{Code}/{CODE} 区分大小写）
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Configuration;

namespace LubanAgent.Tests;

/// <summary>
/// 项目命名令牌（<see cref="NewProjectSettings.BuildRepositoryName"/> / <see cref="NewProjectSettings.BuildModulePath"/> /
/// <see cref="NewProjectSettings.BuildGroupName"/> / <see cref="NewProjectSettings.ExpandTokens"/>）单测：
/// 锁定「单仓名首字母大写、模块目录带端后缀、代码组名全大写」的默认口径。
/// </summary>
/// <remarks>
/// 回归护栏：<c>ExpandTokens</c> 曾用 <c>Replace("{code}", …, OrdinalIgnoreCase)</c>——它会连带把
/// <c>{Code}</c> / <c>{CODE}</c> 也替换成小写原文。这些用例保证三种令牌<b>互不串味</b>。
/// </remarks>
[TestClass]
public class NewProjectNamingUnitTest
{
    [TestMethod]
    public void BuildRepositoryName_DefaultFormat_CapitalizesFirstLetter()
    {
        var settings = new NewProjectSettings();

        // 单仓：一个项目只建一个仓。
        Assert.AreEqual("Csxmy-Mixed", settings.BuildRepositoryName("csxmy"));
    }

    [TestMethod]
    public void BuildModulePath_DefaultFormat_AppendsEndSuffix()
    {
        var settings = new NewProjectSettings();

        Assert.AreEqual("Csxmy-Service", settings.BuildModulePath("csxmy", ProjectEnd.Service));
        Assert.AreEqual("Csxmy-Admin", settings.BuildModulePath("csxmy", ProjectEnd.Admin));
        Assert.AreEqual("Csxmy-Mobile", settings.BuildModulePath("csxmy", ProjectEnd.Mobile));
    }

    [TestMethod]
    public void BuildRepositoryName_LowercaseFormat_KeepsOriginalCode()
    {
        var settings = new NewProjectSettings { RepositoryNameFormat = "{code}-repo" };

        Assert.AreEqual("csxmy-repo", settings.BuildRepositoryName("csxmy"));
    }

    [TestMethod]
    public void BuildRepositoryName_UppercaseFormat_UsesAllCaps()
    {
        var settings = new NewProjectSettings { RepositoryNameFormat = "{CODE}-REPO" };

        Assert.AreEqual("CSXMY-REPO", settings.BuildRepositoryName("csxmy"));
    }

    [TestMethod]
    public void BuildRepositoryName_BlankFormat_FallsBackToCapitalized()
    {
        var settings = new NewProjectSettings { RepositoryNameFormat = "   " };

        Assert.AreEqual("Csxmy-Mixed", settings.BuildRepositoryName("csxmy"));
    }

    [TestMethod]
    public void BuildModulePath_BlankFormat_FallsBackToEndSuffix()
    {
        var settings = new NewProjectSettings { ModulePathFormat = "   " };

        Assert.AreEqual("Csxmy-Service", settings.BuildModulePath("csxmy", ProjectEnd.Service));
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