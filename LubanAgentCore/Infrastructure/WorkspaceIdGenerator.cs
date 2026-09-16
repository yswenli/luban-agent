/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Infrastructure
*文件名： WorkspaceIdGenerator
*版本号： V1.0.0.0
*唯一标识：工作区ID派生器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/16
*描述：由工作区根目录派生稳定工作区ID（同一物理目录在任意宿主/数据库中恒得同一 ID）
*
*****************************************************************************/
using System.Security.Cryptography;
using System.Text;

namespace LubanAgentCore.Infrastructure;

/// <summary>
/// 工作区ID派生器。
/// <para>
/// 工作区ID 由根目录路径派生而非随机生成，保证：
/// 同一物理目录在 CLI / GUI 等不同宿主、不同工作区数据库中得到同一 ID，
/// 从而让按 WorkspaceId 隔离的长期记忆可跨宿主可见。
/// </para>
/// <para>
/// 归一化规则：绝对路径 → 解析符号链接/junction 到真实目标 → 去除末尾分隔符 →
/// Windows 下统一大写（与文件系统大小写不敏感语义一致）。
/// </para>
/// </summary>
public static class WorkspaceIdGenerator
{
    /// <summary>
    /// 派生ID 的十六进制长度（128 位，与 <see cref="Guid.NewGuid"/> 的 "N" 格式同宽）。
    /// </summary>
    private const int IdLength = 32;

    /// <summary>
    /// 归一化工作区根目录路径。
    /// </summary>
    /// <param name="rootPath">原始根目录路径。</param>
    /// <returns>归一化后的绝对路径（不含末尾分隔符）。</returns>
    public static string Normalize(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return string.Empty;

        var full = Path.GetFullPath(rootPath.Trim());
        full = ResolveLinkTarget(full) ?? full;
        // 盘根（如 C:\）去尾分隔符会退化成 C:（相对盘符语义），必须原样保留
        if (!string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.Length == 0)
            return string.Empty;

        // Windows 文件系统大小写不敏感，统一大写避免同一目录因大小写差异派生出不同ID
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    /// <summary>
    /// 由工作区根目录派生稳定的工作区ID。
    /// </summary>
    /// <param name="rootPath">工作区根目录路径。</param>
    /// <returns>32 位十六进制工作区ID；路径为空时返回空串。</returns>
    public static string Compute(string rootPath)
    {
        var normalized = Normalize(rootPath);
        if (normalized.Length == 0)
            return string.Empty;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..IdLength].ToLowerInvariant();
    }

    /// <summary>
    /// 由根目录推导默认显示名（保留原始大小写，避免归一化大写后界面显示全大写目录名）。
    /// </summary>
    /// <param name="rootPath">原始根目录路径。</param>
    /// <returns>目录名；无法推导时返回空串。</returns>
    public static string SuggestName(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return string.Empty;

        try
        {
            var full = Path.GetFullPath(rootPath.Trim());
            // 盘根（如 C:\）不能被 TrimEnd 成 C:，否则 Path.GetFileName 会返回空串
            if (!string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(full);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 若路径存在且为符号链接/junction，返回其最终真实目标路径；否则返回 null。
    /// </summary>
    /// <param name="fullPath">已转为绝对路径的路径。</param>
    /// <returns>真实目标路径或 null。</returns>
    private static string? ResolveLinkTarget(string fullPath)
    {
        try
        {
            FileSystemInfo? info = File.Exists(fullPath)
                ? new FileInfo(fullPath)
                : Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : null;
            if (info?.LinkTarget == null) return null;
            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            return resolved?.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }
}
