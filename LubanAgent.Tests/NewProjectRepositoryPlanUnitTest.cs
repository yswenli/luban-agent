/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectRepositoryPlanUnitTest
*创建时间：2026/11/03
*描述：单仓建库计划的薄校验单测——锁死四条口径：
*      ① 三端共用同一仓库名**不算重复**（单仓形态，旧「仓库名必须不同」已废弃）；
*      ② 各端模块目录必须互不相同（同仓下的落点不能撞）；
*      ③ 各端仓库名不一致时报「单仓必须共用同一仓库名」；
 *      ④ 模块目录名与仓库名同一规则（不得含路径分隔符等非法字符）。
*
*****************************************************************************/

using System.Reflection;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectRepositoryPlanUnitTest
{
    private readonly List<string> _tempDirs = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响断言结果。
            }
        }
    }

    /// <summary>
    /// 造一个已开档（<see cref="NewProjectService.Current"/> 有值）的服务实例：
    /// 用反射把档案仓储换到临时目录，避免写入用户真实的插件状态文件。
    /// </summary>
    private NewProjectService NewService(params ProjectEnd[] ends)
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-newproject-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        var service = new NewProjectService();
        var field = typeof(NewProjectService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "NewProjectService._store 字段改名后需同步本测试的隔离方式。");
        field!.SetValue(service, new NewProjectStateStore(dir));

        service.StartNewProject("江南管理系统", "jnms", "用于单仓建库计划校验", ends, @"D:\ws\jnms");
        return service;
    }

    /// <summary>三端同名仓库（单仓）不判重复，且各端模块目录齐备时校验通过。</summary>
    [TestMethod]
    public async Task ValidateRepositoryPlanAsync_三端同名仓库不判重复()
    {
        var service = NewService(ProjectEnd.Service, ProjectEnd.Admin, ProjectEnd.Mobile);

        var result = await service.ValidateRepositoryPlanAsync(new[]
        {
            Plan(ProjectEnd.Service, "jnms-Mixed", "jnms-Service"),
            Plan(ProjectEnd.Admin, "jnms-Mixed", "jnms-Admin"),
            Plan(ProjectEnd.Mobile, "jnms-Mixed", "jnms-Mobile")
        });

        Assert.IsTrue(result.IsValid, string.Join("；", result.Errors));
    }

    /// <summary>各端模块目录撞名时报错（同仓下不能有同一个落点）。</summary>
    [TestMethod]
    public async Task ValidateRepositoryPlanAsync_模块目录重复报错()
    {
        var service = NewService(ProjectEnd.Service, ProjectEnd.Admin);

        var result = await service.ValidateRepositoryPlanAsync(new[]
        {
            Plan(ProjectEnd.Service, "jnms-Mixed", "jnms-Module"),
            Plan(ProjectEnd.Admin, "jnms-Mixed", "jnms-Module")
        });

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(string.Join("；", result.Errors), "模块目录");
        StringAssert.Contains(string.Join("；", result.Errors), "重复");
    }

    /// <summary>各端仓库名不一致时报错——单仓模式下只能是同一个仓。</summary>
    [TestMethod]
    public async Task ValidateRepositoryPlanAsync_仓库名不一致报错()
    {
        var service = NewService(ProjectEnd.Service, ProjectEnd.Admin);

        var result = await service.ValidateRepositoryPlanAsync(new[]
        {
            Plan(ProjectEnd.Service, "jnms-Mixed", "jnms-Service"),
            Plan(ProjectEnd.Admin, "jnms-Admin", "jnms-Admin")
        });

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(string.Join("；", result.Errors), "单仓模式下各端必须共用同一仓库名");
    }

    /// <summary>模块目录为空时报错，并给出默认值提示（默认名来自 <c>BuildModulePath</c>）。</summary>
    [TestMethod]
    public async Task ValidateRepositoryPlanAsync_模块目录为空报错()
    {
        var service = NewService(ProjectEnd.Service);

        var result = await service.ValidateRepositoryPlanAsync(new[]
        {
            Plan(ProjectEnd.Service, "jnms-Mixed", string.Empty)
        });

        Assert.IsFalse(result.IsValid);
        var text = string.Join("；", result.Errors);
        StringAssert.Contains(text, "模块目录：必填");
        StringAssert.Contains(text, "Jnms-Service");
    }

    /// <summary>模块目录名复用仓库名规则：含路径分隔符等非法字符时报错。</summary>
    [TestMethod]
    public async Task ValidateRepositoryPlanAsync_模块目录名非法报错()
    {
        var service = NewService(ProjectEnd.Service);

        var result = await service.ValidateRepositoryPlanAsync(new[]
        {
            Plan(ProjectEnd.Service, "jnms-Mixed", "jnms/Service")
        });

        Assert.IsFalse(result.IsValid);
        var text = string.Join("；", result.Errors);
        StringAssert.Contains(text, "模块目录");
        StringAssert.Contains(text, "不得含路径分隔符");
    }

    private static EndRepository Plan(ProjectEnd end, string repositoryName, string modulePath) => new()
    {
        End = end,
        RepositoryName = repositoryName,
        ModulePath = modulePath,
        Visibility = "internal"
    };
}
