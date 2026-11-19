/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： GitCommandPlannerUnitTest
*描述：云效 S2 项目初始化 git 指令序列纯函数单测（GitCommandPlanner）——
*      clone → 批量 mv 改名 → 固定文案 commit → remote set-url 新库 → push
*      （展示与执行同一份序列，防展示≠执行）
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.2
*夹具：URL 一律合成裸地址（无凭据形态），断言序列零 pt- / 零 @ / 零控制字符
*****************************************************************************/

using LubanAgentWebHost.Services.Init;

namespace LubanAgent.Tests;

/// <summary>
/// <see cref="GitCommandPlanner"/> 纯函数契约单测：把「步骤顺序 + 固定 commit 文案 + 脱敏」钉死。
/// </summary>
/// <remarks>
/// 预检面板展示的命令序列与执行侧下发的序列<b>必须同源</b>（设计 §4.2），故这里按整串逐字断言。
/// 脱敏为硬性红线：序列里不出现 <c>pt-</c> 前缀明文、不出现 <c>@</c> 凭据形态、
/// 不出现空格外的控制字符；URL 参数就是入参原样（凭据不进 URL，执行侧注入）。
/// </remarks>
[TestClass]
public class GitCommandPlannerUnitTest
{
    private const string WorkspaceRoot = "D:/work/init-demo";
    private const string TemplateRepoUrl = "https://codeup.example.com/org-demo/tplx-mixed.git";
    private const string NewRepoUrl = "https://codeup.example.com/org-demo/newx-mixed.git";
    private const string TemplateCode = "TPLX";
    private const string NewCode = "NEWX";
    private const string Branch = "master";

    private static readonly string[] ThreeDirs = { "NEWX-Admin", "NEWX-Mobile", "NEWX-Service" };

    private static string RepoDir => $"{WorkspaceRoot}/{NewCode}-Mixed";

    // ── 步骤序列：顺序固定 ────────────────────────────────────────────────

    [TestMethod]
    public void Plan_ThreeDirs_ProducesSevenStepsInFixedOrder()
    {
        var steps = GitCommandPlanner.PlanCloneRenamePush(
            WorkspaceRoot, TemplateRepoUrl, NewRepoUrl, TemplateCode, NewCode, ThreeDirs, Branch);

        Assert.AreEqual(7, steps.Count);

        var expected = new[]
        {
            $"git clone --branch {Branch} {TemplateRepoUrl} {RepoDir}",
            $"git -C \"{RepoDir}\" mv TPLX-Admin NEWX-Admin",
            $"git -C \"{RepoDir}\" mv TPLX-Mobile NEWX-Mobile",
            $"git -C \"{RepoDir}\" mv TPLX-Service NEWX-Service",
            $"git -C \"{RepoDir}\" commit -m \"chore: 重命名子目录 TPLX-* → NEWX-*（项目初始化派生）\"",
            $"git -C \"{RepoDir}\" remote set-url origin {NewRepoUrl}",
            $"git -C \"{RepoDir}\" push origin {Branch}",
        };

        CollectionAssert.AreEqual(expected, steps.Select(s => s.Command).ToArray());
    }

    // ── 改名步骤：三目录齐全、Label 注明改名对 ────────────────────────────

    [TestMethod]
    public void Plan_RenameSteps_CoverThreeDirs_WithLabelsStatingFromTo()
    {
        var steps = GitCommandPlanner.PlanCloneRenamePush(
            WorkspaceRoot, TemplateRepoUrl, NewRepoUrl, TemplateCode, NewCode, ThreeDirs, Branch);

        var renameSteps = steps.Skip(1).Take(3).ToArray();
        Assert.AreEqual("改名 TPLX-Admin → NEWX-Admin", renameSteps[0].Label);
        Assert.AreEqual("改名 TPLX-Mobile → NEWX-Mobile", renameSteps[1].Label);
        Assert.AreEqual("改名 TPLX-Service → NEWX-Service", renameSteps[2].Label);

        Assert.AreEqual($"git -C \"{RepoDir}\" mv TPLX-Admin NEWX-Admin", renameSteps[0].Command);
        Assert.AreEqual($"git -C \"{RepoDir}\" mv TPLX-Mobile NEWX-Mobile", renameSteps[1].Command);
        Assert.AreEqual($"git -C \"{RepoDir}\" mv TPLX-Service NEWX-Service", renameSteps[2].Command);
    }

    // ── commit 步骤：固定文案逐字 ─────────────────────────────────────────

    [TestMethod]
    public void Plan_CommitStep_UsesFixedLiteralMessage()
    {
        var steps = GitCommandPlanner.PlanCloneRenamePush(
            WorkspaceRoot, TemplateRepoUrl, NewRepoUrl, TemplateCode, NewCode, ThreeDirs, Branch);

        var commit = steps.Single(s => s.Command.Contains("commit -m"));
        Assert.AreEqual(
            $"git -C \"{RepoDir}\" commit -m \"chore: 重命名子目录 TPLX-* → NEWX-*（项目初始化派生）\"",
            commit.Command);
    }

    // ── remote set-url 在 push 之前 ───────────────────────────────────────

    [TestMethod]
    public void Plan_SetUrlStep_ComesBeforePushStep()
    {
        var steps = GitCommandPlanner.PlanCloneRenamePush(
            WorkspaceRoot, TemplateRepoUrl, NewRepoUrl, TemplateCode, NewCode, ThreeDirs, Branch);

        var commands = steps.Select(s => s.Command).ToArray();
        var setUrlIndex = Array.FindIndex(commands, c => c.Contains("remote set-url"));
        var pushIndex = Array.FindIndex(commands, c => c.Contains("push origin"));

        Assert.IsTrue(setUrlIndex >= 0, "应有 remote set-url 步骤");
        Assert.IsTrue(pushIndex >= 0, "应有 push 步骤");
        Assert.IsTrue(setUrlIndex < pushIndex, "remote set-url 必须在 push 之前");
    }

    // ── 脱敏硬性断言 + URL 原样透传 ───────────────────────────────────────

    [TestMethod]
    public void Plan_AllCommands_CarryNoSecretsNoControlChars_AndPassUrlsThroughVerbatim()
    {
        var steps = GitCommandPlanner.PlanCloneRenamePush(
            WorkspaceRoot, TemplateRepoUrl, NewRepoUrl, TemplateCode, NewCode, ThreeDirs, Branch);

        foreach (var step in steps)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(step.Label), "每步都应有可读 Label");
            foreach (var c in step.Command)
                Assert.IsFalse(char.IsControl(c), $"命令含空格外的控制字符：{step.Command}");
        }

        var joined = string.Join(" ", steps.Select(s => s.Command));
        Assert.IsFalse(joined.Contains("pt-"), "序列不得出现 pt- 前缀凭据明文");
        Assert.IsFalse(joined.Contains("@"), "序列不得出现 @ 凭据形态");

        // URL 参数就是入参原样（凭据不进 URL，执行侧注入）
        StringAssert.Contains(joined, TemplateRepoUrl);
        StringAssert.Contains(joined, NewRepoUrl);
    }

    // ── 空 dirs：无 mv / commit，clone + set-url + push 仍在 ──────────────

    [TestMethod]
    public void Plan_EmptyDirNames_OmitsRenameAndCommit_KeepsCloneSetUrlPush()
    {
        var steps = GitCommandPlanner.PlanCloneRenamePush(
            WorkspaceRoot, TemplateRepoUrl, NewRepoUrl, TemplateCode, NewCode, Array.Empty<string>(), Branch);

        Assert.AreEqual(3, steps.Count);

        var commands = steps.Select(s => s.Command).ToArray();
        Assert.AreEqual($"git clone --branch {Branch} {TemplateRepoUrl} {RepoDir}", commands[0]);
        Assert.AreEqual($"git -C \"{RepoDir}\" remote set-url origin {NewRepoUrl}", commands[1]);
        Assert.AreEqual($"git -C \"{RepoDir}\" push origin {Branch}", commands[2]);

        Assert.IsFalse(commands.Any(c => c.Contains(" mv ")), "空 dirs 不应有 mv 步骤");
        Assert.IsFalse(commands.Any(c => c.Contains("commit")), "空 dirs 不应有 commit 步骤");
    }

    // ── 分支参数：clone 与 push 都用入参分支 ──────────────────────────────

    [TestMethod]
    public void Plan_BranchParameter_FlowsToCloneAndPush()
    {
        var steps = GitCommandPlanner.PlanCloneRenamePush(
            WorkspaceRoot, TemplateRepoUrl, NewRepoUrl, TemplateCode, NewCode, Array.Empty<string>(), "release/2026");

        StringAssert.Contains(steps[0].Command, "--branch release/2026");
        StringAssert.Contains(steps[^1].Command, "push origin release/2026");
    }
}
