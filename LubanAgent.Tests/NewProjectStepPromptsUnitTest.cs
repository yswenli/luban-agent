/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStepPromptsUnitTest
*创建时间：2026/10/21
*描述：Step 4 / Step 5 提示词单测——锁死 M4-P4 定案：
*      「口令内联（方案 A）」「禁止回头调用 newproject-* 技能与服务（同会话回合自等死锁）」
*      「结论文件路径原样下发」「Step 5 逐端清单与端标识」。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectStepPromptsUnitTest
{
    private const string ResultFile = @"D:\ws\jnms\.luban-agent\plugin-newproject\results\step4-20261021-101010-123.json";

    /// <summary>造一份最小可用档案。</summary>
    private static ProjectSpec Spec() => new()
    {
        Name = "江南管理系统",
        Code = "jnms",
        LocalPath = @"D:\ws\jnms",
        Ends = new List<ProjectEnd> { ProjectEnd.Service, ProjectEnd.Admin }
    };

    [TestMethod]
    public void BuildDatabaseProbePrompt_失败时口令明文内联且不复述约束到位()
    {
        var request = new DatabaseProbeRequest
        {
            Host = "10.10.0.214",
            Port = 3306,
            Database = "tjc_dev_jnms",
            User = "testuser",
            Password = "S3cr3t-Pwd"
        };

        var prompt = StepPrompts.BuildDatabaseProbePrompt(Spec(), request, ResultFile);

        // 方案 A（用户定案）：口令内联进提示词；同时要求「不写文件 / 不回显」
        StringAssert.Contains(prompt, "password = S3cr3t-Pwd");
        StringAssert.Contains(prompt, "不要写进任何文件");
        StringAssert.Contains(prompt, "不要回显");

        // 参数齐全，助手无需再猜
        StringAssert.Contains(prompt, "server   = 10.10.0.214");
        StringAssert.Contains(prompt, "database = tjc_dev_jnms");
        StringAssert.Contains(prompt, "user     = testuser");

        // 结论文件路径原样下发
        StringAssert.Contains(prompt, ResultFile);
    }

    [TestMethod]
    public void BuildDatabaseProbePrompt_只探活不建库不写文件不阻断()
    {
        var prompt = StepPrompts.BuildDatabaseProbePrompt(Spec(), new DatabaseProbeRequest
        {
            Host = "h",
            Database = "d",
            User = "u"
        }, ResultFile);

        StringAssert.Contains(prompt, "只做一件事");            // 只探活
        StringAssert.Contains(prompt, "禁止建库");
        StringAssert.Contains(prompt, "禁止执行任何 SQL");
        StringAssert.Contains(prompt, "CREATE");                 // 明确点名 DDL 家族
        StringAssert.Contains(prompt, "DROP");
        StringAssert.Contains(prompt, "禁止修改项目里的任何文件");
        StringAssert.Contains(prompt, "不阻断");                  // 失败只记录、不拦流程
    }

    [TestMethod]
    public void BuildDatabaseProbePrompt_禁止回头调用插件技能与服务()
    {
        // 同会话回合串行：助手若回头调 newproject-* / 插件服务会自等死锁
        var prompt = StepPrompts.BuildDatabaseProbePrompt(Spec(), new DatabaseProbeRequest
        {
            Host = "h",
            Database = "d",
            User = "u"
        }, ResultFile);

        StringAssert.Contains(prompt, "newproject-*");
        StringAssert.Contains(prompt, "同会话回合");
        StringAssert.Contains(prompt, "自等死锁");
        StringAssert.Contains(prompt, "机器判定一律以下面的结论文件为准");
    }

    [TestMethod]
    public void BuildCompilePrompt_逐端清单带本地目录与端标识()
    {
        var targets = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "jnms-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" },
            new() { End = ProjectEnd.Admin, RepositoryName = "jnms-Admin", LocalPath = @"D:\ws\jnms\csxmy-Admin" }
        };

        var prompt = StepPrompts.BuildCompilePrompt(Spec(), targets, ResultFile);

        StringAssert.Contains(prompt, "name = Service（后端）");
        StringAssert.Contains(prompt, "本地目录 = D:\\ws\\jnms\\Csxmy-Service");
        StringAssert.Contains(prompt, "name = Admin（管理端）");
        StringAssert.Contains(prompt, "本地目录 = D:\\ws\\jnms\\csxmy-Admin");
        StringAssert.Contains(prompt, ResultFile);
    }

    [TestMethod]
    public void BuildCompilePrompt_编译方式交助手判断且不修复不改代码()
    {
        var targets = new List<EndRepository>
        {
            new() { End = ProjectEnd.Admin, RepositoryName = "jnms-Admin", LocalPath = @"D:\ws\jnms\csxmy-Admin" }
        };

        var prompt = StepPrompts.BuildCompilePrompt(Spec(), targets, ResultFile);

        // PRD §4.3 Step 5（v0.22）：编译方式由助手按工程类型决定
        StringAssert.Contains(prompt, "编译方式由你按工程类型决定");
        StringAssert.Contains(prompt, "dotnet build");
        StringAssert.Contains(prompt, "pnpm");

        // 只编译不修复
        StringAssert.Contains(prompt, "只编译、不修复");
        StringAssert.Contains(prompt, "禁止修改任何源码");

        // 命令白名单（与 AssistantToolPolicy 同口径）
        StringAssert.Contains(prompt, "白名单");
        StringAssert.Contains(prompt, "dotnet");
        StringAssert.Contains(prompt, "pip");
    }

    [TestMethod]
    public void BuildCompilePrompt_漏端即失败的口径写进提示词()
    {
        var targets = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "jnms-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" }
        };

        var prompt = StepPrompts.BuildCompilePrompt(Spec(), targets, ResultFile);

        StringAssert.Contains(prompt, "checks 必须逐端齐全");
        StringAssert.Contains(prompt, "漏写某个端");
        StringAssert.Contains(prompt, "本步失败");
        StringAssert.Contains(prompt, "\"step\": 5");
    }

    [TestMethod]
    public void BuildDatabaseProbePrompt_结论文件schema写死step4()
    {
        var prompt = StepPrompts.BuildDatabaseProbePrompt(Spec(), new DatabaseProbeRequest
        {
            Host = "h",
            Database = "d",
            User = "u"
        }, ResultFile);

        StringAssert.Contains(prompt, "\"step\": 4");
        StringAssert.Contains(prompt, "\"checks\"");
        StringAssert.Contains(prompt, "\"ok\"");
        StringAssert.Contains(prompt, "pymysql");
    }
}