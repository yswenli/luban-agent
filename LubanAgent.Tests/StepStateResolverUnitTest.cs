/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： StepStateResolverUnitTest
*创建时间：2026/10/20
*描述：步骤状态归并单测——锁死 P3 真机缺陷：
*      步骤条状态改为「取操作记录真实终态」后，记录只在内存、重启即空，
*      于是重启后已经跑完的步骤（Step 2/3/6）全部退回「未开始」，
*      与档案里的 Cloned=true / RemoteArtifacts 自相矛盾。
*      修法＝档案落「终态快照」并与本次运行记录归并（更新者优先）。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class StepStateResolverUnitTest
{
    /// <summary>构造一条记录。</summary>
    private static OperationRecord Record(WizardStep step, StepStatus status) => new()
    {
        Step = step,
        Status = status,
        Action = status.ToString()
    };

    [TestMethod]
    public void Resolve_NoPersistedNoRecords_ReturnsEmpty()
    {
        var states = StepStateResolver.Resolve(null, null);

        Assert.AreEqual(0, states.Count, "无快照、无记录时应没有任何步骤状态。");
    }

    [TestMethod]
    public void Resolve_PersistedOnly_SurvivesRestart()
    {
        // 真机场景：重启后内存记录为空，只剩档案里的快照（Step 2/3/6 曾成功）
        var persisted = new Dictionary<string, string>
        {
            [nameof(WizardStep.Scaffold)] = nameof(StepStatus.Succeeded),
            [nameof(WizardStep.Repositories)] = nameof(StepStatus.Succeeded),
            [nameof(WizardStep.Push)] = nameof(StepStatus.Succeeded)
        };

        var states = StepStateResolver.Resolve(persisted, null);

        Assert.AreEqual(StepStatus.Succeeded, states[WizardStep.Scaffold]);
        Assert.AreEqual(StepStatus.Succeeded, states[WizardStep.Repositories]);
        Assert.AreEqual(StepStatus.Succeeded, states[WizardStep.Push]);
        Assert.AreEqual(3, states.Count);
    }

    [TestMethod]
    public void Resolve_RecordsOverridePersisted_LastRecordWins()
    {
        var persisted = new Dictionary<string, string>
        {
            [nameof(WizardStep.Repositories)] = nameof(StepStatus.Succeeded)
        };
        var records = new[]
        {
            Record(WizardStep.Repositories, StepStatus.Running)
        };

        var states = StepStateResolver.Resolve(persisted, records);

        Assert.AreEqual(StepStatus.Running, states[WizardStep.Repositories],
            "本次运行正在跑时，应显示「执行中」而不是档案里的旧终态。");
    }

    [TestMethod]
    public void Resolve_SucceededThenFailed_ReportsFailed()
    {
        // 重跑同一步：成功过一次、又重跑失败 → 当前状态必须是失败
        var records = new[]
        {
            Record(WizardStep.Scaffold, StepStatus.Succeeded),
            Record(WizardStep.Repositories, StepStatus.Running),
            Record(WizardStep.Scaffold, StepStatus.Failed)
        };

        var states = StepStateResolver.Resolve(null, records);

        Assert.AreEqual(StepStatus.Failed, states[WizardStep.Scaffold], "最后一次发生的终态胜出。");
    }

    [TestMethod]
    public void Resolve_FailedThenSucceeded_ReportsSucceeded()
    {
        // 失败重试成功后不得继续显示失败（PRD §4.7 可重试）
        var records = new[]
        {
            Record(WizardStep.Repositories, StepStatus.Failed),
            Record(WizardStep.Repositories, StepStatus.Succeeded)
        };

        var states = StepStateResolver.Resolve(null, records);

        Assert.AreEqual(StepStatus.Succeeded, states[WizardStep.Repositories]);
    }

    [TestMethod]
    public void Resolve_UnknownKeysAndValues_AreIgnored()
    {
        // 档案被旧版本写入或被手工改坏时，不应让步骤条整体失效
        var persisted = new Dictionary<string, string>
        {
            ["NotAStep"] = nameof(StepStatus.Succeeded),
            [nameof(WizardStep.Build)] = "NotAStatus",
            [nameof(WizardStep.Database)] = nameof(StepStatus.Skipped)
        };

        var states = StepStateResolver.Resolve(persisted, null);

        Assert.AreEqual(1, states.Count);
        Assert.AreEqual(StepStatus.Skipped, states[WizardStep.Database]);
    }

    [TestMethod]
    public void Resolve_SkippedIsKept()
    {
        var records = new[] { Record(WizardStep.Build, StepStatus.Skipped) };

        var states = StepStateResolver.Resolve(null, records);

        Assert.AreEqual(StepStatus.Skipped, states[WizardStep.Build]);
    }
}