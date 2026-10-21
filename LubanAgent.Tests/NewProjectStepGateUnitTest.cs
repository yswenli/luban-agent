/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStepGateUnitTest
*创建时间：2026/10/21
*描述：步骤前置闸门与推进指针单测——锁死 P4 回退导航收口后的两条口径：
*      ① 档案 CurrentStep 单调只前进（回退重跑不得让进度倒退）；
*      ② 推送（Step 6）必须复核「编译（Step 5）已全部通过」，绕开界面顺序也拦得住。
*      回归背景：真机冒烟出现「5 编译 未开始 / 6 推送 已完成」的自相矛盾状态。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectStepGateUnitTest
{
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

    // ── 推送闸门：Step 5 已全部通过才放行 ──────────────────────────────────

    [TestMethod]
    public void CanPush_从未编译_拒绝()
    {
        // 真机冒烟档案形态：StepStates 里没有 Build 键（Step 5 一次都没跑过）
        var spec = new ProjectSpec { CurrentStep = WizardStep.Pipeline };
        spec.StepStates[nameof(WizardStep.Repositories)] = nameof(StepStatus.Succeeded);
        spec.StepStates[nameof(WizardStep.Push)] = nameof(StepStatus.Succeeded);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_编译失败_拒绝()
    {
        var spec = new ProjectSpec { CurrentStep = WizardStep.Build };
        spec.StepStates[nameof(WizardStep.Build)] = nameof(StepStatus.Failed);
        spec.BuildResults.Add(new EndBuildResult { End = ProjectEnd.Service, Ok = true });

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_有终态快照但无逐端结论_拒绝()
    {
        // 快照说成功、逐端结论为空：结论文件缺失那一轮的形态，缺一不可
        var spec = new ProjectSpec { CurrentStep = WizardStep.Push };
        spec.StepStates[nameof(WizardStep.Build)] = nameof(StepStatus.Succeeded);

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_有一端没过_拒绝()
    {
        var spec = new ProjectSpec { CurrentStep = WizardStep.Build };
        spec.StepStates[nameof(WizardStep.Build)] = nameof(StepStatus.Failed);
        spec.BuildResults.Add(new EndBuildResult { End = ProjectEnd.Service, Ok = true });
        spec.BuildResults.Add(new EndBuildResult { End = ProjectEnd.Admin, Ok = false, Summary = "2 个错误" });

        Assert.IsFalse(StepGate.CanPush(spec));
    }

    [TestMethod]
    public void CanPush_逐端全部通过_放行()
    {
        var spec = new ProjectSpec { CurrentStep = WizardStep.Push };
        spec.StepStates[nameof(WizardStep.Build)] = nameof(StepStatus.Succeeded);
        spec.BuildResults.Add(new EndBuildResult { End = ProjectEnd.Service, Ok = true, Command = "dotnet build X.sln" });
        spec.BuildResults.Add(new EndBuildResult { End = ProjectEnd.Admin, Ok = true, Command = "pnpm build" });

        Assert.IsTrue(StepGate.CanPush(spec));
    }
}