/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStepGateUnitTest
*创建时间：2026/10/21
*描述：步骤前置闸门与推进指针单测——锁死 C7′ 收口口径：
*      ① 档案 CurrentStep 单调只前进（回退重跑不得让进度倒退）；
*      ② 推送（Step 6）必须复核「编译（Step 5）已全部通过」——判据改读最近一份
*         results/step5-*.json 结论文件（含逐端完备性），绕开界面顺序也拦得住；
*      ③ 创建流水线（Step 7）必须复核「推送（Step 6）已成功」——读 results/step6-*.json；
*      ④ 结论作废：Step 3 重跑后删除最近一份结论文件，闸门立刻转拒绝。
*      回归背景：真机冒烟出现「5 编译 未开始 / 6 推送 已完成」的自相矛盾状态。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectStepGateUnitTest
{
    private const string BuildOk = """
    { "step": 5, "ok": true, "checks": [
        { "name": "Service", "ok": true, "detail": "编译通过", "command": "dotnet build A.sln" },
        { "name": "Admin",   "ok": true, "detail": "编译通过", "command": "pnpm build" } ] }
    """;

    private const string PushOk = """
    { "step": 6, "status": "done", "ok": true, "checks": [
        { "name": "Service", "ok": true, "detail": "推送成功" },
        { "name": "Admin",   "ok": true, "detail": "推送成功" } ] }
    """;

    private readonly List<string> _workspaces = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _workspaces)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // 临时目录，清理失败不影响断言。
            }
        }

        _workspaces.Clear();
    }

    private string NewWorkspace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-np-gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _workspaces.Add(dir);
        return dir;
    }

    private static void WriteConclusion(string workspace, int step, string json)
    {
        var path = StepResultFile.BuildPath(workspace, step);
        StepResultFile.EnsureDirectory(path);
        File.WriteAllText(path, json);
    }

    private static ProjectSpec Spec(string workspace, params ProjectEnd[] ends)
        => new() { LocalPath = workspace, Ends = ends.ToList() };

    // ── 推进指针：单调只前进 ────────────────────────────────────────────────

    [TestMethod]
    public void AdvanceTo_前向推进_正常前移()
    {
        var spec = new ProjectSpec { CurrentStep = WizardStep.BasicInfo };

        spec.AdvanceTo(WizardStep.Build);

        Assert.AreEqual(WizardStep.Build, spec.CurrentStep);
    }

    [TestMethod]
    public void AdvanceTo_回退重跑_不得让进度倒退()
    {
        // 场景：档案已到流水线（Step 7），用户回退到 Step 5 重编译并成功（旧实现会写回 Push）
        var spec = new ProjectSpec { CurrentStep = WizardStep.Pipeline };

        spec.AdvanceTo(WizardStep.Push);

        Assert.AreEqual(WizardStep.Pipeline, spec.CurrentStep);
    }

    [TestMethod]
    public void AdvanceTo_同级赋值_保持不变()
    {
        var spec = new ProjectSpec { CurrentStep = WizardStep.Database };

        spec.AdvanceTo(WizardStep.Database);

        Assert.AreEqual(WizardStep.Database, spec.CurrentStep);
    }

    // ── 推送闸门（C7′）：读 Step 5 结论文件，逐端全通过才放行 ───────────────

    [TestMethod]
    public void CanPush_无工作区根_拒绝()
    {
        var spec = Spec(string.Empty, ProjectEnd.Service);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_从未编译_无结论文件_拒绝()
    {
        // 真机冒烟档案形态：Step 5 一次都没跑过，results 目录里没有 step5-*.json
        var spec = Spec(NewWorkspace(), ProjectEnd.Service, ProjectEnd.Admin);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_编译失败_拒绝()
    {
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 5, """
        { "step": 5, "ok": false, "checks": [
            { "name": "Service", "ok": false, "detail": "3 个错误：CS0246" } ] }
        """);
        var spec = Spec(workspace, ProjectEnd.Service);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_有一端没过_拒绝()
    {
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 5, """
        { "step": 5, "ok": false, "checks": [
            { "name": "Service", "ok": true,  "detail": "编译通过" },
            { "name": "Admin",   "ok": false, "detail": "2 个错误" } ] }
        """);
        var spec = Spec(workspace, ProjectEnd.Service, ProjectEnd.Admin);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_漏端_拒绝()
    {
        // 结论只覆盖 Service，缺 Admin：不用「其余端通过」兜底
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 5, """
        { "step": 5, "ok": true, "checks": [
            { "name": "Service", "ok": true, "detail": "编译通过" } ] }
        """);
        var spec = Spec(workspace, ProjectEnd.Service, ProjectEnd.Admin);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_逐端全部通过_放行()
    {
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 5, BuildOk);
        var spec = Spec(workspace, ProjectEnd.Service, ProjectEnd.Admin);

        Assert.IsTrue(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_结论文件坏JSON_拒绝()
    {
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 5, "{ \"step\": 5, \"ok\": ");
        var spec = Spec(workspace, ProjectEnd.Service);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_结论文件步骤号不符_拒绝()
    {
        // 张冠李戴：把 step4 的结论文件塞进 step5 的命名里
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 5, """
        { "step": 4, "ok": true, "checks": [ { "name": "Service", "ok": true } ] }
        """);
        var spec = Spec(workspace, ProjectEnd.Service);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_取最近一份结论_旧失败被新成功覆盖_放行()
    {
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 5, """
        { "step": 5, "ok": false, "checks": [ { "name": "Service", "ok": false, "detail": "旧的一轮失败" } ] }
        """);
        Thread.Sleep(20);
        WriteConclusion(workspace, 5, """
        { "step": 5, "ok": true, "checks": [ { "name": "Service", "ok": true, "detail": "重跑通过" } ] }
        """);
        var spec = Spec(workspace, ProjectEnd.Service);

        Assert.IsTrue(StepGate.CanPush(spec), "按 mtime 取最近一份：重跑成功后应放行。");
    }

    // ── 流水线闸门（C7′）：读 Step 6 结论文件 ─────────────────────────────

    [TestMethod]
    public void CanCreatePipelines_未推送_拒绝()
    {
        var workspace = NewWorkspace();
        var spec = Spec(workspace, ProjectEnd.Service);

        Assert.IsFalse(StepGate.CanCreatePipelines(spec));
    }

    [TestMethod]
    public void CanCreatePipelines_推送通过_放行()
    {
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 6, PushOk);
        var spec = Spec(workspace, ProjectEnd.Service, ProjectEnd.Admin);

        Assert.IsTrue(StepGate.CanCreatePipelines(spec));
    }

    [TestMethod]
    public void CanCreatePipelines_有一端推送失败_拒绝()
    {
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 6, """
        { "step": 6, "status": "done", "ok": false, "checks": [
            { "name": "Service", "ok": true,  "detail": "推送成功" },
            { "name": "Admin",   "ok": false, "detail": "non-fast-forward" } ] }
        """);
        var spec = Spec(workspace, ProjectEnd.Service, ProjectEnd.Admin);

        Assert.IsFalse(StepGate.CanCreatePipelines(spec));
    }

    // ── 结论作废：Step 3 重跑后必须重新编译 / 重新推送 ──────────────────────

    [TestMethod]
    public void InvalidateBuild_删除结论文件_闸门转为拒绝()
    {
        // 场景：Step 5 全部通过（本可推送）→ 重跑 Step 3 拉了新代码：旧结论不再代表当前工作树
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 5, BuildOk);
        var spec = Spec(workspace, ProjectEnd.Service, ProjectEnd.Admin);
        Assert.IsTrue(StepGate.CanPush(spec), "前置条件：本用例要先构成「可推送」形态。");

        StepGate.InvalidateBuild(spec);

        Assert.AreEqual(0, spec.BuildResults.Count);
        Assert.AreEqual(0, StepResultFile.List(workspace, 5).Count, "最近一份 step5 结论文件应被删除。");
        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void InvalidateBuild_无结论文件_不抛且保持拒绝()
    {
        var spec = Spec(NewWorkspace(), ProjectEnd.Service);

        StepGate.InvalidateBuild(spec);

        Assert.AreEqual(0, spec.BuildResults.Count);
        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void InvalidateBuild_传空档案_抛参数异常()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => StepGate.InvalidateBuild(null!));
    }

    [TestMethod]
    public void InvalidatePipelineConclusion_删除结论并清流水线产物_闸门转拒绝()
    {
        var workspace = NewWorkspace();
        WriteConclusion(workspace, 6, PushOk);
        WriteConclusion(workspace, 7, """
        { "step": 7, "status": "done", "ok": true, "artifacts": [
            { "kind": "pipeline", "end": "Service", "name": "jnms-Service-Deploy", "yaml": "stages:\n  - stage: build" } ] }
        """);
        var spec = Spec(workspace, ProjectEnd.Service);
        spec.PipelineResults.Add(new EndPipelineResult { End = ProjectEnd.Service, PipelineId = "12345" });
        spec.RemoteArtifacts.Add(new RemoteArtifact { Kind = RemoteArtifactKind.Pipeline, Name = "jnms-Service-Deploy" });

        StepGate.InvalidatePipelineConclusion(spec);

        Assert.AreEqual(0, spec.PipelineResults.Count);
        Assert.AreEqual(0, spec.RemoteArtifacts.Count, "流水线类远端产物记录应被清空（远端本体不删）。");
        Assert.AreEqual(0, StepResultFile.List(workspace, 7).Count, "最近一份 step7 结论文件应被删除。");
        Assert.IsTrue(StepGate.CanCreatePipelines(spec), "推送结论仍在，故流水线闸门仍放行——本次仅作废 Step 7 结论。");
    }
}