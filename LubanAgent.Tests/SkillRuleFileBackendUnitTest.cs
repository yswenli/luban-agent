/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SkillRuleFileBackendUnitTest
*创建时间：2026/10/27
*描述：Skill / Rule 管理服务的「文件型」后端单测。
*      服务把 SKILL.md 与 rules/*.json 的读写约定从 Codex 设置页搬进 Core，
*      这里锁死三件事：frontmatter/JSON 往返不丢字段、技能保存只改 frontmatter 不动目录
*      （规则改名仍移动/删除旧文件）、以及 Id 不允许路径穿越。
*      仅测文件后端，故 ConfigManager / SkillRegistry / RuleEngine / IWorkspaceManager
*      一律传 null（文件后端方法不触碰它们）。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class SkillRuleFileBackendUnitTest
{
    private string _workspaceRoot = "";

    [TestInitialize]
    public void Setup()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "luban-skillrule-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspaceRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_workspaceRoot))
                Directory.Delete(_workspaceRoot, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结论
        }
    }

    // ---------------- Skill 文件型后端 ----------------

    [TestMethod]
    public void Skill_SaveFile_ReadFile_RoundTripsAllEditableFields()
    {
        var svc = NewSkillService();

        svc.SaveFile(null, new SkillFileContent("demo", "示例描述", "research", "a,b", "# 标题\n正文"), _workspaceRoot);

        var content = svc.ReadFile("demo", _workspaceRoot);

        Assert.IsNotNull(content);
        Assert.AreEqual("demo", content.Name);
        Assert.AreEqual("示例描述", content.Description);
        Assert.AreEqual("research", content.Category);
        Assert.AreEqual("a,b", content.Triggers);
        StringAssert.Contains(content.Body, "# 标题");
        StringAssert.Contains(content.Body, "正文");
    }

    [TestMethod]
    public void Skill_SaveFile_KeepsDirectoryWhenNameChanges()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("old-name", "描述", "custom", "", "正文"), _workspaceRoot);

        var key = svc.SaveFile("old-name", new SkillFileContent("新的显示名", "描述", "custom", "", "正文"), _workspaceRoot);

        var skillsDir = Path.Combine(_workspaceRoot, ".luban-agent", "skills");
        Assert.AreEqual("old-name", key, "保存返回的键是目录名，不是显示名");
        Assert.IsTrue(File.Exists(Path.Combine(skillsDir, "old-name", "SKILL.md")), "改名不改目录");
        Assert.IsFalse(Directory.Exists(Path.Combine(skillsDir, "新的显示名")), "不得按显示名新建目录");

        var content = svc.ReadFile("old-name", _workspaceRoot);
        Assert.IsNotNull(content);
        Assert.AreEqual("新的显示名", content.Name, "frontmatter 的 name 必须更新为新显示名");
    }

    [TestMethod]
    public void Skill_SaveFile_New_ThrowsWhenDirectoryExists()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("dup", "描述", "custom", "", "正文"), _workspaceRoot);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => svc.SaveFile(null, new SkillFileContent("dup", "描述", "custom", "", "正文"), _workspaceRoot));
    }

    [TestMethod]
    public void Skill_SaveFile_NormalizesMultilineFrontmatter()
    {
        var svc = NewSkillService();

        svc.SaveFile(null, new SkillFileContent(
            "cache-fix", "第一行\nname: 注入", "设计\n---\n伪造正文", "a\r\nb", "正文原样\n保留换行"), _workspaceRoot);

        var content = svc.ReadFile("cache-fix", _workspaceRoot);

        Assert.IsNotNull(content);
        Assert.AreEqual("cache-fix", content.Name, "name 不受 description 注入影响");
        Assert.AreEqual("第一行 name: 注入", content.Description, "description 的换行应折叠为空格");
        Assert.AreEqual("设计 --- 伪造正文", content.Category, "category 不得把 frontmatter 提前闭合");
        Assert.AreEqual("a b", content.Triggers);
        StringAssert.Contains(content.Body, "正文原样\n保留换行", "正文不受 frontmatter 归一影响");
    }

    [TestMethod]
    public void Skill_SaveFile_ThrowsWhenDescriptionEmpty()
    {
        var svc = NewSkillService();

        Assert.ThrowsExactly<ArgumentException>(() =>
            svc.SaveFile(null, new SkillFileContent("desc-test", "   ", "custom", "", "正文"), _workspaceRoot));
    }

    [TestMethod]
    public void Skill_SaveFile_Existing_AllowsDisplayNameWithFileNameChars()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("cache-fix", "描述", "custom", "", "正文"), _workspaceRoot);

        // 显示名与目录名已解耦：冒号等文件名非法字符只出现在 frontmatter，不应再被拒
        var key = svc.SaveFile("cache-fix", new SkillFileContent("Fix: 缓存", "描述", "custom", "", "正文"), _workspaceRoot);

        Assert.AreEqual("cache-fix", key);
        Assert.AreEqual("Fix: 缓存", svc.ReadFile("cache-fix", _workspaceRoot)!.Name);
    }

    [TestMethod]
    public void Skill_SaveFile_New_RejectsNameUnsafeAsDirectory()
    {
        var svc = NewSkillService();

        // 新建时目录名由名称生成，含路径分隔符必须拒绝
        Assert.ThrowsExactly<ArgumentException>(
            () => svc.SaveFile(null, new SkillFileContent("a/b", "描述", "custom", "", "正文"), _workspaceRoot));
    }

    [TestMethod]
    public void Skill_ListFileEntries_ReturnsSeparatedNameAndCategory()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("brainstorming", "描述", "设计", "", "正文"), _workspaceRoot);
        svc.SaveFile(null, new SkillFileContent("debug", "描述", "工程", "", "正文"), _workspaceRoot);

        // 改显示名不改目录：列表项应显示新名称，键仍是目录名
        svc.SaveFile("brainstorming", new SkillFileContent("头脑风暴", "描述", "设计", "", "正文"), _workspaceRoot);
        svc.SaveFile("debug", new SkillFileContent("调试助手", "描述", "工程", "", "正文"), _workspaceRoot);

        var entries = svc.ListFileEntries(_workspaceRoot);

        CollectionAssert.AreEqual(new[] { "brainstorming", "debug" }, entries.Select(e => e.Id).ToArray(),
            "按显示名排序：头 U+5934 排在调 U+8C03 之前");
        Assert.AreEqual("头脑风暴", entries[0].Name);
        Assert.AreEqual("设计", entries[0].Category);
        Assert.AreEqual("调试助手", entries[1].Name);
        Assert.AreEqual("工程", entries[1].Category);
    }

    [TestMethod]
    public void Skill_ImportZip_WritesNestedSkillAndSkipsInvalid()
    {
        var svc = NewSkillService();
        var zipPath = Path.Combine(_workspaceRoot, "skills.zip");

        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("skills/brainstorming/SKILL.md");
            using (var writer = new StreamWriter(entry.Open()))
                writer.Write("---\nname: 头脑风暴\ncategory: 设计\n---\n正文");

            archive.CreateEntry("skills/readme.txt");
        }

        using (var input = File.OpenRead(zipPath))
        {
            var result = svc.ImportZip(input, _workspaceRoot);

            CollectionAssert.Contains(result.Imported.ToArray(), "brainstorming");
            Assert.AreEqual(0, result.Skipped.Count, "非技能文件不应计为被拒条目");
            CollectionAssert.Contains(result.Ignored.ToArray(), "skills/readme.txt", "非 SKILL.md 文件单独计入未导入");
        }

        var content = svc.ReadFile("brainstorming", _workspaceRoot);
        Assert.IsNotNull(content);
        Assert.AreEqual("头脑风暴", content.Name);
    }

    [TestMethod]
    public void Skill_ImportZip_RejectsOversizedSkillFile()
    {
        var svc = NewSkillService();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("huge/SKILL.md");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(new string('x', 300 * 1024));
        }
        buffer.Position = 0;

        var result = svc.ImportZip(buffer, _workspaceRoot);

        Assert.AreEqual(0, result.Imported.Count);
        Assert.AreEqual(1, result.Skipped.Count, "超过 256KB 的 SKILL.md 应被拒");
        Assert.IsFalse(Directory.Exists(Path.Combine(_workspaceRoot, ".luban-agent", "skills", "huge")));
    }

    [TestMethod]
    public void Skill_ImportZip_RejectsTooManyEntries()
    {
        var svc = NewSkillService();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < 501; i++)
            {
                archive.CreateEntry($"filler-{i}.txt");
            }
        }
        buffer.Position = 0;

        Assert.ThrowsExactly<InvalidOperationException>(() => svc.ImportZip(buffer, _workspaceRoot));
    }

    [TestMethod]
    public void Skill_ImportZip_OverwritesExistingDirectory()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("demo", "旧描述", "custom", "", "旧正文"), _workspaceRoot);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("demo/SKILL.md");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("---\nname: 新名\ncategory: 设计\n---\n新正文");
        }
        buffer.Position = 0;

        svc.ImportZip(buffer, _workspaceRoot);

        var content = svc.ReadFile("demo", _workspaceRoot);
        Assert.IsNotNull(content);
        Assert.AreEqual("新名", content.Name, "同名目录以新导入内容覆盖");
        StringAssert.Contains(content.Body, "新正文");
    }

    [TestMethod]
    public void Skill_ListFileIds_ReturnsSortedIdsWithSkillMdOnly()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("zeta", "描述", "custom", "", "正文"), _workspaceRoot);
        svc.SaveFile(null, new SkillFileContent("alpha", "描述", "custom", "", "正文"), _workspaceRoot);

        // 无 SKILL.md 的目录不算 Skill
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".luban-agent", "skills", "no-skill-md"));

        var ids = svc.ListFileIds(_workspaceRoot);

        CollectionAssert.AreEqual(new[] { "alpha", "zeta" }, ids.ToArray());
    }

    [TestMethod]
    public void Skill_DeleteAsync_File_RemovesDirectory()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("gone", "描述", "custom", "", "正文"), _workspaceRoot);

        svc.DeleteAsync("gone", ManagedOrigin.File, _workspaceRoot).GetAwaiter().GetResult();

        Assert.IsNull(svc.ReadFile("gone", _workspaceRoot));
    }

    [TestMethod]
    public void Skill_SaveFile_RejectsPathTraversalName()
    {
        var svc = NewSkillService();

        Assert.ThrowsExactly<ArgumentException>(
            () => svc.SaveFile(null, new SkillFileContent("..\\evil", "描述", "custom", "", "正文"), _workspaceRoot));
    }

    // ---------------- Rule 文件型后端 ----------------

    [TestMethod]
    public void Rule_SaveFile_ReadFile_RoundTripsAllFields()
    {
        var svc = NewRuleService();

        svc.SaveFile(null, new CustomRuleConfig
        {
            Id = "no-danger",
            Name = "禁止危险动作",
            Description = "示例",
            ActionTypePattern = "tool.*",
            TargetPattern = "rm -rf",
            Action = "allow",
            Priority = 7,
            Enabled = false,
            Content = "引导文本",
        }, _workspaceRoot);

        var cfg = svc.ReadFile("no-danger", _workspaceRoot);

        Assert.IsNotNull(cfg);
        Assert.AreEqual("禁止危险动作", cfg.Name);
        Assert.AreEqual("tool.*", cfg.ActionTypePattern);
        Assert.AreEqual("rm -rf", cfg.TargetPattern);
        Assert.AreEqual("allow", cfg.Action);
        Assert.AreEqual(7, cfg.Priority);
        Assert.IsFalse(cfg.Enabled);
        Assert.AreEqual("引导文本", cfg.Content);
    }

    [TestMethod]
    public void Rule_SaveFile_Rename_DeletesOldFile()
    {
        var svc = NewRuleService();
        svc.SaveFile(null, new CustomRuleConfig { Id = "old-rule", Name = "旧" }, _workspaceRoot);

        svc.SaveFile("old-rule", new CustomRuleConfig { Id = "new-rule", Name = "新" }, _workspaceRoot);

        var rulesDir = Path.Combine(_workspaceRoot, ".luban-agent", "rules");
        Assert.IsFalse(File.Exists(Path.Combine(rulesDir, "old-rule.json")), "改名后旧规则文件必须消失");
        Assert.IsTrue(File.Exists(Path.Combine(rulesDir, "new-rule.json")));
    }

    [TestMethod]
    public void Rule_ListFileIds_ReturnsSortedIds()
    {
        var svc = NewRuleService();
        svc.SaveFile(null, new CustomRuleConfig { Id = "z-rule", Name = "z" }, _workspaceRoot);
        svc.SaveFile(null, new CustomRuleConfig { Id = "a-rule", Name = "a" }, _workspaceRoot);

        CollectionAssert.AreEqual(new[] { "a-rule", "z-rule" }, svc.ListFileIds(_workspaceRoot).ToArray());
    }

    [TestMethod]
    public void Rule_DeleteAsync_File_RemovesFile()
    {
        var svc = NewRuleService();
        svc.SaveFile(null, new CustomRuleConfig { Id = "gone", Name = "gone" }, _workspaceRoot);

        svc.DeleteAsync("gone", ManagedOrigin.File, _workspaceRoot).GetAwaiter().GetResult();

        Assert.IsNull(svc.ReadFile("gone", _workspaceRoot));
    }

    [TestMethod]
    public void Rule_SaveFile_NormalizesActionAndDefaults()
    {
        var svc = NewRuleService();

        svc.SaveFile(null, new CustomRuleConfig { Id = "defaulted", Name = "默认值" }, _workspaceRoot);

        var cfg = svc.ReadFile("defaulted", _workspaceRoot);
        Assert.IsNotNull(cfg);
        Assert.AreEqual("deny", cfg.Action, "Action 空值必须落为 deny");
        Assert.AreEqual("*", cfg.ActionTypePattern);
        Assert.AreEqual("*", cfg.TargetPattern);
    }

    [TestMethod]
    public void Rule_SaveFile_RejectsIllegalAction()
    {
        var svc = NewRuleService();

        Assert.ThrowsExactly<ArgumentException>(
            () => svc.SaveFile(null, new CustomRuleConfig { Id = "bad", Name = "bad", Action = "reject" }, _workspaceRoot));
    }

    // ---------------- 构造（只测文件后端，其余依赖传 null） ----------------

    private static SkillManagementService NewSkillService()
        => new(config: null!, registry: null!, workspaces: null!);

    private static RuleManagementService NewRuleService()
        => new(config: null!, engine: null!, workspaces: null!);
}