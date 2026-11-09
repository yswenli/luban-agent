/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Hosting
*文件名： WorkspaceStartupDefaults
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/6
*描述：首次启动时的工作区根目录建议：不再直接把进程当前目录当作工作区
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 首次启动创建/预填工作区时的根目录建议。
/// </summary>
/// <remarks>
/// 直接取进程当前目录会把工作区锚定到宿主程序自己的输出目录（如 <c>bin\Debug\net10.0</c>），
/// 用户随后让 Agent「分析工作区」时读到的全是构建产物，文件工具因此频繁失败。
/// 这里只在当前目录确实是「用户目录」时才沿用，否则回落到用户目录下的固定工作区目录。
/// </remarks>
public static class WorkspaceStartupDefaults
{
    /// <summary>用户目录下的默认工作区目录名。</summary>
    private const string DefaultFolderName = "LubanAgentWorkspaces";

    /// <summary>
    /// 建议的首次启动工作区根目录（可能尚未存在，创建时自动建目录）。
    /// </summary>
    public static string SuggestRootPath()
    {
        var cwd = TryGetCurrentDirectory();
        if (cwd != null && IsUserFacingDirectory(cwd))
        {
            return cwd;
        }

        var baseDir = TryGetUserBaseDirectory();
        return Path.GetFullPath(Path.Combine(baseDir, DefaultFolderName, "default"));
    }

    /// <summary>
    /// 建议的、已存在的目录（供「必须选择已存在目录」的表单预填）。
    /// </summary>
    public static string SuggestExistingDirectory()
    {
        var suggestion = SuggestRootPath();
        if (Directory.Exists(suggestion))
        {
            return suggestion;
        }

        // 表单要求目录已存在，未创建过的默认工作区目录不预填；退到父目录或用户主目录。
        var parent = Path.GetDirectoryName(Path.GetFullPath(suggestion));
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
        {
            return parent;
        }

        return TryGetUserBaseDirectory();
    }

    /// <summary>
    /// 判断目录是否适合作为用户工作区：排除宿主程序目录、系统目录与盘符根。
    /// </summary>
    /// <param name="directory">待判断目录（绝对路径）。</param>
    public static bool IsUserFacingDirectory(string directory)
    {
        string full;
        try
        {
            full = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (full == Path.GetPathRoot(full)) return false;

        var appBase = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appBase)
            && IsUnder(full, Path.GetFullPath(appBase)))
        {
            return false;
        }

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(systemRoot) && IsUnder(full, systemRoot))
        {
            return false;
        }

        foreach (var special in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                 })
        {
            var path = Environment.GetFolderPath(special);
            if (!string.IsNullOrEmpty(path) && IsUnder(full, path))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>判断 <paramref name="path"/> 是否等于或位于 <paramref name="root"/> 之下。</summary>
    private static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetCurrentDirectory()
    {
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            return string.IsNullOrWhiteSpace(cwd) ? null : cwd;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>取用户主目录；不可用时退到本地应用数据目录，再退到临时目录。</summary>
    private static string TryGetUserBaseDirectory()
    {
        foreach (var special in new[]
                 {
                     Environment.SpecialFolder.UserProfile,
                     Environment.SpecialFolder.LocalApplicationData,
                 })
        {
            var path = Environment.GetFolderPath(special);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return Path.GetTempPath();
    }
}