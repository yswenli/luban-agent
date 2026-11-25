/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services.Management
*文件名： SkillMentionMatcher
*描述：`@` 技能候选的匹配口径（桌面端 / CLI 共用）：按输入内容过滤并按相关度排序
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// `@` 技能候选匹配：id / 名称 / 描述 / 分类任一「包含」输入即命中，
/// 其中 id 或名称以输入开头者优先，同档保持入参顺序。
/// </summary>
/// <remarks>
/// Web 端有等价实现（<c>web/src/pages/Chat.tsx</c> 的 <c>matchSkills</c>），改口径须三端同步。
/// </remarks>
public static class SkillMentionMatcher
{
    /// <summary>候选上限：弹层最多展示的行数。</summary>
    public const int DefaultLimit = 10;

    /// <summary>按 <paramref name="token"/> 过滤并排序技能候选。</summary>
    /// <param name="skills">当前会话可用技能。</param>
    /// <param name="token">`@` 之后已输入的文本（空串表示刚键入 `@`）。</param>
    /// <param name="limit">返回上限。</param>
    /// <returns>前缀命中在前、其余包含命中在后；同档保持入参顺序。</returns>
    public static IReadOnlyList<AvailableSkillItem> Match(
        IEnumerable<AvailableSkillItem>? skills,
        string? token,
        int limit = DefaultLimit)
    {
        if (skills is null || limit <= 0)
        {
            return Array.Empty<AvailableSkillItem>();
        }

        var query = (token ?? string.Empty).Trim();
        var matched = new List<(AvailableSkillItem Skill, int Rank)>();
        foreach (var skill in skills)
        {
            var rank = Rank(skill, query);
            if (rank >= 0)
            {
                matched.Add((skill, rank));
            }
        }

        // OrderBy 是稳定排序：同档（同为前缀命中 / 同为包含命中）保留注册表原序
        return matched
            .OrderBy(entry => entry.Rank)
            .Take(limit)
            .Select(entry => entry.Skill)
            .ToList();
    }

    /// <summary>命中档位：<c>0</c> 前缀命中、<c>1</c> 包含命中、<c>-1</c> 未命中。</summary>
    private static int Rank(AvailableSkillItem skill, string query)
    {
        if (query.Length == 0)
        {
            return 0;
        }

        if (StartsWith(skill.Id, query) || StartsWith(skill.Name, query))
        {
            return 0;
        }

        return Contains(skill.Id, query)
            || Contains(skill.Name, query)
            || Contains(skill.Description, query)
            || Contains(skill.Category, query)
            ? 1
            : -1;
    }

    private static bool StartsWith(string? value, string query)
        => value is not null && value.StartsWith(query, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string query)
        => value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}