/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AgentsMdInitializerUnitTest
*描述：验证 AGENTS.md 初始化服务在规范化模型输出时，
*      必定补齐「Skill 流程遵守」固定章节，且重复规范化保持幂等
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