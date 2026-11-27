/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SessionClearScopeUnitTest
*创建时间：2026/11/26
*描述：清空会话的范围判定单测——
*      ① 显式清全部：指定工作区只决定补建落点，无效则回退当前工作区；
*      ② 无目标无当前工作区沿用旧语义清全部且不补建；
*      ③ 指定工作区：只清该区，仅当前工作区才补建，非本人工作区直接拒绝。
*      纯计算，不触碰 LuBanOrm / WebApi 静态宿主。
*
*****************************************************************************/

using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

[TestClass]
public class SessionClearScopeUnitTest
{
    /// <summary>构造「只有给定工作区属于当前用户」的判定函数。</summary>
    private static Func<string, bool> Owns(params string[] workspaceIds)
    {
        var owned = new HashSet<string>(workspaceIds, StringComparer.Ordinal);
        return owned.Contains;
    }

    // ---------------- 显式清全部 ----------------

    [TestMethod]
    public void All_RebuildsCurrentWorkspaceWhenNoTarget()
    {
        var scope = SessionClearScope.Resolve(true, null, "ws-a", Owns("ws-a"));

        Assert.IsTrue(scope.ClearAll);
        Assert.IsNull(scope.WorkspaceId);
        Assert.AreEqual("ws-a", scope.RebuildWorkspaceId);
        Assert.IsNull(scope.MissingWorkspaceId);
    }

    [TestMethod]
    public void All_RebuildsRequestedWorkspaceWhenOwned()
    {
        var scope = SessionClearScope.Resolve(true, " ws-b ", "ws-a", Owns("ws-a", "ws-b"));

        Assert.IsTrue(scope.ClearAll);
        Assert.IsNull(scope.WorkspaceId);
        Assert.AreEqual("ws-b", scope.RebuildWorkspaceId, "远端调用方的当前工作区应作为补建落点");
    }

    [TestMethod]
    public void All_FallsBackToCurrentWorkspaceWhenTargetNotOwned()
    {
        var scope = SessionClearScope.Resolve(true, "ws-x", "ws-a", Owns("ws-a"));

        Assert.IsTrue(scope.ClearAll);
        Assert.AreEqual("ws-a", scope.RebuildWorkspaceId);
        Assert.IsNull(scope.MissingWorkspaceId);
    }

    [TestMethod]
    public void All_DoesNotRebuildWithoutAnyWorkspace()
    {
        var scope = SessionClearScope.Resolve(true, null, null, Owns());

        Assert.IsTrue(scope.ClearAll);
        Assert.IsNull(scope.RebuildWorkspaceId);
    }

    // ---------------- 未指定工作区 ----------------

    [TestMethod]
    public void Implicit_CurrentWorkspaceClearsOnlyThatWorkspaceAndRebuilds()
    {
        var scope = SessionClearScope.Resolve(false, null, "ws-a", Owns("ws-a"));

        Assert.IsFalse(scope.ClearAll);
        Assert.AreEqual("ws-a", scope.WorkspaceId);
        Assert.AreEqual("ws-a", scope.RebuildWorkspaceId);
    }

    [TestMethod]
    public void Implicit_NoCurrentWorkspaceKeepsLegacyClearAllWithoutRebuild()
    {
        var scope = SessionClearScope.Resolve(false, "   ", null, Owns());

        Assert.IsTrue(scope.ClearAll, "无目标且无当前工作区时沿用旧语义清全部");
        Assert.IsNull(scope.RebuildWorkspaceId);
    }

    // ---------------- 指定工作区 ----------------

    [TestMethod]
    public void Workspace_OtherWorkspaceClearsWithoutTouchingCurrentSession()
    {
        var scope = SessionClearScope.Resolve(false, "ws-b", "ws-a", Owns("ws-a", "ws-b"));

        Assert.IsFalse(scope.ClearAll);
        Assert.AreEqual("ws-b", scope.WorkspaceId);
        Assert.IsNull(scope.RebuildWorkspaceId, "清理非当前工作区不应补建");
    }

    [TestMethod]
    public void Workspace_CurrentWorkspaceRebuilds()
    {
        var scope = SessionClearScope.Resolve(false, "ws-a", "ws-a", Owns("ws-a"));

        Assert.IsFalse(scope.ClearAll);
        Assert.AreEqual("ws-a", scope.WorkspaceId);
        Assert.AreEqual("ws-a", scope.RebuildWorkspaceId);
    }

    [TestMethod]
    public void Workspace_NotOwnedReportsMissing()
    {
        var scope = SessionClearScope.Resolve(false, "ws-x", "ws-a", Owns("ws-a"));

        Assert.IsFalse(scope.ClearAll);
        Assert.IsNull(scope.WorkspaceId);
        Assert.IsNull(scope.RebuildWorkspaceId);
        Assert.AreEqual("ws-x", scope.MissingWorkspaceId);
    }
}