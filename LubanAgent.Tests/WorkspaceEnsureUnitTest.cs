/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WorkspaceEnsureUnitTest
*描述：S2d §13.2 「新建并使用」ensure 幂等语义纯函数单测——rootPath 规范化比较
*     （大小写不敏感、尾分隔符归一）。壳层 WebWorkspaceService 依赖静态 WebApi.Runtime
*     （起不来），只有转发与 IO，不单测；ensure 三态的「规范化命中」判据抽为
*     WorkspaceIdGenerator.IsSameRoot 纯函数在此覆盖。夹具一律合成路径（temp 下合成名），
*     禁用值零引用、不写真实 LocalApplicationData。
*
*****************************************************************************/

using LubanAgentCore.Infrastructure;

namespace LubanAgent.Tests;

/// <summary>
/// S2d §13.2 ensure 语义纯函数：rootPath 规范化比较（IsSameRoot）。
/// </summary>
[TestClass]
public class WorkspaceEnsureUnitTest
{
    [TestMethod]
    public void IsSameRoot_同一路径_命中()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-ensure-" + Guid.NewGuid().ToString("N"));

        Assert.IsTrue(WorkspaceIdGenerator.IsSameRoot(root, root));
    }

    [TestMethod]
    public void IsSameRoot_大小写与尾分隔符差异_仍视为同一工作区()
    {
        // 归一化口径：Windows 大写归一 + 尾分隔符剥离（与 Create/GetByRootPath 同源）
        var root = Path.Combine(Path.GetTempPath(), "luban-ensure-" + Guid.NewGuid().ToString("N"));

        Assert.IsTrue(WorkspaceIdGenerator.IsSameRoot(root, root.ToUpperInvariant()));
        Assert.IsTrue(WorkspaceIdGenerator.IsSameRoot(root.ToLowerInvariant(), root));
        Assert.IsTrue(WorkspaceIdGenerator.IsSameRoot(root, root + Path.DirectorySeparatorChar));
        Assert.IsTrue(WorkspaceIdGenerator.IsSameRoot(root + Path.AltDirectorySeparatorChar, root));
    }

    [TestMethod]
    public void IsSameRoot_不同路径或空白_不命中()
    {
        var a = Path.Combine(Path.GetTempPath(), "luban-ensure-a-" + Guid.NewGuid().ToString("N"));
        var b = Path.Combine(Path.GetTempPath(), "luban-ensure-b-" + Guid.NewGuid().ToString("N"));

        Assert.IsFalse(WorkspaceIdGenerator.IsSameRoot(a, b));
        Assert.IsFalse(WorkspaceIdGenerator.IsSameRoot(a, Path.Combine(a, "child")));
        Assert.IsFalse(WorkspaceIdGenerator.IsSameRoot(a, null));
        Assert.IsFalse(WorkspaceIdGenerator.IsSameRoot("", b));
        Assert.IsFalse(WorkspaceIdGenerator.IsSameRoot(null, null));
    }
}
