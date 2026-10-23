/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStepReportDecisionUnitTest
*创建时间：2026/10/22
*描述：v0.3 助手驱动扩展单测——锁死三件事：
*      ① 结论文件扩展 schema（status / inputs / artifacts + 三个具名 extra）的源生成解析；
*      ② 三个新判定入口（EvaluateRepositorySetup / EvaluatePush / EvaluatePipeline）的
*         done / needsInput / failed 三态与逐端完备性；
*      ③ ResolveStatus 的向后兼容（旧 Step 4/5 结论没有 status，按 ok 兜底）。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectStepReportDecisionUnitTest
{
    private static readonly List<ProjectEnd> BothEnds = [ProjectEnd.Service, ProjectEnd.Admin];

    private static StepReport Parse(string json)
        => StepReportParser.TryParse(json, out var report, out var error)
            ? report!
            : throw new AssertFailedException($"结论文件解析失败：{error}");

    private static readonly string RepositoryOkJson = """
    {
      "step": 3,
      "status": "done",
      "ok": true,
      "message": "建库并克隆完成",
      "artifacts": [
        { "kind": "group", "name": "jnms", "externalId": "100", "webUrl": "https://codeup.aliyun.com/g/100",
          "extra": { "kind": "group", "exists": true, "parentId": 7, "organizationId": "org-1" } },
        { "kind": "repository", "end": "Service", "name": "jnms-Service", "externalId": "201",
          "cloneUrl": "https://codeup.aliyun.com/x/jnms-Service.git", "localPath": "D:\\ws\\jnms\\jnms-Service",
          "templateBranch": "develop",
          "extra": { "kind": "repository", "templateRepository": "TJCES-Service", "templateProjectId": 11,
                     "resolvedBranchSource": "配置登记", "placeholderSources": ["TJCES", "Tjces"],
                     "cloned": true, "defaultBranch": "develop" } },
        { "kind": "repository", "end": "Admin", "name": "jnms-Admin", "externalId": "202",
          "cloneUrl": "https://codeup.aliyun.com/x/jnms-Admin.git", "localPath": "D:\\ws\\jnms\\jnms-Admin",
          "templateBranch": "develop",
          "extra": { "kind": "repository", "templateRepository": "TJCES-Admin",
                     "placeholderSources": ["TJCES"], "cloned": true, "defaultBranch": "develop" } }
      ]
    }
    """;

    // ── 扩展 schema 解析 ───────────────────────────────────────────────────

    [TestMethod]
    public void TryParse_扩展schema_三态与具名产物齐备()
    {
        var report = Parse(RepositoryOkJson);

        Assert.AreEqual(3, report.Step);
        Assert.AreEqual(StepReportStatus.Done, report.Status);
        Assert.IsTrue(report.Ok);
        Assert.AreEqual(3, report.Artifacts.Count);

        var group = report.Artifacts.Single(one => one.Kind == "group");
        Assert.IsInstanceOfType<GroupArtifactExtra>(group.Extra);
        Assert.IsTrue(((GroupArtifactExtra)group.Extra!).Exists);
        Assert.AreEqual(7, ((GroupArtifactExtra)group.Extra!).ParentId);

        var extra = StepReportEvaluator.RepositoryExtra(report, ProjectEnd.Service);
        Assert.IsNotNull(extra);
        Assert.AreEqual("TJCES-Service", extra!.TemplateRepository);
        Assert.IsTrue(extra.Cloned);
        CollectionAssert.AreEqual(new[] { "TJCES", "Tjces" }, extra.PlaceholderSources.ToArray());
        Assert.AreEqual("https://codeup.aliyun.com/x/jnms-Service.git",
            StepReportEvaluator.RepositoryArtifact(report, ProjectEnd.Service)!.CloneUrl);
    }

    [TestMethod]
    public void TryParse_inputs_needsInput交互项()
    {
        const string Json = """
        {
          "step": 3, "status": "needsInput", "ok": false,
          "inputs": [
            { "id": "clone-conflict", "kind": "choice", "prompt": "本地目录已存在，请选择处置方式",
              "ends": ["Service"],
              "options": [
                { "value": "reuse", "label": "复用现有目录" },
                { "value": "reclone", "label": "清空后重新克隆" },
                { "value": "abort", "label": "中止本次执行" } ],
              "details": { "path": "D:\\ws\\jnms\\jnms-Service", "entryCount": 42 } }
          ],
          "artifacts": []
        }
        """;

        var report = Parse(Json);

        Assert.AreEqual(StepReportStatus.NeedsInput, StepReportEvaluator.ResolveStatus(report));
        Assert.AreEqual(1, report.Inputs.Count);

        var input = report.Inputs[0];
        Assert.AreEqual("clone-conflict", input.Id);
        Assert.AreEqual("choice", input.Kind);
        CollectionAssert.AreEqual(new[] { ProjectEnd.Service }, input.Ends.ToArray());
        Assert.AreEqual(3, input.Options.Count);
        Assert.AreEqual(42, input.Details!.EntryCount);
    }

    [TestMethod]
    public void ResolveStatus_无status_按ok兜底_向后兼容Step4与5()
    {
        Assert.AreEqual(StepReportStatus.Done, StepReportEvaluator.ResolveStatus(new StepReport { Ok = true }));
        Assert.AreEqual(StepReportStatus.Failed, StepReportEvaluator.ResolveStatus(new StepReport { Ok = false }));
    }

    [TestMethod]
    public void ResolveStatus_三态_忽略大小写与首尾空白()
    {
        Assert.AreEqual(StepReportStatus.Done, StepReportEvaluator.ResolveStatus(new StepReport { Status = " DONE " }));
        Assert.AreEqual(StepReportStatus.NeedsInput, StepReportEvaluator.ResolveStatus(new StepReport { Status = "needsinput" }));
        Assert.AreEqual(StepReportStatus.Failed, StepReportEvaluator.ResolveStatus(new StepReport { Status = "Failed" }));
    }

    // ── Step 3：建库并克隆（EvaluateRepositorySetup）────────────────────────

    [TestMethod]
    public void EvaluateRepositorySetup_准备阶段_端产物齐备即通过()
    {
        var report = Parse(RepositoryOkJson);

        var (ok, status, _) = StepReportEvaluator.EvaluateRepositorySetup(report, BothEnds, null, null, requireCloned: false);

        Assert.IsTrue(ok);
        Assert.AreEqual(StepReportStatus.Done, status);
    }

    [TestMethod]
    public void EvaluateRepositorySetup_执行续跑_要求逐端cloned()
    {
        var report = Parse(RepositoryOkJson);

        var (ok, _, _) = StepReportEvaluator.EvaluateRepositorySetup(report, BothEnds, null, null, requireCloned: true);

        Assert.IsTrue(ok, "两端都 cloned=true 且无 error，应通过。");

        // 把 Admin 的 cloned 抹掉（改写成 ok 但不 cloned）
        var notCloned = Parse(RepositoryOkJson.Replace("\"placeholderSources\": [\"TJCES\"], \"cloned\": true", "\"cloned\": false"));
        var (ok2, status2, message2) = StepReportEvaluator.EvaluateRepositorySetup(notCloned, BothEnds, null, null, requireCloned: true);

        Assert.IsFalse(ok2);
        Assert.AreEqual(StepReportStatus.Failed, status2);
        StringAssert.Contains(message2, "cloned=true");
        StringAssert.Contains(message2, "管理端");
    }

    [TestMethod]
    public void EvaluateRepositorySetup_缺端产物_整步失败()
    {
        const string Json = """
        {
          "step": 3, "status": "done", "ok": true,
          "artifacts": [
            { "kind": "repository", "end": "Service", "name": "jnms-Service",
              "extra": { "kind": "repository", "cloned": true } } ]
        }
        """;
        var report = Parse(Json);

        var (ok, _, message) = StepReportEvaluator.EvaluateRepositorySetup(report, BothEnds, null, null, requireCloned: true);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "缺少端产物");
        StringAssert.Contains(message, "管理端");
    }

    [TestMethod]
    public void EvaluateRepositorySetup_needsInput_不通过且状态为needsInput()
    {
        const string Json = """
        {
          "step": 3, "status": "needsInput", "ok": false,
          "inputs": [ { "id": "clone-conflict", "kind": "choice", "prompt": "选择处置方式" } ],
          "artifacts": []
        }
        """;
        var report = Parse(Json);

        var (ok, status, message) = StepReportEvaluator.EvaluateRepositorySetup(report, BothEnds, null);

        Assert.IsFalse(ok);
        Assert.AreEqual(StepReportStatus.NeedsInput, status);
        StringAssert.Contains(message, "需要用户补充输入");
    }

    [TestMethod]
    public void EvaluateRepositorySetup_本地复核问题或事件流错误_失败()
    {
        var report = Parse(RepositoryOkJson);

        var (okLocal, _, messageLocal) = StepReportEvaluator.EvaluateRepositorySetup(
            report, BothEnds, null, new[] { "后端：占位符残余未清零（扫过 120 个文件，命中 3 处）。" }, true);
        Assert.IsFalse(okLocal);
        StringAssert.Contains(messageLocal, "本地复核未通过");

        var (okErr, _, messageErr) = StepReportEvaluator.EvaluateRepositorySetup(
            report, BothEnds, new[] { "git push 失败" }, null, true);
        Assert.IsFalse(okErr);
        StringAssert.Contains(messageErr, "本轮事件流");

        // 空白项被忽略（守卫拒绝不计入）
        var (okBlank, _, _) = StepReportEvaluator.EvaluateRepositorySetup(report, BothEnds, new[] { "   " }, null, true);
        Assert.IsTrue(okBlank);
    }

    [TestMethod]
    public void EvaluateRepositorySetup_无结论或步骤号不符_失败()
    {
        var (okNull, _, messageNull) = StepReportEvaluator.EvaluateRepositorySetup(null, BothEnds, null);
        Assert.IsFalse(okNull);
        StringAssert.Contains(messageNull, "未生成结论文件");

        var report = Parse(RepositoryOkJson);
        report.Step = 6;
        var (okStep, _, messageStep) = StepReportEvaluator.EvaluateRepositorySetup(report, BothEnds, null);
        Assert.IsFalse(okStep);
        StringAssert.Contains(messageStep, "步骤号不符");
    }

    // ── Step 6：推送（EvaluatePush）────────────────────────────────────────

    private const string PushOkJson = """
    {
      "step": 6, "status": "done", "ok": true,
      "checks": [
        { "name": "Service", "ok": true, "detail": "已推送 develop" },
        { "name": "Admin",   "ok": true, "detail": "已推送 develop" } ]
    }
    """;

    [TestMethod]
    public void EvaluatePush_逐端通过_通过()
    {
        var report = Parse(PushOkJson);

        var (ok, status, _) = StepReportEvaluator.EvaluatePush(report, BothEnds, null);

        Assert.IsTrue(ok);
        Assert.AreEqual(StepReportStatus.Done, status);
    }

    [TestMethod]
    public void EvaluatePush_漏端_整步失败()
    {
        const string Json = """
        {
          "step": 6, "status": "done", "ok": true,
          "checks": [ { "name": "Service", "ok": true, "detail": "已推送" } ]
        }
        """;
        var report = Parse(Json);

        var (ok, _, message) = StepReportEvaluator.EvaluatePush(report, BothEnds, null);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "缺少端结论");
        StringAssert.Contains(message, "管理端");
    }

    [TestMethod]
    public void EvaluatePush_有一端失败_带出该端详情()
    {
        // 自评 ok=true 但逐端 checks 里有一端失败：仍拦得住，且带出该端详情
        const string Json = """
        {
          "step": 6, "status": "done", "ok": true,
          "checks": [
            { "name": "Service", "ok": true,  "detail": "已推送" },
            { "name": "Admin",   "ok": false, "detail": "! [rejected] non-fast-forward" } ]
        }
        """;
        var report = Parse(Json);

        var (ok, _, message) = StepReportEvaluator.EvaluatePush(report, BothEnds, null);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "non-fast-forward");
        StringAssert.Contains(message, "管理端");
    }

    [TestMethod]
    public void EvaluatePush_ok为false_即便status为done也判失败()
    {
        // 自相矛盾的自报（status=done 但 ok=false）：闸门以 ok 为准，判失败
        const string Json = """
        { "step": 6, "status": "done", "ok": false, "message": "管理端被远端拒绝" }
        """;
        var report = Parse(Json);

        var (ok, status, message) = StepReportEvaluator.EvaluatePush(report, BothEnds, null);

        Assert.IsFalse(ok);
        Assert.AreEqual(StepReportStatus.Failed, status);
        StringAssert.Contains(message, "管理端被远端拒绝");
    }

    [TestMethod]
    public void EvaluatePush_非done状态_失败()
    {
        const string Json = """
        { "step": 6, "status": "failed", "ok": false, "message": "远端拒绝了推送" }
        """;
        var report = Parse(Json);

        var (ok, status, _) = StepReportEvaluator.EvaluatePush(report, BothEnds, null);

        Assert.IsFalse(ok);
        Assert.AreEqual(StepReportStatus.Failed, status);
    }

    // ── Step 7：流水线（EvaluatePipeline）──────────────────────────────────

    private const string PipelineOkJson = """
    {
      "step": 7, "status": "done", "ok": true,
      "artifacts": [
        { "kind": "pipeline", "end": "Service", "name": "jnms-Service-Deploy", "yaml": "stages:\n  - stage: build",
          "extra": { "kind": "pipeline", "yamlIncludesDeployStage": true,
                     "recommendedDeployTargetUuid": "uuid-1", "recommendedDeployTargetHostGroupId": 9 } },
        { "kind": "pipeline", "end": "Admin", "name": "jnms-Admin-Deploy", "yaml": "stages:\n  - stage: build",
          "extra": { "kind": "pipeline", "yamlIncludesDeployStage": false } }
      ]
    }
    """;

    [TestMethod]
    public void EvaluatePipeline_逐端产物齐备_通过()
    {
        var report = Parse(PipelineOkJson);

        var (ok, status, _) = StepReportEvaluator.EvaluatePipeline(report, BothEnds, null);

        Assert.IsTrue(ok);
        Assert.AreEqual(StepReportStatus.Done, status);

        var extra = StepReportEvaluator.PipelineExtra(report, ProjectEnd.Service);
        Assert.IsNotNull(extra);
        Assert.IsTrue(extra!.YamlIncludesDeployStage);
        Assert.AreEqual(9, extra.RecommendedDeployTargetHostGroupId);
    }

    [TestMethod]
    public void EvaluatePipeline_缺端或空YAML_失败()
    {
        const string Missing = """
        { "step": 7, "status": "done", "ok": true,
          "artifacts": [ { "kind": "pipeline", "end": "Service", "name": "x", "yaml": "stages:", "extra": { "kind": "pipeline" } } ] }
        """;
        var (okMissing, _, messageMissing) = StepReportEvaluator.EvaluatePipeline(Parse(Missing), BothEnds, null);
        Assert.IsFalse(okMissing);
        StringAssert.Contains(messageMissing, "缺少端流水线产物");

        const string Blank = """
        { "step": 7, "status": "done", "ok": true,
          "artifacts": [
            { "kind": "pipeline", "end": "Service", "name": "x", "yaml": "stages:", "extra": { "kind": "pipeline" } },
            { "kind": "pipeline", "end": "Admin", "name": "y", "yaml": "   ", "extra": { "kind": "pipeline" } } ] }
        """;
        var (okBlank, _, messageBlank) = StepReportEvaluator.EvaluatePipeline(Parse(Blank), BothEnds, null);
        Assert.IsFalse(okBlank);
        StringAssert.Contains(messageBlank, "未给出流水线 YAML");
    }

    [TestMethod]
    public void EvaluatePipeline_含部署阶段但留未选择占位_失败()
    {
        const string Json = """
        {
          "step": 7, "status": "done", "ok": true,
          "artifacts": [
            { "kind": "pipeline", "end": "Service", "name": "jnms-Service",
              "yaml": "stages:\n  - deploy:\n      machineGroup: <未选择>",
              "extra": { "kind": "pipeline", "yamlIncludesDeployStage": true } } ]
        }
        """;

        var (ok, _, message) = StepReportEvaluator.EvaluatePipeline(Parse(Json), [ProjectEnd.Service], null);

        Assert.IsFalse(ok);
        StringAssert.Contains(message, "未选定部署目标");
    }

    [TestMethod]
    public void EvaluatePipeline_不含部署阶段_未选择字样不阻断()
    {
        // 管理端模板本身无独立部署阶段：即便 YAML 里出现「未选择」字样的注释也不该被当占位拦下
        const string Json = """
        {
          "step": 7, "status": "done", "ok": true,
          "artifacts": [
            { "kind": "pipeline", "end": "Admin", "name": "jnms-Admin",
              "yaml": "stages:\n  - stage: build\n    # machineGroup: <未选择>（本端模板无部署阶段）\n",
              "extra": { "kind": "pipeline", "yamlIncludesDeployStage": false } } ]
        }
        """;

        var (ok, _, _) = StepReportEvaluator.EvaluatePipeline(Parse(Json), [ProjectEnd.Admin], null);

        Assert.IsTrue(ok);
    }
}