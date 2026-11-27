/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SessionClearScopeUnitTest
*创建时间：2026/11/26
*描述：清空会话的范围判定单测——
*      ① 显式清全部：指定工作区只决定补建落点，无效或未指定则不补建；
*      ② 未指定工作区且非清全部：要求调用方显式指定工作区；
*      ③ 指定工作区：只清该区、不补建，非本人工作区直接拒绝。
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
    public void All_RebuildsRequestedWorkspaceWhenOwned()
    {
        var scope = SessionClearScope.Resolve(true, " ws-b ", Owns("ws-a", "ws-b"));

        Assert.IsTrue(scope.ClearAll);
        Assert.IsNull(scope.WorkspaceId);
        Assert.AreEqual("ws-b", scope.RebuildWorkspaceId);
        Assert.IsNull(scope.MissingWorkspaceId);
        Assert.IsFalse(scope.WorkspaceRequired);
    }

    [TestMethod]
    public void All_DoesNotRebuildWithoutTarget()
    {
        var scope = SessionClearScope.Resolve(true, null, Owns("ws-a"));

        Assert.IsTrue(scope.ClearAll);
        Assert.IsNull(scope.RebuildWorkspaceId);
    }

    [TestMethod]
    public void All_DoesNotRebuildWhenTargetNotOwned()
    {
        var scope = SessionClearScope.Resolve(true, "ws-x", Owns("ws-a"));

        Assert.IsTrue(scope.ClearAll);
        Assert.IsNull(scope.RebuildWorkspaceId);
        Assert.IsNull(scope.MissingWorkspaceId, "清全部时无效落点只跳过补建，不报工作区不存在");
    }

    [TestMethod]
    public void All_DoesNotRebuildWithoutAnyWorkspace()
    {
        var scope = SessionClearScope.Resolve(true, null, Owns());

        Assert.IsTrue(scope.ClearAll);
        Assert.IsNull(scope.RebuildWorkspaceId);
    }

    // ---------------- 未指定工作区 ----------------

    [TestMethod]
    public void Workspace_RequiredWhenNotSpecified()
    {
        var scope = SessionClearScope.Resolve(false, null, Owns("ws-a"));

        Assert.IsFalse(scope.ClearAll);
        Assert.IsTrue(scope.WorkspaceRequired);
        Assert.IsNull(scope.WorkspaceId);
        Assert.IsNull(scope.RebuildWorkspaceId);
    }

    [TestMethod]
    public void Workspace_RequiredWhenBlank()
    {
        var scope = SessionClearScope.Resolve(false, "   ", Owns());

        Assert.IsFalse(scope.ClearAll);
        Assert.IsTrue(scope.WorkspaceRequired);
        Assert.IsNull(scope.MissingWorkspaceId);
    }

    // ---------------- 指定工作区 ----------------

    [TestMethod]
    public void Workspace_ClearsOnlyTargetWithoutRebuild()
    {
        var scope = SessionClearScope.Resolve(false, "ws-b", Owns("ws-a", "ws-b"));

        Assert.IsFalse(scope.ClearAll);
        Assert.AreEqual("ws-b", scope.WorkspaceId);
        Assert.IsNull(scope.RebuildWorkspaceId, "按项目清理后不自动补建空会话");
        Assert.IsFalse(scope.WorkspaceRequired);
    }

    [TestMethod]
    public void Workspace_NotOwnedReportsMissing()
    {
        var scope = SessionClearScope.Resolve(false, "ws-x", Owns("ws-a"));

        Assert.IsFalse(scope.ClearAll);
        Assert.IsNull(scope.WorkspaceId);
        Assert.IsNull(scope.RebuildWorkspaceId);
        Assert.AreEqual("ws-x", scope.MissingWorkspaceId);
    }
}