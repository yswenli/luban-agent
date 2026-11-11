/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AgentsMdInitializerUnitTest
*描述：验证 AGENTS.md 初始化服务在规范化模型输出时，
*      必定补齐「Skill 流程遵守」与「协作约定」两个固定章节，且重复规范化保持幂等
*****************************************************************************/

using LubanAgentCore.Services;
using LuBan.AIAgent.Skills;
using LuBan.AIAgent.Skills.BuiltIn;

namespace LubanAgent.Tests;

[TestClass]
public class AgentsMdInitializerUnitTest
{
    private static AgentsMdInitializer CreateInitializer()
        => new(new BuiltinSkillCatalog(new[] { new AgentsMdGeneratorSkill() }));

    /// <summary>「协作约定」正文的逐字期望值；按行存放，避免源文件换行符影响比对。</summary>
    private static readonly string[] ExpectedCollabConventionLines =
    [
        "- 全程中文；结论先行，再给一句话依据。回复精简但不省略依据与风险。",
        "- 位置以「文件 + 符号/章节」为主锚，`文件:行号` 为辅；编辑后重新核对行号再引用。",
        "- 有上游设计依据时给出处（如 `docs/superpowers/specs/<文件>.md` 的章节）；没有就写「无上游设计依据」，不编造。",
        "- 代码与注释不写「见 PRD §X」「按设计 vX」这类溯源标记，规则直接写完整；跨文件引用用「文件名 + 主题」。",
        "- 严格按任务范围执行，不顺手改无关文件或重构；范围外问题先报告，然后继续原任务。必要的连带改动（接口调用方、README 中英同步）不算扩面。",
        "- 改动代码后不得自行修改项目的包版本（`.csproj` 的 `<Version>`），也不得增删、升级或降级 `PackageReference` 版本；包版本管理由用户处理，除非当次任务明确要求。",
        "- 多文件改动、对外行为变更、不可逆操作或存在多种合理实现时：先给 2–3 个方案 + 权衡，暂停等明确许可再动手；中途失效回到比选，不自行换方案。",
        "- 不执行 `git push`、`reset --hard`、`clean`、改写已有提交，除非当次点名。",
        "- 凭据不写入代码/注释/文档/日志/提交；文档只写位置。",
        "- 未实际运行的构建/测试不得声称通过，报告附关键输出或退出码；环境缺失导致的失败写「环境原因」，只做静态核对写「未运行」。",
        "- 具体执行类任务（落码、代码审查、测试补齐、批量文档同步等）一律交给子代理完成；主会话只做需求澄清、方案比选、任务拆解、委派与验收，不亲自改代码。子代理返回后由主会话复核改动范围与构建/测试证据再下结论，不直接采信其自述。",
        "- 流程型 skill（brainstorming / grilling 等）只让渡「步骤顺序」，不因 skill 放宽范围与证据要求。",
    ];

    [TestMethod]
    public void Normalize_AppendsSkillDirective_WhenMissing()
    {
        var initializer = CreateInitializer();

        var markdown = initializer.Normalize("# AGENTS.md\n\n## 角色定位\n\n测试 Agent");

        StringAssert.Contains(markdown, AgentsMdInitializer.SkillDirectiveHeading);
        StringAssert.Contains(markdown, "流程型 skill");
        StringAssert.Contains(markdown, "brainstorming");
        StringAssert.Contains(markdown, "grilling");
        Assert.IsTrue(markdown.EndsWith("。\n"), "正文应以换行结束");
    }

    [TestMethod]
    public void Normalize_IsIdempotent_WhenDirectiveAlreadyPresent()
    {
        var initializer = CreateInitializer();

        var once = initializer.Normalize("# AGENTS.md");
        var twice = initializer.Normalize(once);

        Assert.AreEqual(once, twice);
        Assert.AreEqual(1, CountOccurrences(twice, AgentsMdInitializer.SkillDirectiveHeading));
    }

    [TestMethod]
    public void Normalize_StripsFence_ThenAppendsDirective()
    {
        var initializer = CreateInitializer();

        var markdown = initializer.Normalize("```markdown\n# AGENTS.md\n\n正文\n```\n");

        Assert.IsTrue(markdown.StartsWith("# AGENTS.md"), "应剥离外层代码围栏");
        StringAssert.Contains(markdown, AgentsMdInitializer.SkillDirectiveHeading);
    }

    [TestMethod]
    public void Normalize_EmptyOutput_StaysEmpty()
    {
        var initializer = CreateInitializer();

        Assert.AreEqual(string.Empty, initializer.Normalize("   "));
        Assert.AreEqual(string.Empty, initializer.Normalize("```\n```"));
    }

    [TestMethod]
    public void Normalize_AppendsCollabConvention_WhenMissing()
    {
        var initializer = CreateInitializer();

        var markdown = initializer.Normalize("# AGENTS.md\n\n## 角色定位\n\n测试 Agent");

        // 正文逐字锁定：标题 + 空行 + 12 条顺序与措辞，改动常量即会失败
        var expected = $"{AgentsMdInitializer.CollabConventionHeading}\n\n{string.Join("\n", ExpectedCollabConventionLines)}";
        StringAssert.Contains(markdown, expected, "「协作约定」标题与 12 条正文须逐字一致");
        Assert.AreEqual(12, ExpectedCollabConventionLines.Length, "「协作约定」应为 12 条");

        var skillIndex = markdown.IndexOf(AgentsMdInitializer.SkillDirectiveHeading, StringComparison.Ordinal);
        var collabIndex = markdown.IndexOf("## 协作约定", StringComparison.Ordinal);
        Assert.IsTrue(skillIndex > 0, "应包含「Skill 流程遵守」章节");
        Assert.IsTrue(collabIndex > skillIndex, "「协作约定」应位于「Skill 流程遵守」之后");
    }

    [TestMethod]
    public void Normalize_CollabConvention_IsIdempotent()
    {
        var initializer = CreateInitializer();

        var once = initializer.Normalize("# AGENTS.md");
        var twice = initializer.Normalize(once);

        Assert.AreEqual(once, twice);
        Assert.AreEqual(1, CountOccurrences(twice, "## 协作约定"));
    }

    [TestMethod]
    public void Normalize_SkipsCollabConvention_WhenAlreadyPresent()
    {
        var initializer = CreateInitializer();

        var markdown = initializer.Normalize("# AGENTS.md\n\n## 协作约定（优先于助手默认行为）\n\n- 已有条目\n");

        Assert.AreEqual(1, CountOccurrences(markdown, "## 协作约定"));
        StringAssert.Contains(markdown, "- 已有条目");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}