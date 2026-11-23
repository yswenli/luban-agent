/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebFilePath
*描述：会话文本里的相对文件路径解析为工作区内的绝对路径（纯逻辑，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 相对文件路径 → 工作区内绝对路径。
/// </summary>
/// <remarks>
/// <para>
/// 模型输出的链接大多是相对路径（<c>overview.md</c>、<c>entities/X.md</c>），浏览器按当前页面地址解析，
/// 落到站点根下的静态文件必然 404。这里把它解析到工作区根目录，再由
/// <see cref="WebFileService"/> 以纯文本回传。
/// </para>
/// <para>
/// LLM Wiki 的页面链接相对 <c>wiki/</c> 子目录（页面物理位置是 <c>&lt;根&gt;/wiki/overview.md</c>），
/// 所以根目录找不到时再回落一次 <c>wiki/</c>；两种链接形态都能命中。任何结果都必须落在工作区内。
/// </para>
/// </remarks>
public static class WebFilePath
{
    /// <summary>LLM Wiki 页面所在子目录（相对工作区根）。</summary>
    private const string WikiDirectory = "wiki";

    /// <summary>
    /// 把相对路径解析为工作区内已存在的文件。
    /// </summary>
    /// <param name="rootPath">工作区根目录。</param>
    /// <param name="relativePath">链接里的相对路径（可带 <c>./</c> 前缀、查询串或锚点）。</param>
    /// <param name="fullPath">命中时的绝对路径；失败为空串。</param>
    /// <returns>命中工作区内真实文件时返回 true。</returns>
    public static bool TryResolve(string? rootPath, string? relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        var root = NormalizeRoot(rootPath);
        if (root is null) return false;

        var relative = NormalizeRelative(relativePath);
        if (relative is null) return false;

        // 工作区根本身可能就是符号链接，比较基准取它的真实路径
        var realRoot = ResolveRealPath(root);
        if (realRoot is null) return false;

        foreach (var candidate in Candidates(relative))
        {
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, candidate));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!IsWithin(root, full)) continue;

            // 工作区内可能有 junction/符号链接目录，字符串前缀拦不住这种逃逸：
            // 逐段解析真实路径后再判一次，并且只回传真实路径（读取时不再穿过链接）
            var real = ResolveRealPath(full);
            if (real is null || !IsWithin(realRoot, real) || !File.Exists(real)) continue;

            fullPath = real;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 目标路径是否落在工作区根之下。
    /// </summary>
    /// <param name="rootPath">工作区根目录。</param>
    /// <param name="fullPath">待判定的绝对路径。</param>
    /// <returns>落在根之下返回 true；根无效或路径越界返回 false。</returns>
    public static bool IsWithin(string? rootPath, string? fullPath)
    {
        var root = NormalizeRoot(rootPath);
        if (root is null || string.IsNullOrWhiteSpace(fullPath)) return false;

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// 把工作区内的绝对路径换算为可交给 <c>/file/read</c> 的相对路径。
    /// </summary>
    /// <remarks>
    /// 只做换算，不检查文件是否存在：附件刚落盘时可能还没写完，真正的安全闸门在
    /// <see cref="TryResolve"/>（读取时会再解析符号链接判一次越界）。越界或含盘符 / 数据流成分时返回 false。
    /// </remarks>
    /// <param name="rootPath">工作区根目录。</param>
    /// <param name="fullPath">待换算的绝对路径。</param>
    /// <param name="relative">换算出的相对路径（分隔符统一为 <c>/</c>）；失败为空串。</param>
    /// <returns>落在工作区内且可安全拼接时返回 true。</returns>
    public static bool TryGetRelative(string? rootPath, string? fullPath, out string relative)
    {
        relative = string.Empty;
        var root = NormalizeRoot(rootPath);
        if (root is null || string.IsNullOrWhiteSpace(fullPath)) return false;

        string absolute;
        try
        {
            absolute = Path.GetFullPath(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!IsWithin(root, absolute)) return false;

        var value = Path.GetRelativePath(root, absolute).Replace('\\', '/').Trim();
        if (value.Length == 0 || value == "." || value.StartsWith("../", StringComparison.Ordinal))
        {
            return false;
        }

        // 与 TryResolve 同口径：盘符相对路径与备用数据流一律拒绝
        if (!IsSafeRelative(value)) return false;

        relative = value;
        return true;
    }

    /// <summary>
    /// 把相对路径解析为工作区内已存在的目录（相对路径为空时解析为工作区根）。
    /// </summary>
    /// <param name="rootPath">工作区根目录。</param>
    /// <param name="relativePath">相对路径；空白表示工作区根。</param>
    /// <param name="fullPath">命中时的真实绝对路径；失败为空串。</param>
    /// <returns>命中工作区内真实目录时返回 true。</returns>
    public static bool TryResolveDirectory(string? rootPath, string? relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        var root = NormalizeRoot(rootPath);
        if (root is null) return false;

        var realRoot = ResolveRealPath(root);
        if (realRoot is null) return false;

        string candidate;
        try
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                candidate = root;
            }
            else
            {
                var relative = NormalizeRelative(relativePath);
                if (relative is null) return false;
                candidate = Path.GetFullPath(Path.Combine(root, relative));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!IsWithin(root, candidate)) return false;

        // 与文件读取同口径：目录本身可能是 junction/符号链接，必须按真实路径再判一次越界
        var real = ResolveRealPath(candidate);
        if (real is null || !IsWithin(realRoot, real) || !Directory.Exists(real)) return false;

        fullPath = real;
        return true;
    }

    /// <summary>
    /// 把绝对路径逐段解析为真实路径：任一分量是符号链接或 junction 时，替换为其最终目标。
    /// </summary>
    /// <remarks>
    /// <see cref="IsWithin"/> 只做字符串前缀比较，<see cref="Path.GetFullPath(string)"/> 也不会展开 reparse point，
    /// 因此工作区内的链接目录（如 <c>leak -> C:\Users</c>）能把读取带出根目录。这里按「根 → 逐段」解析，
    /// 让越界判定看到的是真实落点。
    /// </remarks>
    /// <param name="fullPath">绝对路径。</param>
    /// <returns>真实路径；路径不存在、无权限或链接断裂导致无法判定时返回 null。</returns>
    private static string? ResolveRealPath(string fullPath)
    {
        try
        {
            var current = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(current)) return null;

            var segments = fullPath[current.Length..]
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < segments.Length; i++)
            {
                var next = Path.Combine(current, segments[i]);
                FileSystemInfo info = i == segments.Length - 1
                    ? new FileInfo(next)
                    : new DirectoryInfo(next);

                // Exists 为假时不去查链接目标：不存在或链接断裂的片段不构成逃逸
                if (info.Exists && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    next = target.FullName;
                }

                current = next;
            }

            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>归一化工作区根目录：去掉首尾引号与结尾分隔符。</summary>
    /// <param name="rootPath">原始根路径。</param>
    /// <returns>绝对路径；无效时返回 null。</returns>
    private static string? NormalizeRoot(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return null;

        try
        {
            var root = Path.GetFullPath(rootPath.Trim().Trim('"', '\''));
            return root.Length > RootLength(root) ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : root;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>取盘符/根标记长度，避免把 <c>C:\</c> 修剪成 <c>C:</c>。</summary>
    /// <param name="root">绝对根路径。</param>
    /// <returns>必须保留的字符数。</returns>
    private static int RootLength(string root) => Path.GetPathRoot(root)?.Length ?? 0;

    /// <summary>
    /// 清掉相对路径里的非文件名成分（引号、<c>./</c> 前缀、查询串、锚点），并拒绝绝对路径。
    /// </summary>
    /// <param name="relativePath">链接里的原始相对路径。</param>
    /// <returns>可拼到根目录的相对路径；不安全或为空时返回 null。</returns>
    private static string? NormalizeRelative(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var text = relativePath.Trim().Trim('"', '\'');
        if (text.Length == 0) return null;

        // 锚点与查询串不属于文件名；表格/正文里偶见 <a.md#片段>
        var cut = text.IndexOfAny(['?', '#']);
        if (cut >= 0) text = text[..cut];

        while (text.StartsWith("./", StringComparison.Ordinal) || text.StartsWith(".\\", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        text = text.Trim();
        return IsSafeRelative(text) ? text : null;
    }

    /// <summary>
    /// 相对路径是否不含绝对路径、盘符与数据流成分。
    /// </summary>
    /// <param name="text">待判定的相对路径。</param>
    /// <returns>可安全拼到根目录时返回 true。</returns>
    private static bool IsSafeRelative(string text)
    {
        if (text.Length == 0 || text.Contains('\0')) return false;

        // 绝对路径（含 UNC）不属于会话内的相对文件链接
        if (Path.IsPathRooted(text)) return false;

        // 盘符相对路径（C:foo）与备用数据流（file.md:stream）一律拒绝
        return !text.Contains(':');
    }

    /// <summary>候选相对路径：原样 → 解码百分号转义 → 再各补一个 <c>wiki/</c> 前缀。</summary>
    /// <param name="relative">已归一化的相对路径。</param>
    /// <returns>按优先级排列的候选路径。</returns>
    private static IEnumerable<string> Candidates(string relative)
    {
        foreach (var variant in Variants(relative))
        {
            yield return variant;

            // 链接本身已带 wiki/ 前缀时不再重复拼接
            if (variant.StartsWith(WikiDirectory + "/", StringComparison.OrdinalIgnoreCase)
                || variant.StartsWith(WikiDirectory + "\\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return Path.Combine(WikiDirectory, variant);
        }
    }

    /// <summary>原样与百分号解码两种写法（模型有时输出 <c>%20</c> 而不是空格）。</summary>
    /// <param name="relative">已归一化的相对路径。</param>
    /// <returns>去重后的候选写法。</returns>
    private static IEnumerable<string> Variants(string relative)
    {
        yield return relative;

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(relative);
        }
        catch (UriFormatException)
        {
            yield break;
        }

        if (!string.Equals(decoded, relative, StringComparison.Ordinal))
        {
            // 解码可能引入新的危险成分：%3A 变成 NTFS 数据流、%43%3A 变成盘符路径，
            // 因此解码结果要按同一口径复核，不能绕过 NormalizeRelative 的校验
            if (IsSafeRelative(decoded)) yield return decoded;
        }
    }
}