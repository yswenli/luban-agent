/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepLocalVerifier
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/22
*描述：Step 3/6/7 的插件侧本地复核器——用文件系统证据补上「助手自报」之外的独立判据
*
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 结论文件自报产物的<b>本地独立复核</b>（纯文件系统检查，不触网、不调 git 命令）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：Step 3/6/7 的云效与 git <b>业务失败</b>都不进 <c>TurnOutcome.Errors</c>
/// （MCP 失败返回普通字符串、git 业务失败返回 <c>Fail</c>，都不发 <c>ToolCallFailedEvent</c>），
/// 「本轮事件流无错误事件」这一条判据恒为空 ⇒ 通过判定会退化为「助手说什么就是什么」。
/// 本复核器补上插件能独立核对的部分：
/// </para>
/// <list type="bullet">
/// <item>Step 3：目录存在且非空、<c>.git/HEAD</c> 有效、<c>refs/heads/&lt;pin&gt;</c> 存在、
/// <b>占位符残余命中 = 0</b>（最有价值的一项独立证据）；</item>
/// <item>Step 6：<c>refs/remotes/origin/&lt;pushBranch&gt;</c> 存在且提交 id 与自报一致；</item>
/// <item>Step 7：流水线在远端 ⇒ <b>没有可独立核对的本地证据</b>，如实返回一条残余风险说明。</item>
/// </list>
/// <para>
/// 返回值为「问题清单」：空 = 本地复核通过。任一条非空即由 <see cref="StepReportEvaluator"/> 判失败。
/// </para>
/// </remarks>
public static class StepLocalVerifier
{
    /// <summary>占位符残余扫描时跳过的目录（与 <c>docs/org-reference.md</c>「占位符规则」表的「跳过目录」一致）。</summary>
    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".idea", ".vscode", "node_modules", "bin", "obj",
            "packages", "TestResults", "dist", "build"
        };

    /// <summary>
    /// 残余扫描的文本扩展名白名单（与 <c>docs/org-reference.md</c>「占位符规则」表的「只改文本」、
    /// <c>docs/step3-repositories.md</c> 的占位符替换步骤一致）。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须同口径</b>：助手只改「文本白名单」内的文件，复核器若扫全部文件，
    /// 就会把助手<b>按规则有意跳过</b>的文件（二进制 / 设计资源 / 未列入白名单的名）判成「残余」——
    /// 真机已复现：管理端 <c>.env.development</c>（扩展名 <c>.development</c>）里残留 <c>tjces</c>，
    /// 助手按白名单未改、复核器却扫到，导致 Step 3 被误判失败。
    /// </remarks>
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".config", ".resx", ".manifest",
            ".json", ".xml", ".yml", ".yaml", ".toml", ".ini", ".env", ".editorconfig", ".md", ".txt",
            ".sql", ".sh", ".ps1", ".bat", ".cmd", ".dockerfile", ".html", ".htm", ".css", ".scss",
            ".less", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".vue", ".razor", ".cshtml", ".xaml",
            ".axaml", ".java", ".kt", ".kts", ".gradle", ".properties", ".pro", ".plist", ".pbxproj",
            ".xcconfig", ".swift", ".dart", ".go", ".py", ".rb", ".php"
        };

    /// <summary>无扩展名的文本文件名白名单（与文档同一张表；<c>.gitignore</c> 这类点号开头的名靠它命中）。</summary>
    private static readonly HashSet<string> TextFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Dockerfile", ".gitignore", ".gitattributes", ".npmrc", ".dockerignore", "Makefile", "LICENSE", "README"
        };

    /// <summary>
    /// 环境变量族统一按文本处理：<c>.env</c>、<c>.env.development</c>、<c>.env.production</c> 等。
    /// </summary>
    /// <remarks>
    /// 这些文件的 <c>Path.GetExtension</c> 是 <c>.env</c> / <c>.development</c> / <c>.production</c>，单靠扩展名表
    /// 会漏掉多段变体（真机踩坑点）；项目专有域名、包名常写在这里，必须一并替换。
    /// </remarks>
    private const string EnvFilePrefix = ".env";

    /// <summary>残余扫描的单文件上限（超过按二进制处理，跳过）。</summary>
    private const long MaxScanFileBytes = 4 * 1024 * 1024;

    /// <summary>残余扫描的文件总数上限（防止超大仓库拖垮界面线程）。</summary>
    private const int MaxScanFiles = 20000;

    /// <summary>
    /// 按 <c>localPath</c> 把「端 + 该端仓库产物」分组：单仓各端同值 ⇒ 归入同一组，仓级核对只做一次。
    /// </summary>
    /// <param name="report">结论。</param>
    /// <param name="ends">本次要求的端。</param>
    /// <returns>按仓根目录分组的端与产物。</returns>
    private static List<IGrouping<string, (ProjectEnd End, StepReportArtifact? Artifact)>> GroupByRepository(
        StepReport report,
        IReadOnlyList<ProjectEnd> ends)
        => ends
            .Select(end => (End: end, Artifact: StepReportEvaluator.RepositoryArtifact(report, end)))
            .GroupBy(
                pair => (pair.Artifact?.LocalPath ?? string.Empty).Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Step 3 执行续跑后的本地复核（准备阶段不要调用：那时目录尚未克隆）。
    /// </summary>
    /// <param name="report">已解析的执行续跑结论。</param>
    /// <param name="ends">本次要求的端。</param>
    /// <param name="absentModules">
    /// 未勾选端「克隆后应已删除」的模块目录（端 + 目录名）；传 <c>null</c> 表示不核对。
    /// </param>
    /// <returns>问题清单（空 = 通过）。</returns>
    public static IReadOnlyList<string> VerifyRepositories(
        StepReport report,
        IReadOnlyList<ProjectEnd> ends,
        IReadOnlyList<(ProjectEnd End, string ModulePath)>? absentModules = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(ends);

        var issues = new List<string>();

        // 单仓：各端共用同一个仓根目录（localPath 同值），所以目录 / .git / pin / 占位符残余都按仓复核**一次**；
        // 端之间只额外核对各自的仓内模块目录是否存在。
        var groups = GroupByRepository(report, ends);

        foreach (var group in groups)
        {
            var groupEnds = group.Select(pair => pair.End).ToList();
            var label = EndList(groupEnds);
            var localPath = group.Key;

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

            if (!HasAnyEntry(localPath))
                issues.Add($"{label}：本地目录为空（{localPath}）。");

            var gitDir = Path.Combine(localPath, ".git");
            if (!System.IO.Directory.Exists(gitDir))
            {
                issues.Add($"{label}：未发现 .git 目录（{gitDir}）。");
                continue;
            }

            var head = Path.Combine(gitDir, "HEAD");
            if (!System.IO.File.Exists(head) || !IsValidHead(SafeRead(head)))
                issues.Add($"{label}：.git/HEAD 缺失或未指向有效 ref。");

            var pin = group
                .Select(pair => FirstNonEmpty(
                    pair.Artifact?.TemplateBranch,
                    StepReportEvaluator.RepositoryExtra(report, pair.End)?.DefaultBranch))
                .FirstOrDefault(one => !string.IsNullOrWhiteSpace(one));
            if (string.IsNullOrWhiteSpace(pin))
            {
                issues.Add($"{label}：结论未给出 pin 分支，无法核对 refs/heads/<pin>。");
            }
            else if (!RefExists(gitDir, $"refs/heads/{pin}"))
            {
                issues.Add($"{label}：未发现 refs/heads/{pin}（pin 分支未就位）。");
            }

            // 各端的仓内模块目录必须真实存在（单仓下「端」就是模块子目录）。
            foreach (var (end, artifact) in group)
            {
                var module = artifact?.ModulePath?.Trim();
                if (string.IsNullOrWhiteSpace(module))
                {
                    issues.Add($"{EndName(end)}：结论未给出 modulePath，无法核对仓内模块目录。");
                    continue;
                }

                if (!System.IO.Directory.Exists(Path.Combine(localPath, module)))
                    issues.Add($"{EndName(end)}：仓内模块目录不存在（{Path.Combine(localPath, module)}）。");
            }

            var tokens = group
                .SelectMany(pair => (StepReportEvaluator.RepositoryExtra(report, pair.End)?.PlaceholderSources
                                     ?? new List<string>()))
                .Where(one => !string.IsNullOrWhiteSpace(one))
                .Select(one => one.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (tokens.Count == 0)
            {
                // 方案 C：占位符来源令牌为空＝该仓**没做**占位符替换，不是「无法复核」。
                // 事实依据（2026-10-23 真机）：助手克隆完一端就把结论文件写成 failed，两端 placeholderSources 都是空，
                // 旧文案只说「无法独立复核残余」，容易被当成复核器能力不足，半成品仍带着 cloned=true 落进结论。
                issues.Add($"{label}：占位符替换未完成（placeholderSources 为空）——该仓仍属未完成，"
                           + "`cloned=true` 不成立；请按 docs 的规则表替换后把实际命中的来源令牌逐字写回。");
            }
            else
            {
                var (scanned, hits) = ScanResidual(localPath, tokens);
                if (hits > 0)
                    issues.Add($"{label}：占位符残余未清零（扫过 {scanned} 个文件，命中 {hits} 处）。");
            }
        }

        // 未勾选的端：模板仓里那份模块目录应在占位符改名后被删掉；没删就说明用户会在仓里见到不该存在的端。
        if (absentModules is { Count: > 0 })
        {
            var root = ends
                .Select(end => (StepReportEvaluator.RepositoryArtifact(report, end)?.LocalPath ?? string.Empty).Trim())
                .FirstOrDefault(path => path.Length > 0);

            if (!string.IsNullOrWhiteSpace(root))
            {
                foreach (var (end, modulePath) in absentModules)
                {
                    var module = modulePath?.Trim() ?? string.Empty;
                    if (module.Length == 0)
                        continue;

                    var full = Path.Combine(root, module);
                    if (System.IO.Directory.Exists(full))
                        issues.Add($"{EndName(end)}：未勾选该端，但仓内模块目录仍存在（{full}）——克隆后应删除该目录。");
                }
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

        // 单仓：各端共用同一个仓根目录与推送分支，远端跟踪分支只核对一次；
        // 各端只核对自己自报的提交 id 是否与远端一致。
        var groups = GroupByRepository(report, ends);

        foreach (var group in groups)
        {
            var groupEnds = group.Select(pair => pair.End).ToList();
            var label = EndList(groupEnds);
            var localPath = group.Key;

            if (string.IsNullOrWhiteSpace(localPath) || !System.IO.Directory.Exists(localPath))
            {
                issues.Add($"{label}：本地目录不可用（{localPath}），无法核对远端跟踪分支。");
                continue;
            }

            var gitDir = Path.Combine(localPath, ".git");
            if (!System.IO.Directory.Exists(gitDir))
            {
                issues.Add($"{label}：未发现 .git 目录，无法核对推送结果。");
                continue;
            }

            var branches = group
                .Select(pair => pair.Artifact?.TemplateBranch?.Trim() ?? string.Empty)
                .Where(one => one.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (branches.Count == 0)
            {
                issues.Add($"{label}：结论未给出推送分支，无法核对 refs/remotes/origin/<branch>。");
                continue;
            }

            // 单仓：各端必须推同一分支；自报不一致说明结论本身有问题，不能只取首个了事。
            if (branches.Count > 1)
            {
                issues.Add($"{label}：结论给出的推送分支不一致（{string.Join("、", branches)}）——单仓模式下各端必须推同一分支。");
                continue;
            }

            var branch = branches[0];

            var refName = $"refs/remotes/origin/{branch}";
            var sha = ReadRef(gitDir, refName);
            if (sha is null)
            {
                issues.Add($"{label}：未发现 {refName}（推送后本地未见远端跟踪分支，可能是推送失败）。");
                continue;
            }

            foreach (var (end, _) in group)
            {
                var reported = StepReportEvaluator.RepositoryExtra(report, end)?.CommitId?.Trim();
                if (string.IsNullOrWhiteSpace(reported))
                {
                    issues.Add($"{EndName(end)}：结论未给出提交 id，无法核对推送提交。");
                }
                else if (NormalizeSha(sha) != NormalizeSha(reported))
                {
                    issues.Add($"{EndName(end)}：{refName} 的提交（{Shorten(sha)}）与结论自报（{Shorten(reported!)}）不一致。");
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Step 7 的本地复核：流水线在远端，<b>没有可独立核对的本地证据</b>——
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
                + "没有可独立核对的本地证据，仅以助手自报 + 工具调用记录为凭。"
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
                if (!IsTextCandidate(Path.GetFileName(file)))
                    continue;

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

    /// <summary>
    /// 判定文件是否落在「替换口径」的文本范围内：助手会改的，复核器才扫。
    /// </summary>
    /// <param name="fileName">文件名（不含目录）。</param>
    /// <returns>在文本白名单内返回 <c>true</c>。</returns>
    private static bool IsTextCandidate(string fileName)
    {
        if (TextFileNames.Contains(fileName))
            return true;

        if (fileName.StartsWith(EnvFilePrefix, StringComparison.OrdinalIgnoreCase))
            return true;

        var extension = Path.GetExtension(fileName);
        return extension.Length > 0 && TextExtensions.Contains(extension);
    }

    private static (bool Ok, string Text) TryReadText(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > MaxScanFileBytes)
                return (false, string.Empty);

            var bytes = File.ReadAllBytes(file);
            if (HasUtf16Or32Bom(bytes) || ContainsNul(bytes))
                return (false, string.Empty);

            return (true, Encoding.UTF8.GetString(bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (false, string.Empty);
        }
    }

    /// <summary>UTF-16 / UTF-32 的 BOM（助手按文档跳过这类文件，复核器同样不参与）。</summary>
    private static bool HasUtf16Or32Bom(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return true;

        return bytes.Length >= 4
            && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF;
    }

    /// <summary>前 8 KB 出现 <c>NUL</c> 视为二进制（与文档的编码边界一致）。</summary>
    private static bool ContainsNul(byte[] bytes)
    {
        var limit = Math.Min(bytes.Length, 8 * 1024);
        for (var i = 0; i < limit; i++)
        {
            if (bytes[i] == 0)
                return true;
        }

        return false;
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

    /// <summary>同仓端集合的标签（多个端共用一个仓库时标明「同仓」）。</summary>
    /// <param name="ends">端集合。</param>
    /// <returns>标签。</returns>
    private static string EndList(IReadOnlyList<ProjectEnd> ends)
        => ends.Count == 1
            ? EndName(ends[0])
            : string.Join("、", ends.Select(EndName)) + "（同仓）";
}