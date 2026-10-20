/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： DirectoryCleanerUnitTest
*创建时间：2026/10/20
*描述：DirectoryCleaner 单测——复现并锁死真机缺陷 RC5：
*      git 的 pack 文件（.git/objects/pack/*.pack|.idx|.rev）是只读的，
*      裸 Directory.Delete 必抛 UnauthorizedAccessException，且抛错前已删掉
*      HEAD / config / refs，把目录留成「只剩只读文件」的残缺态；
*      故一切整目录删除必须走 DirectoryCleaner.DeleteForce（先清只读再删）。
*
*****************************************************************************/

using LubanAgentCore.Utils;

namespace LubanAgent.Tests;

[TestClass]
public class DirectoryCleanerUnitTest
{
    /// <summary>真机现场那个只读 pack 的文件名（用于断言文案与场景一致）。</summary>
    private const string PackName = "pack-454abe167a810a488401583e104bd635bd124cb5.idx";

    [TestMethod]
    public void BareDirectoryDelete_FailsOnReadOnlyPack_ReproducingDefect()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("「只读属性挡删除」是 Windows 语义，本用例仅在 Windows 有判定意义。");

        var root = BuildGitLikeTreeWithReadOnlyPack();
        try
        {
            // 前提复现：错误信息即真机屏幕上的那句 “Access to the path '…idx' is denied.”
            var ex = Assert.ThrowsExactly<UnauthorizedAccessException>(
                () => Directory.Delete(root, recursive: true));

            StringAssert.Contains(ex.Message, PackName);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [TestMethod]
    public void DeleteForce_RemovesTreeWithReadOnlyPackUnderHiddenGitDir()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("只读属性语义差异，仅 Windows 需要本防护。");

        var root = BuildGitLikeTreeWithReadOnlyPack();

        DirectoryCleaner.DeleteForce(root);

        Assert.IsFalse(Directory.Exists(root), "只读 pack 必须不再挡住整目录删除。");
    }

    [TestMethod]
    public void DeleteForce_IsIdempotent_WhenDirectoryMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "luban-dircleaner-missing-" + Guid.NewGuid().ToString("N"));

        // 不抛异常即通过（重跑 Step 3 / 残留清理都会重复调用）
        DirectoryCleaner.DeleteForce(missing);

        Assert.IsFalse(Directory.Exists(missing));
    }

    [TestMethod]
    public void DeleteForce_ThrowsArgumentException_WhenPathBlank()
    {
        Assert.ThrowsExactly<ArgumentException>(() => DirectoryCleaner.DeleteForce("  "));
    }

    /// <summary>
    /// 造出与真机残骸等价的结构：<c>.git</c>（隐藏）→ <c>objects/pack</c> → 只读 pack 文件。
    /// </summary>
    /// <returns>新目录路径。</returns>
    private static string BuildGitLikeTreeWithReadOnlyPack()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-dircleaner-" + Guid.NewGuid().ToString("N"));
        var packDir = Path.Combine(root, ".git", "objects", "pack");
        Directory.CreateDirectory(packDir);

        // .git 是隐藏目录（与真机一致）：默认枚举会跳过 Hidden，这正是本缺陷最隐蔽的一环
        var gitDir = Path.Combine(root, ".git");
        File.SetAttributes(gitDir, File.GetAttributes(gitDir) | FileAttributes.Hidden);

        var pack = Path.Combine(packDir, PackName);
        File.WriteAllText(pack, "fake pack");
        File.SetAttributes(pack, File.GetAttributes(pack) | FileAttributes.ReadOnly);

        return root;
    }

    /// <summary>清理测试目录（尽力而为，用被测方法本身以避开只读挡路）。</summary>
    /// <param name="root">测试目录。</param>
    private static void Cleanup(string root)
    {
        try
        {
            DirectoryCleaner.DeleteForce(root);
        }
        catch (Exception)
        {
            // 清理失败不影响断言结论
        }
    }
}