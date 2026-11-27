/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WorkspaceCacheScanner
*描述：工作区临时目录的扫描与回收（纯逻辑，不依赖宿主状态，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 工作区缓存扫描结果。
/// </summary>
/// <param name="TempBytes">非上传类临时文件字节数。</param>
/// <param name="TempFiles">非上传类临时文件数量。</param>
/// <param name="UploadBytes">上传附件总字节数。</param>
/// <param name="UploadFiles">上传附件总数量。</param>
/// <param name="ReferencedBytes">被历史消息引用的附件字节数。</param>
/// <param name="ReferencedFiles">被历史消息引用的附件数量。</param>
/// <param name="ReclaimableBytes">按保留期可回收字节数（临时文件 + 未被引用的附件）。</param>
/// <param name="ReclaimableFiles">按保留期可回收文件数。</param>
public sealed record WorkspaceCacheScanResult(
    long TempBytes,
    int TempFiles,
    long UploadBytes,
    int UploadFiles,
    long ReferencedBytes,
    int ReferencedFiles,
    long ReclaimableBytes,
    int ReclaimableFiles);

/// <summary>
/// 工作区缓存清理结果。
/// </summary>
/// <param name="FreedBytes">已释放字节数。</param>
/// <param name="FreedFiles">已删除文件数。</param>
/// <param name="FailedFiles">删除失败的文件数（被占用 / 无权限）。</param>
public sealed record WorkspaceCacheCleanResult(long FreedBytes, int FreedFiles, int FailedFiles);

/// <summary>
/// 工作区临时目录扫描与回收。
/// </summary>
/// <remarks>
/// <para>
/// 只处理 <c>{工作区}/.luban-agent/temp</c> 下的文件：<c>uploads/</c> 分支视为上传附件，
/// 其余视为可再生的临时中间文件。附件是否仍被历史消息引用由调用方传入的路径集合判定。
/// </para>
/// <para>
/// 扫描与清理都用调用方传入的保留期；不跟随符号链接（重解析点直接跳过），
/// 单个文件失败只计数、不中断其余删除。
/// </para>
/// </remarks>
public static class WorkspaceCacheScanner
{
    /// <summary>上传附件所在子目录名（相对 temp 根）。</summary>
    public const string UploadsDirectoryName = "uploads";

    /// <summary>扫描临时目录。</summary>
    /// <param name="tempRoot">临时目录绝对路径。</param>
    /// <param name="keepDays">保留最近 N 天修改的文件；0 表示不保留。</param>
    /// <param name="referencedPaths">仍被历史消息引用的文件绝对路径集合。</param>
    /// <returns>分类统计。</returns>
    public static WorkspaceCacheScanResult Scan(
        string tempRoot,
        int keepDays,
        IReadOnlyCollection<string> referencedPaths)
    {
        if (string.IsNullOrWhiteSpace(tempRoot) || !Directory.Exists(tempRoot))
        {
            return new WorkspaceCacheScanResult(0, 0, 0, 0, 0, 0, 0, 0);
        }

        var cutoff = Cutoff(keepDays);
        var referenced = Normalize(referencedPaths);

        long tempBytes = 0, uploadBytes = 0, referencedBytes = 0, reclaimableBytes = 0;
        int tempFiles = 0, uploadFiles = 0, referencedFiles = 0, reclaimableFiles = 0;

        foreach (var path in EnumerateFiles(tempRoot))
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists) continue;
                _ = info.Length;
            }
            catch
            {
                // 快照期间被删除或不可读：忽略，不计入统计
                continue;
            }

            var isUpload = IsUnderUploads(tempRoot, path);
            var expired = cutoff is null || info.LastWriteTimeUtc <= cutoff.Value;

            if (isUpload)
            {
                uploadBytes += info.Length;
                uploadFiles++;
                if (referenced.Contains(info.FullName))
                {
                    referencedBytes += info.Length;
                    referencedFiles++;
                }
                else if (expired)
                {
                    reclaimableBytes += info.Length;
                    reclaimableFiles++;
                }
            }
            else
            {
                tempBytes += info.Length;
                tempFiles++;
                if (expired)
                {
                    reclaimableBytes += info.Length;
                    reclaimableFiles++;
                }
            }
        }

        return new WorkspaceCacheScanResult(
            tempBytes,
            tempFiles,
            uploadBytes,
            uploadFiles,
            referencedBytes,
            referencedFiles,
            reclaimableBytes,
            reclaimableFiles);
    }

    /// <summary>清理临时目录。</summary>
    /// <param name="tempRoot">临时目录绝对路径。</param>
    /// <param name="keepDays">保留最近 N 天修改的文件；0 表示不保留。</param>
    /// <param name="referencedPaths">仍被历史消息引用的文件绝对路径集合。</param>
    /// <param name="includeTempFiles">是否删除非上传类临时文件。</param>
    /// <param name="includeOrphanUploads">是否删除未被引用的上传附件。</param>
    /// <param name="includeReferencedUploads">是否删除已被引用的上传附件。</param>
    /// <returns>清理结果。</returns>
    public static WorkspaceCacheCleanResult Clean(
        string tempRoot,
        int keepDays,
        IReadOnlyCollection<string> referencedPaths,
        bool includeTempFiles,
        bool includeOrphanUploads,
        bool includeReferencedUploads)
    {
        if (string.IsNullOrWhiteSpace(tempRoot) || !Directory.Exists(tempRoot))
        {
            return new WorkspaceCacheCleanResult(0, 0, 0);
        }

        var root = Path.GetFullPath(tempRoot);
        var cutoff = Cutoff(keepDays);
        var referenced = Normalize(referencedPaths);

        long freedBytes = 0;
        int freedFiles = 0, failedFiles = 0;

        foreach (var path in EnumerateFiles(root))
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists) continue;
                _ = info.Length;
            }
            catch
            {
                failedFiles++;
                continue;
            }

            var isUpload = IsUnderUploads(root, path);
            var isReferenced = isUpload && referenced.Contains(info.FullName);
            var allowed = isUpload
                ? (isReferenced ? includeReferencedUploads : includeOrphanUploads)
                : includeTempFiles;

            if (!allowed) continue;
            if (cutoff is not null && info.LastWriteTimeUtc > cutoff.Value) continue;
            if (!IsInside(root, info.FullName)) continue;

            try
            {
                // 只读文件（如从版本库带过来的附件副本）先去掉只读属性再删
                if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    info.Attributes &= ~FileAttributes.ReadOnly;
                }

                var length = info.Length;
                info.Delete();
                freedBytes += length;
                freedFiles++;
            }
            catch
            {
                failedFiles++;
            }
        }

        PruneEmptyDirectories(root);
        return new WorkspaceCacheCleanResult(freedBytes, freedFiles, failedFiles);
    }

    /// <summary>保留期截止时刻；<paramref name="keepDays"/> 为 0 时返回 null（不设下限）。</summary>
    private static DateTime? Cutoff(int keepDays)
        => keepDays > 0 ? DateTime.UtcNow.AddDays(-keepDays) : null;

    /// <summary>把引用路径归一化为绝对路径集合（按平台决定大小写敏感性）。</summary>
    private static HashSet<string> Normalize(IReadOnlyCollection<string>? paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var set = new HashSet<string>(comparer);
        if (paths is null) return set;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                set.Add(Path.GetFullPath(path));
            }
            catch
            {
                // 非法路径（历史记录里可能有空串或占位符）：无法参与匹配，跳过
            }
        }

        return set;
    }

    /// <summary>递归枚举文件；跳过重解析点目录，避免跟随符号链接跑到目录外。</summary>
    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] files;
            string[] dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var sub in dirs)
            {
                try
                {
                    if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch
                {
                    continue;
                }

                pending.Push(sub);
            }
        }
    }

    /// <summary>路径是否位于指定根目录之下（含根自身）。</summary>
    private static bool IsInside(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison)
            || string.Equals(normalizedPath, normalizedRoot, comparison);
    }

    /// <summary>文件是否位于 <c>uploads/</c> 分支。</summary>
    private static bool IsUnderUploads(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
        return string.Equals(first, UploadsDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>自底向上删除空目录，保留 temp 根本身。</summary>
    private static void PruneEmptyDirectories(string root)
    {
        // 与文件枚举保持同一口径：只收集真实目录，跳过重解析点，
        // 避免 <c>SearchOption.AllDirectories</c> 顺着符号链接/junction 跑到目录外。
        var dirs = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] subs;
            try
            {
                subs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subs)
            {
                try
                {
                    if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch
                {
                    continue;
                }

                dirs.Add(sub);
                pending.Push(sub);
            }
        }

        // 先深后浅：子目录删空后父目录才可能变空
        var depth = Path.TrimEndingDirectorySeparator(root)
            .Count(ch => ch == Path.DirectorySeparatorChar);
        foreach (var dir in dirs.OrderByDescending(GetDepth))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch
            {
                // 目录被占用或已删除：保留即可
            }
        }

        int GetDepth(string path) => path.Count(ch => ch == Path.DirectorySeparatorChar) - depth;
    }
}