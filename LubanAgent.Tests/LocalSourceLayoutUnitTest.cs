/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： LocalSourceLayoutUnitTest
*描述：云效 S2e §14 修复三——本地源码检出形态判定纯函数单测（LocalSourceLayout.Detect）——
*      原形态 {root}/{repoName} 子目录 / 形态 A 根目录名≈库名 / 形态 B 根含 .git（综合库特征子目录 ≥2、
*      单端库退化为根含 .git 即认）；都不命中 → None（出检出序列的原行为前提）。
*      全部用临时目录（Path.GetTempPath()），不写真实 LocalApplicationData；夹具一律合成名。
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §14 第 3 条
*
*****************************************************************************/

using LubanAgentWebHost.Services.Init;

namespace LubanAgent.Tests;

/// <summary>
/// <see cref="LocalSourceLayout.Detect"/> 形态判定契约单测：三形态命中 / 未命中 / 空参。
/// </summary>
[TestClass]
public class LocalSourceLayoutUnitTest
{
    /// <summary>
    /// 新建独立临时根目录（用后即弃，不落真实 LocalApplicationData）。
    /// <paramref name="leafName"/> 非空时根目录叶子名<b>恰为</b>该名（guid 放父目录防串扰，形态 A 判据需要精确叶子名）。
    /// </summary>
    private static string NewRoot(string? leafName = null)
    {
        var parent = Path.Combine(Path.GetTempPath(), "luban-lsl-" + Guid.NewGuid().ToString("N"));
        var root = leafName is null ? parent : Path.Combine(parent, leafName);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>建目录 + 塞一个占位文件（保证「非空」判据）。</summary>
    private static void MakeNonEmptyDir(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "placeholder.txt"), "x");
    }

    // ── 原形态：{root}/{repoName} 子目录 ────────────────────────────────────

    [TestMethod]
    public void Detect_库子目录非空_命中SubDir()
    {
        var root = NewRoot();
        MakeNonEmptyDir(Path.Combine(root, "NEWX-Mixed"));

        var probe = LocalSourceLayout.Detect(root, "NEWX-Mixed");

        Assert.IsTrue(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.SubDir, probe.Kind);
        Assert.AreEqual(Path.Combine(root, "NEWX-Mixed"), probe.SourceDir);
    }

    [TestMethod]
    public void Detect_库子目录为空_未命中_空目录按无源码()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed"));   // 空目录

        var probe = LocalSourceLayout.Detect(root, "NEWX-Mixed");

        Assert.IsFalse(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.None, probe.Kind);
        Assert.IsNull(probe.SourceDir);
    }

    // ── 形态 A：工作区根本身即检出（根目录名 ≈ 库名）────────────────────────

    [TestMethod]
    public void Detect_根目录名等于库名_命中RootCheckout()
    {
        // 用户报障形态：工作区根本身就是该库检出（根名与库名规范化相等）
        var root = NewRoot("NEWX-Mixed");
        MakeNonEmptyDir(Path.Combine(root, "NEWX-Admin"));

        var probe = LocalSourceLayout.Detect(root, "NEWX-Mixed");

        Assert.IsTrue(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.RootCheckout, probe.Kind);
        Assert.AreEqual(root, probe.SourceDir);
    }

    [TestMethod]
    public void Detect_根目录名大小写与尾分隔符不同_仍命中RootCheckout()
    {
        var root = NewRoot("NEWX-Mixed");
        MakeNonEmptyDir(Path.Combine(root, "NEWX-Service"));

        // 库名大小写不同 + 根路径带尾分隔符 → 规范化相等（忽略大小写/尾分隔符）
        var probe = LocalSourceLayout.Detect(root + Path.DirectorySeparatorChar, "newx-mixed");

        Assert.IsTrue(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.RootCheckout, probe.Kind);
    }

    [TestMethod]
    public void Detect_根目录名不等库名_不命中形态A()
    {
        var root = NewRoot("OTHER-ROOT");
        MakeNonEmptyDir(root);

        var probe = LocalSourceLayout.Detect(root, "NEWX-Mixed");

        Assert.IsFalse(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.None, probe.Kind);
    }

    [TestMethod]
    public void IsRootNamedAsRepo_纯名字比较_大小写与尾分隔符容忍()
    {
        Assert.IsTrue(LocalSourceLayout.IsRootNamedAsRepo("/w/NEWX-Mixed", "NEWX-Mixed"));
        Assert.IsTrue(LocalSourceLayout.IsRootNamedAsRepo("/w/NEWX-Mixed/", "newx-mixed"));
        Assert.IsFalse(LocalSourceLayout.IsRootNamedAsRepo("/w/OTHER", "NEWX-Mixed"));
        Assert.IsFalse(LocalSourceLayout.IsRootNamedAsRepo("", "NEWX-Mixed"));
        Assert.IsFalse(LocalSourceLayout.IsRootNamedAsRepo("/w/NEWX-Mixed", null));
    }

    // ── 形态 B：根含 .git（综合库特征子目录；单端库退化）────────────────────

    [TestMethod]
    public void Detect_根含git与综合库特征子目录两个_命中RootGit()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        MakeNonEmptyDir(Path.Combine(root, "NEWX-Admin"));
        MakeNonEmptyDir(Path.Combine(root, "NEWX-Mobile"));

        var probe = LocalSourceLayout.Detect(root, "NEWX-Mixed");

        Assert.IsTrue(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.RootGit, probe.Kind);
        Assert.AreEqual(root, probe.SourceDir);
    }

    [TestMethod]
    public void Detect_根含git但特征子目录不足两个_综合库不命中()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        MakeNonEmptyDir(Path.Combine(root, "NEWX-Admin"));   // 仅 1 个 < 2

        var probe = LocalSourceLayout.Detect(root, "NEWX-Mixed");

        Assert.IsFalse(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.None, probe.Kind);
    }

    [TestMethod]
    public void Detect_单端库根含git_即认RootGit()
    {
        // 单端库（非 -Mixed）退化为「根含 .git 即认」
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        MakeNonEmptyDir(root);

        var probe = LocalSourceLayout.Detect(root, "NEWX-Service");

        Assert.IsTrue(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.RootGit, probe.Kind);
    }

    [TestMethod]
    public void Detect_根无git且子目录无_均不命中()
    {
        var root = NewRoot();
        MakeNonEmptyDir(root);   // 根非空但既非检出名也无 .git

        var probe = LocalSourceLayout.Detect(root, "NEWX-Mixed");

        Assert.IsFalse(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.None, probe.Kind);
    }

    // ── 空参与优先级 ────────────────────────────────────────────────────────

    [TestMethod]
    public void Detect_空参_未命中()
    {
        Assert.IsFalse(LocalSourceLayout.Detect(null, "NEWX-Mixed").HasSource);
        Assert.IsFalse(LocalSourceLayout.Detect(NewRoot(), null).HasSource);
        Assert.IsFalse(LocalSourceLayout.Detect("  ", "NEWX-Mixed").HasSource);
        Assert.IsFalse(LocalSourceLayout.Detect(NewRoot(), " ").HasSource);
    }

    [TestMethod]
    public void Detect_三形态齐备_优先原形态SubDir()
    {
        // {root}/{repoName} 命中优先于根形态（原形态保持既有语义优先）
        var root = NewRoot("NEWX-Mixed");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        MakeNonEmptyDir(Path.Combine(root, "NEWX-Mixed", "NEWX-Admin"));

        var probe = LocalSourceLayout.Detect(root, "NEWX-Mixed");

        Assert.IsTrue(probe.HasSource);
        Assert.AreEqual(LocalSourceKind.SubDir, probe.Kind);
    }

    [TestMethod]
    public void HasSource_布尔投影与Detect一致()
    {
        var root = NewRoot();
        MakeNonEmptyDir(Path.Combine(root, "NEWX-Mixed"));
        Assert.IsTrue(LocalSourceLayout.HasSource(root, "NEWX-Mixed"));

        var empty = NewRoot();
        Assert.IsFalse(LocalSourceLayout.HasSource(empty, "NEWX-Mixed"));
    }
}
