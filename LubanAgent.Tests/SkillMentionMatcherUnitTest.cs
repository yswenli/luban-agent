/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SkillMentionMatcherUnitTest
*描述：三端 `@` 技能候选匹配口径单测——锁死「按输入内容过滤 + 前缀优先」语义：
*      1) id / 名称 / 描述 / 分类任一包含即命中，不区分大小写；
*      2) id 或名称前缀命中的排在同档包含命中之前，同档保持入参顺序；
*      3) 空输入返回首屏候选，返回条数受上限约束。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class SkillMentionMatcherUnitTest
{
    [TestMethod]
    public void Match_MatchesDescriptionAndCategory_BySubstring()
    {
        var skills = new[]
        {
            Skill("brainstorming", "头脑风暴", "多轮提问澄清需求并产出方案", "设计"),
            Skill("newproject", "新建项目", "创建项目骨架", "工程"),
        };

        var byDescription = SkillMentionMatcher.Match(skills, "澄清");
        CollectionAssert.AreEqual(new[] { "brainstorming" }, Ids(byDescription));

        var byCategory = SkillMentionMatcher.Match(skills, "工程");
        CollectionAssert.AreEqual(new[] { "newproject" }, Ids(byCategory));
    }

    [TestMethod]
    public void Match_PrefixHitsComeBeforeContainsHits()
    {
        var skills = new[]
        {
            // "design" 只出现在 id 中段与描述里
            Skill("ui-design", "界面设计", "design tokens 说明", "设计"),
            // id 前缀命中
            Skill("design", "方案设计", "草稿", "设计"),
        };

        CollectionAssert.AreEqual(new[] { "design", "ui-design" }, Ids(SkillMentionMatcher.Match(skills, "design")));
    }

    [TestMethod]
    public void Match_IgnoresCase()
    {
        var skills = new[] { Skill("NewProject", "新建项目", "创建项目骨架", "Engineering") };

        CollectionAssert.AreEqual(new[] { "NewProject" }, Ids(SkillMentionMatcher.Match(skills, "newpro")));
        CollectionAssert.AreEqual(new[] { "NewProject" }, Ids(SkillMentionMatcher.Match(skills, "engineering")));
    }

    [TestMethod]
    public void Match_EmptyToken_ReturnsFirstPageInOriginalOrder()
    {
        var skills = Enumerable.Range(1, 12)
            .Select(index => Skill($"skill-{index:D2}", $"技能{index}", "描述", "分类"))
            .ToList();

        var result = SkillMentionMatcher.Match(skills, "  ");

        Assert.AreEqual(SkillMentionMatcher.DefaultLimit, result.Count);
        CollectionAssert.AreEqual(
            skills.Take(SkillMentionMatcher.DefaultLimit).Select(skill => skill.Id).ToList(),
            Ids(result));
    }

    [TestMethod]
    public void Match_NoHit_ReturnsEmpty()
    {
        var skills = new[] { Skill("brainstorming", "头脑风暴", "澄清需求", "设计") };

        Assert.AreEqual(0, SkillMentionMatcher.Match(skills, "工作区里没有的词").Count);
    }

    private static List<string> Ids(IReadOnlyList<AvailableSkillItem> items)
        => items.Select(item => item.Id).ToList();

    private static AvailableSkillItem Skill(string id, string name, string description, string category)
        => new(id, name, description, category, IsBuiltin: false);
}