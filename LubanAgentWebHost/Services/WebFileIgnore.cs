/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebFileIgnore
*描述：工作区目录列举时默认跳过的构建产物与版本库目录（纯逻辑，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 目录列举的默认忽略规则。
/// </summary>
/// <remarks>
/// <para>
/// 工作区根目录直接接面板时，<c>.git</c>、<c>node_modules</c>、<c>bin</c> 这些目录动辄几万条，
/// 前端树根本展不开，也没有查看价值。这里只按「目录名」过滤，不解析 <c>.gitignore</c>：
/// 规则简单可预期，用户点「显示忽略项」即可完整查看。
/// </para>
/// <para>
/// 点开头的普通文件（<c>.gitignore</c>、<c>.env</c> 等）**不**忽略：它们常是要看的配置。
/// </para>
/// </remarks>
public static class WebFileIgnore
{
    /// <summary>默认忽略的目录名（不区分大小写）。</summary>
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bower_components", "jspm_packages",
        "bin", "obj", "dist", "build", "out", "target", "packages",
        "venv", "__pycache__", ".pytest_cache", "coverage", "TestResults",
        "artifacts", ".vs", ".idea", ".vscode",
    };

    /// <summary>
    /// 该目录项是否按默认规则跳过。
    /// </summary>
    /// <param name="name">条目名（不含路径）。</param>
    /// <param name="isDirectory">是否是目录；文件一律不跳过。</param>
    /// <returns>需要跳过时返回 true。</returns>
    public static bool IsIgnored(string? name, bool isDirectory)
    {
        if (!isDirectory || string.IsNullOrWhiteSpace(name)) return false;

        // 版本库、运行时配置等点开头目录一并隐藏（.git / .svn / .luban-agent ...）
        if (name[0] == '.') return true;

        return IgnoredDirectories.Contains(name);
    }
}