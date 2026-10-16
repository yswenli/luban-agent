/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： PlaceholderReplacer
*创建时间：2026/10/16
*描述：模板代码占位符替换（PRD §4.3 Step 3「命名空间 / 项目名 / 缩写」）
*****************************************************************************/

using System.Text;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 占位符替换结果（供时间线展示，便于首次真机联调时校准替换规则）。
/// </summary>
/// <param name="FilesChanged">内容被改写的文件数。</param>
/// <param name="EntriesRenamed">被重命名的文件 / 目录数。</param>
/// <param name="FilesSkipped">因二进制 / 过大 / 编码不受支持而跳过的文件数。</param>
internal sealed record PlaceholderReplaceResult(int FilesChanged, int EntriesRenamed, int FilesSkipped);

/// <summary>
/// 模板代码占位符替换器：对已克隆的仓库目录做「文件内容 + 文件 / 目录名」的字面量替换。
/// </summary>
/// <remarks>
/// <para>
/// <b>为何是字面量而非正则</b>：模板缩写（如 <c>TJCES</c>）会以多种大小写写法散落在命名空间、csproj、
/// 配置与文档里，忽略大小写的模糊匹配会把 <c>tjces</c> 意外改写进 URL、注释与第三方标识；故替换表由
/// 插件配置显式列出大小写变体（<see cref="Configuration.NewProjectSettings.BuildReplacements"/>）。
/// </para>
/// <para>
/// <b>安全边界</b>：跳过 <c>.git</c> / 构建产物目录；非文本扩展名、含 <c>NUL</c> 的二进制、超过 4MB 的文件、
/// 以及 UTF-16 编码文件一律不改写（宁可漏改也不破坏文件）；UTF-8 BOM 原样保留。
/// </para>
/// </remarks>
internal static class PlaceholderReplacer
{
    /// <summary>参与内容替换的文本扩展名（未列出的一律不触碰）。</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".config", ".resx", ".manifest",
        ".json", ".xml", ".yml", ".yaml", ".toml", ".ini", ".env", ".editorconfig",
        ".md", ".txt", ".sql", ".sh", ".ps1", ".bat", ".cmd", ".dockerfile",
        ".html", ".htm", ".css", ".scss", ".less", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx",
        ".vue", ".razor", ".cshtml", ".xaml", ".axaml",
        ".java", ".kt", ".kts", ".gradle", ".properties", ".pro", ".plist", ".pbxproj", ".xcconfig",
        ".swift", ".dart", ".go", ".py", ".rb", ".php", ".gradle"
    };

    /// <summary>无扩展名但属于文本的文件名（小写比较）。</summary>
    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dockerfile", ".gitignore", ".gitattributes", ".npmrc", ".dockerignore", "Makefile", "LICENSE", "README"
    };

    /// <summary>不进入的目录（版本库元数据与构建产物：改了也没意义，还会拖慢 + 破坏增量构建）。</summary>
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", "node_modules", "bin", "obj", "packages", "TestResults", "dist", "build"
    };

    /// <summary>单文件上限（超过即跳过：模板里的大文件基本是二进制资源）。</summary>
    private const long MaxFileBytes = 4 * 1024 * 1024;

    /// <summary>
    /// 对目标目录执行替换（内容 → 文件改名 → 目录改名，自底向上避免路径失效）。
    /// </summary>
    /// <param name="root">仓库根目录（已克隆完成）。</param>
    /// <param name="replacements">替换表（已展开令牌、按源串长度降序）。</param>
    /// <returns>替换结果。</returns>
    public static PlaceholderReplaceResult Apply(
        string root,
        IReadOnlyList<KeyValuePair<string, string>> replacements)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || replacements.Count == 0)
            return new PlaceholderReplaceResult(0, 0, 0);

        var filesChanged = 0;
        var filesSkipped = 0;
        var entriesRenamed = 0;

        var files = new List<string>();
        var directories = new List<string>();
        Collect(root, files, directories);

        foreach (var file in files)
        {
            try
            {
                var outcome = ReplaceInFile(file, replacements);
                switch (outcome)
                {
                    case FileOutcome.Changed:
                        filesChanged++;
                        break;
                    case FileOutcome.Skipped:
                        filesSkipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[newproject] 占位符替换跳过文件 {file}", ex);
                filesSkipped++;
            }
        }

        // 文件先改名（仅文件级，不影响遍历结果），目录按深度倒序改名
        foreach (var file in files)
            entriesRenamed += TryRename(file, replacements);

        foreach (var directory in directories.OrderByDescending(path => path.Length))
            entriesRenamed += TryRename(directory, replacements);

        return new PlaceholderReplaceResult(filesChanged, entriesRenamed, filesSkipped);
    }

    /// <summary>递归收集待处理的文件与目录（跳过版本库元数据与构建产物目录）。</summary>
    private static void Collect(string root, List<string> files, List<string> directories)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (SkipDirectories.Contains(name))
                continue;

            directories.Add(directory);
            Collect(directory, files, directories);
        }

        files.AddRange(Directory.EnumerateFiles(root));
    }

    /// <summary>单文件替换结果。</summary>
    private enum FileOutcome
    {
        /// <summary>未命中或无需改写。</summary>
        Unchanged,

        /// <summary>已改写。</summary>
        Changed,

        /// <summary>跳过（二进制 / 过大 / 编码不支持）。</summary>
        Skipped
    }

    /// <summary>
    /// 改写单个文件的内容（UTF-8，保留 BOM；非文本一律跳过）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="replacements">替换表。</param>
    /// <returns>处理结果。</returns>
    private static FileOutcome ReplaceInFile(string path, IReadOnlyList<KeyValuePair<string, string>> replacements)
    {
        if (!IsTextFile(path))
            return FileOutcome.Skipped;

        var info = new FileInfo(path);
        if (info.Length == 0)
            return FileOutcome.Unchanged;
        if (info.Length > MaxFileBytes)
            return FileOutcome.Skipped;

        var bytes = File.ReadAllBytes(path);

        // 编码判定：UTF-16/32 BOM 直接跳过；其余按 UTF-8 处理（无 BOM ≠ 非 UTF-8 在代码模板里风险可忽略）
        if (bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return FileOutcome.Skipped;

        // 二进制探测：前 8KB 出现 NUL（UTF-8 文本不会有）
        var probe = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < probe; i++)
        {
            if (bytes[i] == 0x00)
                return FileOutcome.Skipped;
        }

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));

        var replaced = text;
        foreach (var (from, to) in replacements)
            replaced = replaced.Replace(from, to, StringComparison.Ordinal);

        if (string.Equals(replaced, text, StringComparison.Ordinal))
            return FileOutcome.Unchanged;

        var payload = Encoding.UTF8.GetBytes(replaced);
        if (hasBom)
        {
            var withBom = new byte[payload.Length + 3];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Buffer.BlockCopy(payload, 0, withBom, 3, payload.Length);
            File.WriteAllBytes(path, withBom);
        }
        else
        {
            File.WriteAllBytes(path, payload);
        }

        return FileOutcome.Changed;
    }

    /// <summary>
    /// 重命名文件 / 目录（仅当名字命中替换表时；目标已存在则放弃本次重命名并告警）。
    /// </summary>
    /// <param name="path">原路径。</param>
    /// <param name="replacements">替换表。</param>
    /// <returns>1 表示已重命名，0 表示未命中或放弃。</returns>
    private static int TryRename(string path, IReadOnlyList<KeyValuePair<string, string>> replacements)
    {
        var name = Path.GetFileName(path);
        var renamed = name;
        foreach (var (from, to) in replacements)
            renamed = renamed.Replace(from, to, StringComparison.Ordinal);

        if (string.Equals(renamed, name, StringComparison.Ordinal))
            return 0;

        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
            return 0;

        var target = Path.Combine(parent, renamed);
        if (File.Exists(target) || Directory.Exists(target))
        {
            Logger.Warn($"[newproject] 占位符替换：重命名目标已存在，已放弃 {path} → {target}");
            return 0;
        }

        try
        {
            if (Directory.Exists(path))
                Directory.Move(path, target);
            else
                File.Move(path, target);

            return 1;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[newproject] 占位符替换：重命名失败 {path} → {target}", ex);
            return 0;
        }
    }

    /// <summary>是否为参与内容替换的文本文件（按扩展名 / 白名单文件名判定）。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>是否文本。</returns>
    private static bool IsTextFile(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);

        if (extension.Length == 0)
            return TextFileNames.Contains(name);

        return TextExtensions.Contains(extension) || TextFileNames.Contains(name);
    }
}