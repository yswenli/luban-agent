/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStepReportUnitTest
*创建时间：2026/10/21
*描述：Step 4 / Step 5 结论文件单测——锁死 M4-P4 判定口径：
*      解析容错（正常 / 缺字段 / 坏 JSON 都不抛）、Step 4 失败不阻断、
*      Step 5 逐端归并与「漏端即失败」「事件流有错即失败」。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectStepReportUnitTest
{
    // ── 解析 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TryParse_正常结论_字段齐全()
    {
        const string Json = """
        {
          "step": 5,
          "ok": false,
          "checks": [
            { "name": "Service", "ok": true,  "detail": "编译通过", "command": "dotnet build A.sln" },
            { "name": "Admin",   "ok": false, "detail": "2 个错误", "command": "pnpm run build" }
          ],
          "message": "有一个端没过"
        }
        """;

        var ok = StepReportParser.TryParse(Json, out var report, out var error);

        Assert.IsTrue(ok, error);
        Assert.IsNotNull(report);
        Assert.AreEqual(5, report!.Step);
        Assert.IsFalse(report.Ok);
        Assert.AreEqual(2, report.Checks.Count);
        Assert.AreEqual("Service", report.Checks[0].Name);
        Assert.AreEqual("dotnet build A.sln", report.Checks[0].Command);
        Assert.AreEqual("Admin", report.Checks[1].Name);
        Assert.AreEqual("有一个端没过", report.Message);
    }

    [TestMethod]
    public void TryParse_缺字段_按默认值解析而非报错()
    {
        // 助手只写了 step：其余字段应落默认值，让「漏端」在下游被判失败，而不是解析层直接崩
        var ok = StepReportParser.TryParse("{\"step\":4}", out var report, out var error);

        Assert.IsTrue(ok, error);
        Assert.IsNotNull(report);
        Assert.AreEqual(4, report!.Step);
        Assert.IsFalse(report.Ok);
        Assert.AreEqual(0, report.Checks.Count);
        Assert.AreEqual(string.Empty, report.Message);
    }

    [TestMethod]
    public void TryParse_未知字段_忽略不抛()
    {
        var ok = StepReportParser.TryParse(
            "{\"step\":4,\"ok\":true,\"checks\":[],\"message\":\"ok\",\"extra\":{\"a\":1}}",
            out var report,
            out _);

        Assert.IsTrue(ok);
        Assert.IsTrue(report!.Ok);
    }

    [TestMethod]
    public void TryParse_坏JSON_返回false且不抛()
    {
        var ok = StepReportParser.TryParse("{ \"step\": 4, \"ok\": ", out var report, out var error);

        Assert.IsFalse(ok);
        Assert.IsNull(report);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error), "应给出可展示的解析错误。");
    }

    [TestMethod]
    public void TryParse_空内容_返回false()
    {
        Assert.IsFalse(StepReportParser.TryParse(null, out _, out _));
        Assert.IsFalse(StepReportParser.TryParse(string.Empty, out _, out _));
        Assert.IsFalse(StepReportParser.TryParse("   ", out _, out _));
    }

    [TestMethod]
    public void TryParse_结论文件不存在也等价于坏JSON()
    {
        // 助手没写文件 → 调用方读到的内容是 null/空 → 必须判失败而不是当成「无结论即通过」
        Assert.IsFalse(StepReportParser.TryParse(null, out var report, out var error));
        Assert.IsNull(report);
        StringAssert.Contains(error, "空");
    }

    // ── Step 4 归并（不阻断）───────────────────────────────────────────────

    [TestMethod]
    public void EvaluateProbe_通过且无错误事件_判通过()
    {
        var report = new StepReport
        {
            Step = 4,
            Ok = true,
            Checks = { new StepReportCheck { Name = "connect", Ok = true, Detail = "已连接且库存在" } },
            Message = "探活通过：能连上且库存在。"
        };

        var (ok, message) = StepReportEvaluator.EvaluateProbe(report, null);

        Assert.IsTrue(ok);
        StringAssert.Contains(message, "能连上且库存在");
    }

    [TestMethod]
    public void EvaluateProbe_库不存在_判未通过但给出可操作原因()
    {
        var report = new StepReport
        {
            Step = 4,
            Ok = false,
            Message = "探活未通过：1049 Unknown database 'tjc_dev_jnms'"
        };

        var (ok, message) = StepReportEvaluator.EvaluateProbe(report, null);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "1049");
    }

    [TestMethod]
    public void EvaluateProbe_助手自评通过但事件流有错_仍判失败()
    {
        var report = new StepReport { Step = 4, Ok = true, Message = "探活通过" };

        var (ok, message) = StepReportEvaluator.EvaluateProbe(report, new[] { "工具调用失败：pymysql 未安装" });

        Assert.IsFalse(ok, "四环齐才算通过：事件流有错误事件时不得放行。");
        StringAssert.Contains(message, "事件流");
    }

    [TestMethod]
    public void EvaluateProbe_步骤号不符_判失败()
    {
        var report = new StepReport { Step = 5, Ok = true };

        var (ok, message) = StepReportEvaluator.EvaluateProbe(report, null);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "步骤号不符");
    }

    [TestMethod]
    public void EvaluateProbe_未给原因时给默认文案()
    {
        var report = new StepReport { Step = 4, Ok = false };

        var (ok, message) = StepReportEvaluator.EvaluateProbe(report, null);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "未给出原因");
    }

    // ── Step 5 归并（阻断）───────────────────────────────────────────────

    private static readonly IReadOnlyList<ProjectEnd> TwoEnds = new[] { ProjectEnd.Service, ProjectEnd.Admin };

    [TestMethod]
    public void EvaluateBuild_两端齐全且全过_判通过()
    {
        var report = new StepReport
        {
            Step = 5,
            Ok = true,
            Checks =
            {
                new StepReportCheck { Name = "Service", Ok = true, Detail = "编译通过", Command = "dotnet build A.sln" },
                new StepReportCheck { Name = "Admin", Ok = true, Command = "pnpm run build" }
            }
        };

        var (ok, results, message) = StepReportEvaluator.EvaluateBuild(report, TwoEnds, null);

        Assert.IsTrue(ok);
        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(one => one.Ok));
        StringAssert.Contains(message, "2 端全部编译通过");

        var service = results.First(one => one.End == ProjectEnd.Service);
        Assert.AreEqual("dotnet build A.sln", service.Command);
        Assert.AreEqual("编译通过", service.Summary);

        // detail 缺省时给默认摘要（不能是空串）
        var admin = results.First(one => one.End == ProjectEnd.Admin);
        Assert.AreEqual("编译通过。", admin.Summary);
    }

    [TestMethod]
    public void EvaluateBuild_漏端_判失败且点名缺哪端()
    {
        var report = new StepReport
        {
            Step = 5,
            Ok = true,                                  // 助手自评「全过」，但漏写了 Admin
            Checks = { new StepReportCheck { Name = "Service", Ok = true } }
        };

        var (ok, results, message) = StepReportEvaluator.EvaluateBuild(report, TwoEnds, null);

        Assert.IsFalse(ok, "漏端必须判失败，不能用「其余端通过」兜底。");
        Assert.AreEqual(2, results.Count);
        var admin = results.First(one => one.End == ProjectEnd.Admin);
        Assert.IsFalse(admin.Ok);
        StringAssert.Contains(admin.Summary, "未给出该端的编译结论");
        StringAssert.Contains(message, "管理端");
    }

    [TestMethod]
    public void EvaluateBuild_某端失败_判失败并带错误摘要()
    {
        var report = new StepReport
        {
            Step = 5,
            Ok = false,
            Checks =
            {
                new StepReportCheck { Name = "Service", Ok = false, Detail = "3 个错误：CS0246 …", Command = "dotnet build A.sln" },
                new StepReportCheck { Name = "Admin", Ok = true }
            }
        };

        var (ok, results, message) = StepReportEvaluator.EvaluateBuild(report, TwoEnds, null);

        Assert.IsFalse(ok);
        var service = results.First(one => one.End == ProjectEnd.Service);
        Assert.AreEqual("3 个错误：CS0246 …", service.Summary);
        StringAssert.Contains(message, "后端");
    }

    [TestMethod]
    public void EvaluateBuild_端标识忽略大小写()
    {
        var report = new StepReport
        {
            Step = 5,
            Ok = true,
            Checks =
            {
                new StepReportCheck { Name = " service ", Ok = true },
                new StepReportCheck { Name = "admin", Ok = true }
            }
        };

        var (ok, results, _) = StepReportEvaluator.EvaluateBuild(report, TwoEnds, null);

        Assert.IsTrue(ok);
        Assert.IsTrue(results.All(one => one.Ok));
    }

    [TestMethod]
    public void EvaluateBuild_事件流有错_判失败()
    {
        var report = new StepReport
        {
            Step = 5,
            Ok = true,
            Checks =
            {
                new StepReportCheck { Name = "Service", Ok = true },
                new StepReportCheck { Name = "Admin", Ok = true }
            }
        };

        var (ok, _, message) = StepReportEvaluator.EvaluateBuild(report, TwoEnds, new[] { "命令被拒：docker" });

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "事件流");
    }

    [TestMethod]
    public void EvaluateBuild_步骤号不符_判失败且无逐端结果()
    {
        var report = new StepReport { Step = 4, Ok = true };

        var (ok, results, message) = StepReportEvaluator.EvaluateBuild(report, TwoEnds, null);

        Assert.IsFalse(ok);
        Assert.AreEqual(0, results.Count);
        StringAssert.Contains(message, "步骤号不符");
    }

    [TestMethod]
    public void EvaluateBuild_没有任何端_判失败()
    {
        var report = new StepReport { Step = 5, Ok = true };

        var (ok, results, _) = StepReportEvaluator.EvaluateBuild(report, Array.Empty<ProjectEnd>(), null);

        Assert.IsFalse(ok, "没有端可编译时不得判通过（避免空跑被静默放过）。");
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void EvaluateBuild_错误摘要压成一句最多三条()
    {
        var report = new StepReport { Step = 5, Ok = true, Checks = { new StepReportCheck { Name = "Service", Ok = true } } };

        var errors = new[] { "e1", "e2", "e3", "e4" };
        var (ok, _, message) = StepReportEvaluator.EvaluateBuild(report, new[] { ProjectEnd.Service }, errors);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "4 条错误");
        StringAssert.Contains(message, "e3");
        Assert.IsFalse(message.Contains("e4"), "最多展开 3 条细节，避免记录被刷屏。");
    }
}