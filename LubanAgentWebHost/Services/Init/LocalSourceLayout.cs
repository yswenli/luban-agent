/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： LocalSourceLayout
*描述：云效 S2e §14 修复三——本地源码检出形态判定纯函数（零副作用，只读目录元数据）：
*      原形态 {root}/{repoName} 子目录 + 形态 A 工作区根本身即检出 + 形态 B 根含 .git（综合库再验特征子目录）。
*      命中任一 → 已有源码 → 预检/执行零 git 步骤、不再误报「本地未克隆」。
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §14 第 3 条
*
*****************************************************************************/

namespace LubanAgentWebHost.Services.Init;

/// <summary>本地源码检出形态（S2e §14 修复三）。</summary>
public enum LocalSourceKind
{
    /// <summary>无源码（需 git 检出）。</summary>
    None = 0,

    /// <summary>原形态：<c>{workspaceRoot}/{repoName}</c> 子目录存在且非空。</summary>
    SubDir = 1,

    /// <summary>形态 A：工作区根本身即该库检出（根目录名与库名规范化相等）。</summary>
    RootCheckout = 2,

    /// <summary>
    /// 形态 B：根下含 <c>.git</c>——综合库再验 <c>{前缀}-Admin/-Mobile/-Service</c> 中 ≥2 个存在；
    /// 单端库退化为「根含 <c>.git</c> 即认」。
    /// </summary>
    RootGit = 3,
}

/// <summary>本地检出探测结果。</summary>
/// <param name="HasSource">是否已有源码检出（命中任一形态）。</param>
/// <param name="Kind">命中形态（未命中为 <see cref="LocalSourceKind.None"/>）。</param>
/// <param name="SourceDir">源码所在目录（命中时非空；未命中为 null）。</param>
public sealed record LocalSourceProbe(bool HasSource, LocalSourceKind Kind, string? SourceDir)
{
    /// <summary>未命中常量。</summary>
    public static readonly LocalSourceProbe None = new(false, LocalSourceKind.None, null);
}

/// <summary>
/// 本地源码检出形态判定（纯函数：只读目录存在性，不创建、不删除、不写盘）。
/// </summary>
/// <remarks>
/// 三形态任一命中即「已有源码」（S2c §12.1.2 幂等口径 → 零 git 步骤）：
/// <list type="number">
/// <item>原形态：<c>{workspaceRoot}/{repoName}</c> 子目录存在且非空；</item>
/// <item>形态 A：根目录名与库名规范化相等（忽略大小写 / 尾分隔符）且根非空——
/// 工作区根本身就是该库检出；</item>
/// <item>形态 B：根下含 <c>.git</c>；综合库（<c>-Mixed</c> 后缀）再验
/// <c>{代号前缀}-Admin/-Mobile/-Service</c> 中 ≥2 个存在，单端库退化为「根含 <c>.git</c> 即认」。</item>
/// </list>
/// 空目录一律按「无源码」处理（可安全 clone 落位，与原口径一致）。
/// </remarks>
public static class LocalSourceLayout
{
    /// <summary>综合库模板名后缀（与 <see cref="NamingDeriver"/> 同口径）。</summary>
    private const string MixedSuffix = "-Mixed";

    /// <summary>git 元数据目录名。</summary>
    private const string GitDirName = ".git";

    /// <summary>综合库特征子目录后缀（<c>{前缀}-Admin/-Mobile/-Service</c>）。</summary>
    private static readonly string[] CompositeDirSuffixes = ["-Admin", "-Mobile", "-Service"];

    /// <summary>
    /// 探测本地源码检出形态（S2e §14）。
    /// </summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="repoName">目标库名。</param>
    /// <returns>探测结果（形态 + 源码目录）；参数空白或均未命中为 <see cref="LocalSourceProbe.None"/>。</returns>
    public static LocalSourceProbe Detect(string? workspaceRoot, string? repoName)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(repoName))
        {
            return LocalSourceProbe.None;
        }

        var root = workspaceRoot!.Trim();
        var name = repoName!.Trim().TrimEnd('/', '\\');

        try
        {
            // 原形态：{root}/{repoName} 子目录存在且非空
            var subDir = Path.Combine(root, name);
            if (Directory.Exists(subDir) && Directory.EnumerateFileSystemEntries(subDir).Any())
            {
                return new LocalSourceProbe(true, LocalSourceKind.SubDir, subDir);
            }

            // 形态 A：根目录名 ≈ 库名（忽略大小写/尾分隔符）且根非空 → 根本身即检出
            if (Directory.Exists(root)
                && IsRootNamedAsRepo(root, name)
                && Directory.EnumerateFileSystemEntries(root).Any())
            {
                return new LocalSourceProbe(true, LocalSourceKind.RootCheckout, root);
            }

            // 形态 B：根下含 .git（综合库再验特征子目录；单端库退化为根含 .git 即认）
            if (Directory.Exists(root) && Directory.Exists(Path.Combine(root, GitDirName)))
            {
                if (IsCompositeRepo(name))
                {
                    var prefix = ParseNamePrefix(name);
                    if (prefix is not null)
                    {
                        var hits = CompositeDirSuffixes.Count(suffix =>
                            Directory.Exists(Path.Combine(root, prefix + suffix)));
                        if (hits >= 2)
                        {
                            return new LocalSourceProbe(true, LocalSourceKind.RootGit, root);
                        }
                    }
                }
                else
                {
                    return new LocalSourceProbe(true, LocalSourceKind.RootGit, root);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // 目录不可读按无源码处理（可安全走检出计划）
            return LocalSourceProbe.None;
        }

        return LocalSourceProbe.None;
    }

    /// <summary>是否已有本地源码检出（<see cref="Detect"/> 的布尔投影）。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="repoName">目标库名。</param>
    /// <returns>命中任一形态为 <c>true</c>。</returns>
    public static bool HasSource(string? workspaceRoot, string? repoName)
        => Detect(workspaceRoot, repoName).HasSource;

    /// <summary>
    /// 根目录名与库名规范化相等（忽略大小写 / 尾分隔符；纯名字比较，不做完整路径解析）。
    /// </summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="repoName">目标库名。</param>
    /// <returns>叶子名等值（OrdinalIgnoreCase）为 <c>true</c>。</returns>
    public static bool IsRootNamedAsRepo(string? workspaceRoot, string? repoName)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(repoName))
        {
            return false;
        }

        var rootName = Path.GetFileName(workspaceRoot!.Trim().TrimEnd('/', '\\'));
        var name = repoName!.Trim().TrimEnd('/', '\\');
        return rootName.Length > 0
            && string.Equals(rootName, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>是否综合库形态（<c>-Mixed</c> 后缀，忽略大小写）。</summary>
    private static bool IsCompositeRepo(string repoName)
        => repoName.EndsWith(MixedSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>库名首个 <c>-</c> 之前的代号段（无 <c>-</c> 或段空为 null）。</summary>
    private static string? ParseNamePrefix(string repoName)
    {
        var index = repoName.IndexOf('-');
        return index <= 0 ? null : repoName[..index];
    }
}
