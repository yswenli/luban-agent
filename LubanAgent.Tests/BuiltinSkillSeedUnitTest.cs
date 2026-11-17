/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： BuiltinSkillSeedUnitTest
*描述：验证 WorkspaceManager 的内置 Skill 播种/刷新策略：
*      首次全量播种并写清单、未被改动的副本随内置内容刷新、用户改过的副本保留、
*      清单已记录但目录被删除时不补写。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class BuiltinSkillSeedUnitTest
{
    private string _skillsDir = "";

    [TestInitialize]
    public void Setup()
    {
        _skillsDir = Path.Combine(Path.GetTempPath(), "luban-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_skillsDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_skillsDir))
                Directory.Delete(_skillsDir, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结论
        }
    }

    [TestMethod]
    public void Sync_FirstRun_SeedsBuiltinAndWritesManifest()
    {
        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("v1") });

        var file = Path.Combine(_skillsDir, "demo", "SKILL.md");
        Assert.IsTrue(File.Exists(file), "缺失的内置技能应被播种");
        StringAssert.Contains(File.ReadAllText(file), "v1");
        Assert.IsTrue(File.Exists(Path.Combine(_skillsDir, ".seeded.json")), "播种后应写入内容哈希清单");
    }

    [TestMethod]
    public void Sync_UnmodifiedCopy_RefreshesWhenBuiltinChanges()
    {
        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("v1") });

        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("v2") });

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(_skillsDir, "demo", "SKILL.md")),
            "v2",
            "内容仍与清单记录一致时，应按新内置内容刷新");
    }

    [TestMethod]
    public void Sync_ModifiedCopy_IsPreserved()
    {
        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("v1") });
        var file = Path.Combine(_skillsDir, "demo", "SKILL.md");
        File.WriteAllText(file, "用户改过的内容");

        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("v2") });

        Assert.AreEqual("用户改过的内容", File.ReadAllText(file), "与清单不一致的文件视为用户改动，不得覆盖");
    }

    [TestMethod]
    public void Sync_DeletedCopy_IsNotRecreated()
    {
        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("v1") });
        Directory.Delete(Path.Combine(_skillsDir, "demo"), recursive: true);

        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("v1") });

        Assert.IsFalse(Directory.Exists(Path.Combine(_skillsDir, "demo")), "清单已记录的目录被删除后不应补写");
    }

    [TestMethod]
    public void Sync_LegacyCopyWithoutManifest_RefreshesWhenFrontmatterMatches()
    {
        // 模拟升级前的播种副本：frontmatter 与内置一致、正文是旧文本，且没有 .seeded.json
        WriteLegacyCopy(frontmatter: LegacyFrontmatter(), body: "旧内置正文");

        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("新正文") });

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(_skillsDir, "demo", "SKILL.md")),
            "新正文",
            "frontmatter 与内置一致的旧副本应按新内置刷新");
    }

    [TestMethod]
    public void Sync_LegacyCopyWithoutManifest_KeepsWhenFrontmatterDiffers()
    {
        WriteLegacyCopy(frontmatter: "---\nname: 演示技能\ndescription: 用户改过的描述\ncategory: 测试\n---\n", body: "用户正文");

        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("新正文") });

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(_skillsDir, "demo", "SKILL.md")),
            "用户正文",
            "frontmatter 与内置不一致时按用户内容保留");
    }

    [TestMethod]
    public void Sync_LegacyCopyWithoutManifest_ToleratesLfLineEndings()
    {
        // 播种内容按 Environment.NewLine 生成；跨平台/手工改写过的副本可能是 LF，仍应识别为播种副本
        WriteLegacyCopy(
            frontmatter: "---\nname: 演示技能\ndescription: 用于播种测试\ncategory: 测试\n---\n",
            body: "旧内置正文");

        WorkspaceManager.SyncBuiltinSkillFiles(_skillsDir, new[] { Skill("新正文") });

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(_skillsDir, "demo", "SKILL.md")),
            "新正文",
            "换行风格不同的旧副本仍应按新内置刷新");
    }

    private void WriteLegacyCopy(string frontmatter, string body)
    {
        var dir = Path.Combine(_skillsDir, "demo");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), frontmatter + "\n" + body);
    }

    /// <summary>与 <see cref="TestSkill"/> 的属性逐字一致的 frontmatter（换行按 Environment.NewLine）。</summary>
    private static string LegacyFrontmatter()
        => "---" + Environment.NewLine
           + "name: 演示技能" + Environment.NewLine
           + "description: 用于播种测试" + Environment.NewLine
           + "category: 测试" + Environment.NewLine
           + "---" + Environment.NewLine;

    private static TestSkill Skill(string body) => new(body);

    /// <summary>测试用最小 Skill 实现：只有 Id / 名称 / 分类 / 正文参与 SKILL.md 生成。</summary>
    private sealed class TestSkill : ISkill
    {
        private readonly string _body;

        public TestSkill(string body) => _body = body;

        public string Id => "demo";

        public string Name => "演示技能";

        public string Description => "用于播种测试";

        public string Category => "测试";

        public IEnumerable<string> Examples => Array.Empty<string>();

        public IEnumerable<string> TriggerKeywords => Array.Empty<string>();

        public string? PromptTemplate => _body;

        public Task<SkillResult> ExecuteAsync(SkillContext context, string input)
            => Task.FromResult(SkillResult.Ok(""));
    }
}