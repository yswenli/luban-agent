/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services
*文件名： WorkspaceSubPath
*描述：把「根目录 + 相对子目录」收敛成安全的目标目录
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 工作区子目录校验：只允许根目录之下的相对路径（可多级），拒绝上跳、绝对路径与符号链接逃逸。
/// </summary>
/// <remarks>
/// 纯函数，不落盘、不依赖宿主。调用方仍应在写入磁盘前用
/// <see cref="WorkspaceRootsStore.IsAllowed"/> 复核白名单，此处只保证「不越过给定根目录」。
/// </remarks>
public static class WorkspaceSubPath
{
    /// <summary>Windows 保留设备名：同名目录无法创建，提前给出可读错误。</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 在根目录下解析相对子目录。
    /// </summary>
    /// <param name="root">根目录（绝对路径）。</param>
    /// <param name="subPath">
    /// 相对子目录；允许 <c>/</c> 与 <c>\</c> 分隔的多级路径。空值表示根目录本身
    /// （兼容未提交 <c>subPath</c> 的旧客户端）。
    /// </param>
    /// <param name="target">解析后的绝对路径。</param>
    /// <param name="error">失败原因；成功时为空串。</param>
    /// <returns>成功为 <c>true</c>。</returns>
    public static bool TryResolve(string root, string? subPath, out string target, out string error)
    {
        target = string.Empty;
        error = string.Empty;

        var rootText = (root ?? string.Empty).Trim();
        if (rootText.Length == 0)
        {
            error = "根目录不能为空";
            return false;
        }

        if (!Path.IsPathFullyQualified(rootText))
        {
            error = "根目录必须是绝对路径";
            return false;
        }

        if (File.Exists(rootText))
        {
            error = "根目录指向的是文件";
            return false;
        }

        var segments = new List<string>();
        var sub = (subPath ?? string.Empty).Trim();
        if (sub.Length > 0)
        {
            // 绝对路径（/x、C:\x、\\server\share）一律拒绝：子目录只能是相对路径
            if (Path.IsPathRooted(sub) || sub.StartsWith('/') || sub.StartsWith('\\'))
            {
                error = "子目录必须是相对于根目录的相对路径";
                return false;
            }

            foreach (var rawSegment in sub.Split(['/', '\\'], StringSplitOptions.None))
            {
                var segment = rawSegment;
                if (segment.Length == 0)
                {
                    error = "子目录不能包含空路径段";
                    return false;
                }

                if (segment is "." or "..")
                {
                    error = "子目录不能包含 . 或 ..";
                    return false;
                }

                if (segment.Trim().Length == 0 || segment != segment.Trim())
                {
                    error = "子目录段不能以空格开头或结尾";
                    return false;
                }

                if (segment.EndsWith('.') || segment.EndsWith(' '))
                {
                    error = "子目录段不能以点或空格结尾";
                    return false;
                }

                if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0
                    || segment.Any(char.IsControl))
                {
                    error = $"子目录包含非法字符：{segment}";
                    return false;
                }

                var stem = segment.Split('.')[0];
                if (ReservedNames.Contains(stem))
                {
                    error = $"子目录名是系统保留名：{segment}";
                    return false;
                }

                segments.Add(segment);
            }
        }

        string fullRoot;
        string fullTarget;
        try
        {
            fullRoot = Path.GetFullPath(rootText);
            fullTarget = fullRoot;
            foreach (var segment in segments)
            {
                fullTarget = Path.Combine(fullTarget, segment);
            }

            fullTarget = Path.GetFullPath(fullTarget);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "路径不合法";
            return false;
        }

        // 逃逸校验：先解析符号链接/junction 的物理路径，再判断是否仍在根目录之下。
        // 逐段解析才能拦住 root/link/sub（link 位于中间层）这类只在末段看不出来的逃逸。
        var physicalRoot = ResolvePhysical(fullRoot);
        var physicalTarget = ResolvePhysical(fullTarget);
        if (!WorkspaceRootsStore.IsWithin(physicalTarget, physicalRoot))
        {
            error = "子目录越出了根目录";
            return false;
        }

        target = fullTarget;
        return true;
    }

    /// <summary>
    /// 解析绝对路径的物理路径（逐段解析符号链接 / junction）。
    /// </summary>
    /// <remarks>
    /// 供白名单复核使用：<see cref="WorkspaceRootsStore.IsAllowed"/> 只解析末段链接，
    /// 中间段的链接需要这里逐段展开，否则「已存在链接 + 不存在的尾段」会绕过白名单。
    /// </remarks>
    /// <param name="fullPath">绝对路径。</param>
    /// <returns>物理路径；解析失败时返回原路径。</returns>
    public static string ResolvePhysicalPath(string fullPath) => ResolvePhysical(fullPath);

    /// <summary>
    /// 逐段解析路径中已存在部分的符号链接 / junction，返回物理路径。
    /// </summary>
    /// <remarks>
    /// .NET 的 <see cref="FileSystemInfo.ResolveLinkTarget"/> 只解析节点自身，
    /// 因此必须逐段走；不存在的尾段原样拼接（克隆/建目录时它们还不存在）。
    /// </remarks>
    /// <param name="fullPath">绝对路径。</param>
    /// <returns>物理路径；解析失败时退化为原路径。</returns>
    private static string ResolvePhysical(string fullPath)
    {
        try
        {
            var pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (pathRoot.Length == 0)
            {
                return Path.GetFullPath(fullPath);
            }

            var rest = fullPath[pathRoot.Length..];
            var segments = rest.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            var current = pathRoot;
            for (var index = 0; index < segments.Length; index++)
            {
                current = Path.Combine(current, segments[index]);

                var exists = Directory.Exists(current) || File.Exists(current);
                if (!exists)
                {
                    // 该段尚未创建：其余段也不可能存在，直接拼回去
                    for (var tail = index + 1; tail < segments.Length; tail++)
                    {
                        current = Path.Combine(current, segments[tail]);
                    }

                    return Path.GetFullPath(current);
                }

                try
                {
                    FileSystemInfo info = Directory.Exists(current)
                        ? new DirectoryInfo(current)
                        : new FileInfo(current);
                    var resolved = info.LinkTarget is null
                        ? null
                        : info.ResolveLinkTarget(returnFinalTarget: true);
                    if (resolved is not null)
                    {
                        current = resolved.FullName;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 单个节点解析失败按普通目录继续，白名单仍会在外层复核
                }
            }

            return Path.GetFullPath(current);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return fullPath;
        }
    }
}