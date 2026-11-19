/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： YamlSubstitutorUnitTest
*描述：云效 S2 FR-3 流水线 YAML 占位符替换 + diff 单测（设计 §4.6）——
*      表驱动替换对（代号段 + 占位符并行）、长 From 先替防子串误伤、
*      无匹配原样、空串容错、逐行简单 unified diff（增/删/改）。
*      夹具一律合成名（TPLX/NEWX），禁用值零引用。
*
*****************************************************************************/

using LubanAgentWebHost.Services.Init;

namespace LubanAgent.Tests;

[TestClass]
public class YamlSubstitutorUnitTest
{
    // ── BuildPairs：替换对表 ───────────────────────────────────────────────

    [TestMethod]
    public void BuildPairs_代号段替换对_长串先替()
    {
        var pairs = YamlSubstitutor.BuildPairs(new PipelineVars(), "TPLX", "NEWX");

        Assert.IsTrue(pairs.Contains(("TPLX-Admin", "NEWX-Admin")));
        Assert.IsTrue(pairs.Contains(("TPLX-Mobile", "NEWX-Mobile")));
        Assert.IsTrue(pairs.Contains(("TPLX-Service", "NEWX-Service")));
        Assert.IsTrue(pairs.Contains(("TPLX-Mixed", "NEWX-Mixed")));

        // 长 From 先于短 From（裸代号殿后），防子串误伤
        var bare = Array.FindIndex(pairs, p => p.From == "TPLX");
        var admin = Array.FindIndex(pairs, p => p.From == "TPLX-Admin");
        Assert.IsTrue(admin >= 0 && bare >= 0 && admin < bare);

        var yaml = YamlSubstitutor.Substitute("TPLX-Admin TPLX-Mixed TPLX", pairs);
        Assert.AreEqual("NEWX-Admin NEWX-Mixed NEWX", yaml);
    }

    [TestMethod]
    public void BuildPairs_占位符替换对_全量值()
    {
        var vars = new PipelineVars("rid-1", "NEWX-Mixed", "/w/svc", "master", 9000);
        var pairs = YamlSubstitutor.BuildPairs(vars, "", "");

        Assert.IsTrue(pairs.Contains(("{{REPO_ID}}", "rid-1")));
        Assert.IsTrue(pairs.Contains(("{{REPO_NAME}}", "NEWX-Mixed")));
        Assert.IsTrue(pairs.Contains(("{{WORK_DIR}}", "/w/svc")));
        Assert.IsTrue(pairs.Contains(("{{BRANCH}}", "master")));
        Assert.IsTrue(pairs.Contains(("{{PORT}}", "9000")));

        var yaml = YamlSubstitutor.Substitute(
            "{{REPO_ID}} {{REPO_NAME}} {{WORK_DIR}} {{BRANCH}} {{PORT}}", pairs);
        Assert.AreEqual("rid-1 NEWX-Mixed /w/svc master 9000", yaml);
    }

    [TestMethod]
    public void BuildPairs_空值跳过_不出替换对()
    {
        var pairs = YamlSubstitutor.BuildPairs(new PipelineVars(RepoId: "rid-1"), "TPLX", "NEWX");

        Assert.IsTrue(pairs.Contains(("{{REPO_ID}}", "rid-1")));
        Assert.IsFalse(pairs.Any(p => p.From == "{{REPO_NAME}}"));
        Assert.IsFalse(pairs.Any(p => p.From == "{{WORK_DIR}}"));
        Assert.IsFalse(pairs.Any(p => p.From == "{{BRANCH}}"));
        Assert.IsFalse(pairs.Any(p => p.From == "{{PORT}}"));

        // 未给值的占位符原样不动
        var yaml = YamlSubstitutor.Substitute("{{REPO_ID}} {{REPO_NAME}}", pairs);
        Assert.AreEqual("rid-1 {{REPO_NAME}}", yaml);
    }

    // ── Substitute：精确替换 ───────────────────────────────────────────────

    [TestMethod]
    public void Substitute_无匹配_原样返回()
    {
        const string yaml = "name: ACME\nstages:\n  - stage: build\n";
        var result = YamlSubstitutor.Substitute(yaml, ("TPLX-Admin", "NEWX-Admin"), ("{{REPO_ID}}", "rid"));
        Assert.AreEqual(yaml, result);
    }

    [TestMethod]
    public void Substitute_替换对顺序_长From先替不误伤()
    {
        // 短 From 在前会把长串打碎；表驱动按 pairs 顺序替换，调用方（BuildPairs）保证长串在前
        var result = YamlSubstitutor.Substitute(
            "TPLX-Admin",
            ("TPLX-Admin", "NEWX-Admin"),
            ("TPLX", "TPL9"));
        Assert.AreEqual("NEWX-Admin", result);
    }

    [TestMethod]
    public void Substitute_空串容错_空From跳过()
    {
        Assert.AreEqual("", YamlSubstitutor.Substitute("", ("A", "B")));
        Assert.AreEqual("", YamlSubstitutor.Substitute(null, ("A", "B")));
        Assert.AreEqual("AB", YamlSubstitutor.Substitute("AB", ("", "X")));
    }

    [TestMethod]
    public void Substitute_PipelineVars重载_仅占位符()
    {
        var vars = new PipelineVars(RepoName: "NEWX-Mixed", Branch: "master");
        var yaml = YamlSubstitutor.Substitute("repo: {{REPO_NAME}}\nbranch: {{BRANCH}}\npath: TPLX-Admin\n", vars);
        Assert.AreEqual("repo: NEWX-Mixed\nbranch: master\npath: TPLX-Admin\n", yaml);
    }

    // ── Diff：逐行简单 unified 风格 ────────────────────────────────────────

    [TestMethod]
    public void Diff_增删改_逐行加减块()
    {
        var change = YamlSubstitutor.Diff("a\nb\nc", "a\nb2\nc");
        StringAssert.Contains(change, "-b");
        StringAssert.Contains(change, "+b2");

        var add = YamlSubstitutor.Diff("a\nb", "a\nb\nd");
        StringAssert.Contains(add, "+d");
        Assert.IsFalse(add.Contains("-d", StringComparison.Ordinal));

        var remove = YamlSubstitutor.Diff("a\nb\nc", "a\nc");
        StringAssert.Contains(remove, "-b");
        Assert.IsFalse(remove.Contains("+b", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Diff_空串容错_无变更为空()
    {
        Assert.AreEqual("", YamlSubstitutor.Diff("a\nb", "a\nb"));
        Assert.AreEqual("", YamlSubstitutor.Diff(null, null));
        Assert.AreEqual("", YamlSubstitutor.Diff("", ""));
        StringAssert.Contains(YamlSubstitutor.Diff("", "x"), "+x");
        StringAssert.Contains(YamlSubstitutor.Diff("x", ""), "-x");
    }
}
