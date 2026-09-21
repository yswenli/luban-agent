/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Retrieval
*文件名： RetrievalIndexPlanner
*版本号： V1.0.0.0
*唯一标识：索引任务预扫规划器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/21
*描述：长耗时知识库任务（索引/重建/lint）的预扫计数与阶段提示文案，供 CLI 与桌面端（Codex）复用
*
*****************************************************************************/
using LuBan.AIAgent.Retrieval.Chunkers;

namespace LubanAgentCore.Retrieval;

/// <summary>
/// 索引任务预扫规划器。在真正开始嵌入前，按与 framework 完全相同的枚举/过滤规则统计待处理文件数，
/// 供两端展示「正在扫描 → 共 N 个文件，正在生成向量」的两段式提示，避免用户误以为程序卡死。
/// </summary>
public static class RetrievalIndexPlanner
{
    /// <summary>预扫失败（目录不可访问等）时返回，调用方应退化为不确定态文案。</summary>
    public const int Unknown = -1;

    /// <summary>未发现任何可索引文件时的统一提示。</summary>
    public const string NoIndexableFilesText = "未发现可索引文件（请检查扩展名、文件大小上限与排除目录）";

    /// <summary>未发现任何 wiki 页面时的统一提示。</summary>
    public const string NoWikiPagesText = "未发现 wiki 页面（wiki 目录为空或尚未初始化）";

    private static readonly ChunkerFactory Chunkers = new();

    /// <summary>
    /// 读取配置中的单文件大小上限（字节），口径与 <c>RetrievalBootstrap</c> 一致。
    /// </summary>
    public static long GetMaxFileSizeBytes(IConfiguration configuration)
        => (configuration.GetSection("LuBanAgent:Tools:Retrieval").Get<RetrievalToolOptions>() ?? new RetrievalToolOptions()).MaxFileSizeKB * 1024L;

    /// <summary>
    /// 统计目录下会被真正索引的文件数：与 <c>RetrievalService.IndexDirectoryAsync</c> 同口径
    /// （glob 以 ';' 分隔、逐层安全递归、<see cref="ChunkerFactory.ShouldIndex"/> 过滤）。
    /// </summary>
    /// <returns>待索引文件数；失败时返回 <see cref="Unknown"/>。</returns>
    public static int CountIndexableFiles(string rootPath, string? glob, long maxFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return 0;

        try
        {
            var root = Path.GetFullPath(rootPath);
            var patterns = string.IsNullOrWhiteSpace(glob)
                ? new[] { "*" }
                : glob.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return patterns.SelectMany(p => SafeEnumerateFiles(root, p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(f => Chunkers.ShouldIndex(f, root, maxFileSizeBytes));
        }
        catch (Exception ex)
        {
            Logger.Warn("预扫索引目录失败", ex, rootPath, glob ?? "");
            return Unknown;
        }
    }

    /// <summary>
    /// 统计 wiki 页面数：与 framework <c>WikiService.EnumeratePages</c> 同口径（**/*.md，排除 log.md）。
    /// </summary>
    /// <returns>页面数；失败时返回 <see cref="Unknown"/>。</returns>
    public static int CountWikiPages(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return 0;

        try
        {
            var wikiRoot = Path.Combine(workspaceRoot, "wiki");
            if (!Directory.Exists(wikiRoot)) return 0;

            return Directory.EnumerateFiles(wikiRoot, "*.md", SearchOption.AllDirectories)
                .Count(f => !string.Equals(Path.GetFileName(f), "log.md", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Warn("预扫 wiki 页面失败", ex, workspaceRoot);
            return Unknown;
        }
    }

    /// <summary>索引任务的扫描阶段文案。</summary>
    public static string IndexScanStatus(string target)
        => string.IsNullOrWhiteSpace(target) ? "正在扫描目录…" : $"正在扫描目录：{target}";

    /// <summary>索引任务的嵌入阶段文案（两段式第二段）。</summary>
    public static string IndexEmbedStatus(int fileCount, string action = "正在生成向量") => fileCount switch
    {
        Unknown => $"{action}（首次可能较久，请稍候）…",
        <= 0 => NoIndexableFilesText,
        _ => $"共 {fileCount} 个可索引文件，{action}（首次可能较久）…"
    };

    /// <summary>wiki 任务的扫描阶段文案。</summary>
    public const string WikiScanStatus = "正在扫描 wiki 页面…";

    /// <summary>wiki 任务的执行阶段文案（两段式第二段）。</summary>
    public static string WikiPagesStatus(int pageCount, string action) => pageCount switch
    {
        Unknown => $"{action}（请稍候）…",
        <= 0 => NoWikiPagesText,
        _ => $"共 {pageCount} 页，{action}…"
    };

    /// <summary>
    /// 与 framework <c>RetrievalService.SafeEnumerateFiles</c> 同实现的逐层安全枚举：
    /// 单层目录不可访问时跳过该层，不中断整体枚举。
    /// </summary>
    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
    {
        Queue<string> dirs = new();
        dirs.Enqueue(root);
        while (dirs.Count > 0)
        {
            var dir = dirs.Dequeue();
            string[] files;
            try { files = Directory.GetFiles(dir, pattern); }
            catch { continue; }
            foreach (var f in files) yield return f;
            string[] subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var d in subDirs) dirs.Enqueue(d);
        }
    }
}