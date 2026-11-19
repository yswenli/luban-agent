/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： YamlSubstitutor
*描述：云效 S2 FR-3 流水线 YAML 占位符替换 + diff 生成纯函数（设计 §4.6）——
*      表驱动精确替换（代号段 + 占位符并行，不做全文盲替，测试/生产段天然不动）+
*      逐行简单 unified 风格 diff（-/+ 行块，无库依赖）。
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.6
*
*****************************************************************************/

namespace LubanAgentWebHost.Services.Init;

/// <summary>
/// FR-3 流水线替换变量（PRD 字段表替换面：代码库 id/名称、构建目录、分支、后端端口）。
/// </summary>
/// <param name="RepoId">新库 id（<c>{{REPO_ID}}</c>）。</param>
/// <param name="RepoName">新库名（<c>{{REPO_NAME}}</c>）。</param>
/// <param name="WorkDir">构建目录（<c>{{WORK_DIR}}</c>）。</param>
/// <param name="Branch">分支（<c>{{BRANCH}}</c>，FR-3 固定 master）。</param>
/// <param name="Port">后端端口（<c>{{PORT}}</c>，仅后端端）。</param>
public sealed record PipelineVars(
    string? RepoId = null,
    string? RepoName = null,
    string? WorkDir = null,
    string? Branch = null,
    int? Port = null);

/// <summary>
/// 流水线 YAML 表驱动替换 + diff（纯函数）。
/// </summary>
/// <remarks>
/// 替换对以 T13 实机校正：当前口径为「占位符形态 <c>{{REPO_ID}}</c> 等 + 代号段形态
/// （<c>TPLX-Admin</c> → <c>NEWX-Admin</c>、仓库名串、裸代号）」并行入表；
/// 只替换给定 From 串，天然不误伤测试/生产段（不做全文盲替）。
/// </remarks>
public static class YamlSubstitutor
{
    /// <summary>
    /// 表驱动替换：按 <paramref name="pairs"/> 顺序逐对全量替换（<b>顺序敏感</b>：长 From 先于其子串）。
    /// </summary>
    /// <param name="yaml">母版 YAML（null/空按空串）。</param>
    /// <param name="pairs">替换对（From 空跳过；按给出顺序执行）。</param>
    /// <returns>替换后文本。</returns>
    public static string Substitute(string? yaml, params (string From, string To)[] pairs)
    {
        var text = yaml ?? string.Empty;
        if (pairs is null)
        {
            return text;
        }

        foreach (var (from, to) in pairs)
        {
            if (string.IsNullOrEmpty(from))
            {
                continue;   // 空 From 是病态对（Replace("") 会到处插值），跳过
            }

            text = text.Replace(from, to ?? string.Empty, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>
    /// 占位符替换便捷重载（仅 <see cref="PipelineVars"/> 占位符对，不做代号段替换）。
    /// </summary>
    /// <param name="yaml">母版 YAML。</param>
    /// <param name="vars">替换变量。</param>
    /// <returns>替换后文本。</returns>
    public static string Substitute(string? yaml, PipelineVars? vars)
        => Substitute(yaml, PlaceholderPairs(vars));

    /// <summary>
    /// 组装替换对（代号段替换 + 占位符替换并行；长 From 在前、裸代号殿后，null 值不出对）。
    /// </summary>
    /// <remarks>替换对以 T13 实机校正（实机 YAML 占位符形态未见，先按 <c>{{REPO_ID}}</c> 等约定入表）。</remarks>
    /// <param name="vars">替换变量。</param>
    /// <param name="templateCode">母版代号。</param>
    /// <param name="newCode">新代号。</param>
    /// <returns>替换对序列。</returns>
    public static (string From, string To)[] BuildPairs(PipelineVars? vars, string? templateCode, string? newCode)
    {
        var pairs = new List<(string, string)>(PlaceholderPairs(vars));

        // 代号段替换：带后缀长串在前（-Mixed 仓库名串 / 三端目录名串），裸代号殿后——
        // 顺序即防子串误伤（"TPLX-Admin" 先于 "TPLX"，不会被裸代号打碎）
        if (!string.IsNullOrEmpty(templateCode) && !string.IsNullOrEmpty(newCode))
        {
            foreach (var suffix in new[] { "-Mixed", "-Admin", "-Mobile", "-Service" })
            {
                pairs.Add((templateCode + suffix, newCode + suffix));
            }

            pairs.Add((templateCode, newCode));
        }

        return pairs.ToArray();
    }

    /// <summary>占位符替换对（值为 null 的项不出对，替换时原样保留）。</summary>
    private static (string From, string To)[] PlaceholderPairs(PipelineVars? vars)
    {
        if (vars is null)
        {
            return [];
        }

        var pairs = new List<(string, string)>(5);
        void Add(string from, string? to)
        {
            if (!string.IsNullOrEmpty(to))
            {
                pairs.Add((from, to));
            }
        }

        Add("{{REPO_ID}}", vars.RepoId);
        Add("{{REPO_NAME}}", vars.RepoName);
        Add("{{WORK_DIR}}", vars.WorkDir);
        Add("{{BRANCH}}", vars.Branch);
        Add("{{PORT}}", vars.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return [.. pairs];
    }

    /// <summary>
    /// 逐行简单 unified 风格 diff：<c>-</c>/<c>+</c> 行块（无库依赖、无上下文行；无变更返回空串）。
    /// </summary>
    /// <remarks>行级 LCS：等值行不输出，变更段按「先 <c>-</c> 全部删行、后 <c>+</c> 全部增行」成块。</remarks>
    /// <param name="original">原 YAML。</param>
    /// <param name="modified">修改后 YAML。</param>
    /// <returns>diff 文本。</returns>
    public static string Diff(string? original, string? modified)
    {
        var a = SplitLines(original);
        var b = SplitLines(modified);

        // LCS 动态规划（流水线 YAML 体量小，O(n·m) 可接受；无库依赖）
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var output = new List<string>();
        var pendingRemoved = new List<string>();
        var pendingAdded = new List<string>();

        void Flush()
        {
            foreach (var line in pendingRemoved)
            {
                output.Add("-" + line);
            }

            foreach (var line in pendingAdded)
            {
                output.Add("+" + line);
            }

            pendingRemoved.Clear();
            pendingAdded.Clear();
        }

        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                Flush();   // 等值行收口当前变更块并跳过（无上下文行）
                x++;
                y++;
            }
            else if (y < b.Length && (x >= a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                pendingAdded.Add(b[y]);
                y++;
            }
            else
            {
                pendingRemoved.Add(a[x]);
                x++;
            }
        }

        Flush();
        return string.Join("\n", output);
    }

    /// <summary>按行切分（兼容 CRLF；空串 → 空数组，null 同空串）。</summary>
    private static string[] SplitLines(string? text)
        => string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
