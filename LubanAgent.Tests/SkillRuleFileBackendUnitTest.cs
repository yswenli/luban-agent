/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SkillRuleFileBackendUnitTest
*创建时间：2026/10/27
*描述：Skill / Rule 管理服务的「文件型」后端单测。
*      服务把 SKILL.md 与 rules/*.json 的读写约定从 Codex 设置页搬进 Core，
*      这里锁死三件事：frontmatter/JSON 往返不丢字段、改名即移动/删除旧文件、
*      以及 Id 不允许路径穿越。
*      仅测文件后端，故 ConfigManager / SkillRegistry / RuleEngine / IWorkspaceManager
*      一律传 null（文件后端方法不触碰它们）。
*
*****************************************************************************/

using LuBan.AIAgent.Configuration;
using LubanAgentCore.Services.Management;
using LubanAgentCore.Services.Management.Models;

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
    public void Skill_SaveFile_Rename_MovesDirectoryAndRemovesOld()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("old-name", "描述", "custom", "", "正文"), _workspaceRoot);

        svc.SaveFile("old-name", new SkillFileContent("new-name", "描述", "custom", "", "正文"), _workspaceRoot);

        var skillsDir = Path.Combine(_workspaceRoot, ".luban-agent", "skills");
        Assert.IsFalse(Directory.Exists(Path.Combine(skillsDir, "old-name")), "改名后旧目录必须消失");
        Assert.IsTrue(File.Exists(Path.Combine(skillsDir, "new-name", "SKILL.md")));
    }

    [TestMethod]
    public void Skill_SaveFile_Rename_ThrowsWhenTargetExists()
    {
        var svc = NewSkillService();
        svc.SaveFile(null, new SkillFileContent("a", "描述", "custom", "", "正文"), _workspaceRoot);
        svc.SaveFile(null, new SkillFileContent("b", "描述", "custom", "", "正文"), _workspaceRoot);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => svc.SaveFile("a", new SkillFileContent("b", "描述", "custom", "", "正文"), _workspaceRoot));
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