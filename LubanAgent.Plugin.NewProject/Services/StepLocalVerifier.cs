/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepLocalVerifier
*创建时间：2026/10/22
*描述：Step 3/6/7 的插件侧本地复核器（设计 §6.1.4「独立证据策略」第 4 环）
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 结论文件自报产物的<b>本地独立复核</b>（纯文件系统检查，不触网、不调 git 命令）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：Step 3/6/7 的云效与 git <b>业务失败</b>都不进 <c>TurnOutcome.Errors</c>
/// （MCP 失败返回普通字符串、git 业务失败返回 <c>Fail</c>，都不发 <c>ToolCallFailedEvent</c>），
/// 判定第 4 环恒空 ⇒ 会退化为「助手自报」。本复核器补上插件能独立核对的部分（§6.1.4）：
/// </para>
/// <list type="bullet">
/// <item>Step 3：目录存在且非空、<c>.git/HEAD</c> 有效、<c>refs/heads/&lt;pin&gt;</c> 存在、
/// <b>占位符残余命中 = 0</b>（最有价值的一项独立证据，§6.2）；</item>
/// <item>Step 6：<c>refs/remotes/origin/&lt;pushBranch&gt;</c> 存在且提交 id 与自报一致；</item>
/// <item>Step 7：流水线在远端 ⇒ <b>没有可独立核对的本地证据</b>，如实返回一条残余风险说明。</item>
/// </list>
/// <para>
/// 返回值为「问题清单」：空 = 本地复核通过。任一条非空即由 <see cref="StepReportEvaluator"/> 判失败。
/// </para>
/// </remarks>
public static class StepLocalVerifier
{
    /// <summary>占位符残余扫描时跳过的目录（与 <c>docs/step3-repositories.md</c> 的约定一致）。</summary>
    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj", "node_modules" };

    /// <summary>残余扫描的单文件上限（超过按二进制处理，跳过）。</summary>
    private const long MaxScanFileBytes = 4 * 1024 * 1024;

    /// <summary>残余扫描的文件总数上限（防止超大仓库拖垮界面线程）。</summary>
    private const int MaxScanFiles = 20000;

    /// <summary>
    /// Step 3 执行续跑后的本地复核（准备阶段不要调用：那时目录尚未克隆）。
    /// </summary>
    /// <param name="report">已解析的执行续跑结论。</param>
    /// <param name="ends">本次要求的端。</param>
    /// <returns>问题清单（空 = 通过）。</returns>
    public static IReadOnlyList<string> VerifyRepositories(StepReport report, IReadOnlyList<ProjectEnd> ends)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(ends);

        var issues = new List<string>();

        foreach (var end in ends)
        {
            var artifact = StepReportEvaluator.RepositoryArtifact(report, end);
            var extra = StepReportEvaluator.RepositoryExtra(report, end);
            var label = EndName(end);
            var localPath = artifact?.LocalPath;

            if (string.IsNullOrWhiteSpace(localPath))
            {
                issues.Add($"{label}：结论未给出 localPath，无法本地核对。");
                continue;
            }

            if (!System.IO.Directory.Exists(localPath))
            {
                issues.Add($"{label}：本地目录不存在（{localPath}）。");
                continue;
            }

            if (!HasAnyEntry(localPath!))
                issues.Add($"{label}：本地目录为空（{localPath}）。");

            var gitDir = Path.Combine(localPath!, ".git");
            if (!System.IO.Directory.Exists(gitDir))
            {
                issues.Add($"{label}：未发现 .git 目录（{gitDir}）。");
                continue;
            }

            var head = Path.Combine(gitDir, "HEAD");
            if (!System.IO.File.Exists(head) || !IsValidHead(SafeRead(head)))
                issues.Add($"{label}：.git/HEAD 缺失或未指向有效 ref。");

            var pin = FirstNonEmpty(artifact?.TemplateBranch, extra?.DefaultBranch);
            if (string.IsNullOrWhiteSpace(pin))
            {
                issues.Add($"{label}：结论未给出 pin 分支，无法核对 refs/heads/<pin>。");
            }
            else if (!RefExists(gitDir, $"refs/heads/{pin}"))
            {
                issues.Add($"{label}：未发现 refs/heads/{pin}（pin 分支未就位）。");
            }

            var tokens = (extra?.PlaceholderSources ?? new List<string>())
                .Where(one => !string.IsNullOrWhiteSpace(one))
                .Select(one => one.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (tokens.Count == 0)
            {
                issues.Add($"{label}：结论未给出占位符来源令牌（placeholderSources 为空），无法独立复核残余。");
            }
            else
            {
                var (scanned, hits) = ScanResidual(localPath!, tokens);
                if (hits > 0)
                    issues.Add($"{label}：占位符残余未清零（扫过 {scanned} 个文件，命中 {hits} 处）。");
            }
        }

        return issues;
    }

    /// <summary>
    /// Step 6 推送后的本地复核：远端跟踪分支就位且提交 id 与自报一致。
    /// </summary>
    /// <param name="report">已解析的推送结论。</param>
    /// <param name="ends">本次要求的端。</param>
    /// <returns>问题清单（空 = 通过）。</returns>
    public static IReadOnlyList<string> VerifyPush(StepReport report, IReadOnlyList<ProjectEnd> ends)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(ends);

        var issues = new List<string>();

        foreach (var end in ends)
        {
            var artifact = StepReportEvaluator.RepositoryArtifact(report, end);
            var extra = StepReportEvaluator.RepositoryExtra(report, end);
            var label = EndName(end);
            var localPath = artifact?.LocalPath;

            if (string.IsNullOrWhiteSpace(localPath) || !System.IO.Directory.Exists(localPath))
            {
                issues.Add($"{label}：本地目录不可用（{localPath}），无法核对远端跟踪分支。");
                continue;
            }

            var gitDir = Path.Combine(localPath!, ".git");
            if (!System.IO.Directory.Exists(gitDir))
            {
                issues.Add($"{label}：未发现 .git 目录，无法核对推送结果。");
                continue;
            }

            var branch = artifact?.TemplateBranch;
            if (string.IsNullOrWhiteSpace(branch))
            {
                issues.Add($"{label}：结论未给出推送分支，无法核对 refs/remotes/origin/<branch>。");
                continue;
            }

            var refName = $"refs/remotes/origin/{branch}";
            var sha = ReadRef(gitDir, refName);
            if (sha is null)
            {
                issues.Add($"{label}：未发现 {refName}（推送后本地未见远端跟踪分支，可能是推送失败）。");
                continue;
            }

            var reported = extra?.CommitId?.Trim();
            if (string.IsNullOrWhiteSpace(reported))
            {
                issues.Add($"{label}：结论未给出提交 id，无法核对推送提交。");
            }
            else if (NormalizeSha(sha) != NormalizeSha(reported))
            {
                issues.Add($"{label}：{refName} 的提交（{Shorten(sha)}）与结论自报（{Shorten(reported!)}）不一致。");
            }
        }

        return issues;
    }

    /// <summary>
    /// Step 7 的本地复核：流水线在远端，<b>没有可独立核对的本地证据</b>（§6.1.4）——
    /// 返回一条如实的残余风险说明，仅供操作记录展示，<b>不作为失败依据</b>。
    /// </summary>
    /// <param name="report">已解析的流水线结论。</param>
    /// <param name="ends">本次要求的端。</param>
    /// <returns>残余风险说明（供展示）。</returns>
    public static IReadOnlyList<string> PipelineResidualNotes(StepReport report, IReadOnlyList<ProjectEnd> ends)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(ends);

        var created = ends.Where(end => StepReportEvaluator.HasPipelineArtifact(report, end)).ToList();
        return created.Count == 0
            ? Array.Empty<string>()
            : new[]
            {
                $"流水线在云效远端，插件回读结论文件时不触网 ⇒ {created.Count} 端流水线的创建结果"
                + "没有可独立核对的本地证据（设计 §6.1.4 / C12/C13），仅以助手自报 + 工具调用记录为凭。"
            };
    }

    // ── 内部实现 ──────────────────────────────────────────────────────────

    private static bool HasAnyEntry(string directory)
    {
        try
        {
            using var enumerator = System.IO.Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            return enumerator.MoveNext();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsValidHead(string? content)
    {
        var value = content?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return false;

        if (value.StartsWith("ref:", StringComparison.Ordinal))
            return value.Length > "ref:".Length;

        return NormalizeSha(value).Length == 40;
    }

    private static string? ReadRef(string gitDir, string refName)
    {
        var loose = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(loose))
        {
            var value = SafeRead(loose)?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        var packed = Path.Combine(gitDir, "packed-refs");
        if (!System.IO.File.Exists(packed))
            return null;

        foreach (var line in SafeReadLines(packed))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] is '#' or '^')
                continue;

            var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[1], refName, StringComparison.Ordinal))
                return parts[0];
        }

        return null;
    }

    private static bool RefExists(string gitDir, string refName) => ReadRef(gitDir, refName) is not null;

    private static (int Scanned, int Hits) ScanResidual(string root, IReadOnlyList<string> tokens)
    {
        var scanned = 0;
        var hits = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0 && scanned < MaxScanFiles)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = System.IO.Directory.GetFiles(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                if (scanned >= MaxScanFiles)
                    break;

                scanned++;
                var (ok, text) = TryReadText(file);
                if (!ok)
                    continue;

                foreach (var token in tokens)
                {
                    if (text.Contains(token, StringComparison.Ordinal))
                    {
                        hits++;
                        break;
                    }
                }
            }

            string[] children;
            try
            {
                children = System.IO.Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                children = Array.Empty<string>();
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (ExcludedDirectories.Contains(name))
                    continue;
                pending.Push(child);
            }
        }

        return (scanned, hits);
    }

    private static (bool Ok, string Text) TryReadText(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > MaxScanFileBytes)
                return (false, string.Empty);

            return (true, File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (false, string.Empty);
        }
    }

    private static string? SafeRead(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeReadLines(string file)
    {
        try
        {
            return File.ReadAllLines(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(one => !string.IsNullOrWhiteSpace(one));

    private static string NormalizeSha(string? value)
    {
        var shas = (value ?? string.Empty).Trim().ToLowerInvariant();
        return shas.Length >= 40 ? shas[..40] : shas;
    }

    private static string Shorten(string sha)
        => sha.Length <= 12 ? sha : sha[..12];

    private static string EndName(ProjectEnd end) => end switch
    {
        ProjectEnd.Service => "后端",
        ProjectEnd.Admin => "管理端",
        ProjectEnd.Mobile => "移动端",
        _ => end.ToString()
    };
}