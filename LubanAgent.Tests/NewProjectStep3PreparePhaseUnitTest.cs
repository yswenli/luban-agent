/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStep3PreparePhaseUnitTest
*创建时间：2026/10/23
*描述：方案 A 单测——Step 3 准备（只读）与执行两阶段的「文件名 + 状态」双重隔离：
*      ① 准备结论叫 step3-prepare-<时间戳>.json，执行结论仍叫 step3-<时间戳>.json；
*      ② List / FindLatest 只看执行结论，准备结论必须走 ListPrepare / FindLatestPrepare；
*      ③ 判定入口：准备阶段只接受 ready（兼容旧 done）、拒绝 blocked；
*         执行阶段拒绝 phase=prepare / status=ready|blocked，只认 done。
*      为什么必须锁死：两阶段同文件名同 status 语义时，助手会把准备结论当「本步已完成」，
*      真机 Step 3 因此回 DONE 且零写动作。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectStep3PreparePhaseUnitTest
{
    private static readonly List<ProjectEnd> BothEnds = [ProjectEnd.Service, ProjectEnd.Admin];

    private static StepReport Parse(string json)
        => StepReportParser.TryParse(json, out var report, out var error)
            ? report!
            : throw new AssertFailedException($"结论文件解析失败：{error}");

    private static string PrepareJson(string status, bool ok, string message) => $$"""
    {
      "step": 3,
      "phase": "prepare",
      "status": "{{status}}",
      "ok": {{(ok ? "true" : "false")}},
      "message": "{{message}}",
      "artifacts": [
        { "kind": "group", "name": "csxmy", "externalId": null,
          "extra": { "kind": "group", "exists": false, "parentId": null, "organizationId": "org-1" } },
        { "kind": "repository", "end": "Service", "name": "csxmy-Service", "localPath": "D:\\ws\\csxmy\\csxmy-Service",
          "extra": { "kind": "repository", "templateRepository": "TJCES-Service", "templateProjectId": 11 } },
        { "kind": "repository", "end": "Admin", "name": "csxmy-Admin", "localPath": "D:\\ws\\csxmy\\csxmy-Admin",
          "extra": { "kind": "repository", "templateRepository": "TJCES-Admin", "templateProjectId": 12 } }
      ]
    }
    """;

    private static readonly string ExecuteOkJson = """
    {
      "step": 3,
      "phase": "execute",
      "status": "done",
      "ok": true,
      "message": "建库并克隆完成",
      "artifacts": [
        { "kind": "group", "name": "csxmy", "externalId": "100",
          "extra": { "kind": "group", "exists": true, "parentId": 7, "organizationId": "org-1" } },
        { "kind": "repository", "end": "Service", "name": "csxmy-Service", "externalId": "201",
          "cloneUrl": "https://codeup.aliyun.com/x/csxmy-Service.git", "localPath": "D:\\ws\\csxmy\\csxmy-Service",
          "extra": { "kind": "repository", "templateRepository": "TJCES-Service", "templateProjectId": 11,
                     "placeholderSources": ["TJCES"], "cloned": true, "defaultBranch": "develop" } },
        { "kind": "repository", "end": "Admin", "name": "csxmy-Admin", "externalId": "202",
          "cloneUrl": "https://codeup.aliyun.com/x/csxmy-Admin.git", "localPath": "D:\\ws\\csxmy\\csxmy-Admin",
          "extra": { "kind": "repository", "templateRepository": "TJCES-Admin", "templateProjectId": 12,
                     "placeholderSources": ["TJCES"], "cloned": true, "defaultBranch": "develop" } }
      ]
    }
    """;

    // ── 结论文件命名与列举 ─────────────────────────────────────────────────

    [TestMethod]
    public void 结论文件_Prepare阶段独立命名且不被当作执行结论()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-step3-" + Guid.NewGuid().ToString("N"));
        try
        {
            var prepareFile = StepResultFile.BuildPath(root, 3, StepResultFile.PreparePhase);
            StepResultFile.EnsureDirectory(prepareFile);
            File.WriteAllText(prepareFile, "{}");

            var executeFile = StepResultFile.BuildPath(root, 3);
            StepResultFile.EnsureDirectory(executeFile);
            File.WriteAllText(executeFile, "{}");

            Assert.IsTrue(Path.GetFileName(prepareFile).StartsWith("step3-prepare-"), Path.GetFileName(prepareFile));
            Assert.IsFalse(Path.GetFileName(executeFile).StartsWith("step3-prepare-"), Path.GetFileName(executeFile));
            Assert.IsTrue(StepResultFile.IsPrepare(prepareFile, 3));
            Assert.IsFalse(StepResultFile.IsPrepare(executeFile, 3));

            CollectionAssert.AreEqual(new[] { executeFile }, StepResultFile.List(root, 3).ToArray(),
                "List 只能回执行结论（准备结论会污染「本步是否已完成」的判定）。");
            CollectionAssert.AreEqual(new[] { prepareFile }, StepResultFile.ListPrepare(root, 3).ToArray());

            Assert.AreEqual(executeFile, StepResultFile.FindLatest(root, 3));
            Assert.AreEqual(prepareFile, StepResultFile.FindLatestPrepare(root, 3));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void 结论文件_作废执行结论不影响准备结论()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-step3-" + Guid.NewGuid().ToString("N"));
        try
        {
            var prepareFile = StepResultFile.BuildPath(root, 3, StepResultFile.PreparePhase);
            StepResultFile.EnsureDirectory(prepareFile);
            File.WriteAllText(prepareFile, "{}");

            var executeFile = StepResultFile.BuildPath(root, 3);
            File.WriteAllText(executeFile, "{}");

            Assert.IsTrue(StepResultFile.TryDeleteLatest(root, 3));
            Assert.IsFalse(File.Exists(executeFile));
            Assert.IsTrue(File.Exists(prepareFile), "作废执行结论时不得删掉准备结论。");
            Assert.IsNull(StepResultFile.FindLatest(root, 3));
            Assert.AreEqual(prepareFile, StepResultFile.FindLatestPrepare(root, 3));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // ── 判定入口的阶段区分 ────────────────────────────────────────────────

    [TestMethod]
    public void 准备阶段_ready通过()
    {
        var report = Parse(PrepareJson(StepReportStatus.Ready, true, "准备就绪"));

        var (ok, _, message) = StepReportEvaluator.EvaluateRepositorySetup(report, BothEnds, assistantErrors: null);

        Assert.IsTrue(ok, message);
    }

    [TestMethod]
    public void 准备阶段_blocked阻断()
    {
        var report = Parse(PrepareJson(StepReportStatus.Blocked, false, "模板仓缺失，无法继续"));

        var (ok, _, message) = StepReportEvaluator.EvaluateRepositorySetup(report, BothEnds, assistantErrors: null);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "模板仓缺失");
    }

    [TestMethod]
    public void 执行阶段_读到准备结论即判未执行()
    {
        var report = Parse(PrepareJson(StepReportStatus.Ready, true, "准备就绪"));

        var (ok, _, message) = StepReportEvaluator.EvaluateRepositorySetup(
            report, BothEnds, assistantErrors: null, requireCloned: true);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "准备（只读）阶段结论");
    }

    [TestMethod]
    public void 执行阶段_done且逐端克隆齐备通过()
    {
        var report = Parse(ExecuteOkJson);

        var (ok, _, message) = StepReportEvaluator.EvaluateRepositorySetup(
            report, BothEnds, assistantErrors: null, requireCloned: true);

        Assert.IsTrue(ok, message);
    }
}