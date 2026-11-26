/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WorkspaceGit
*描述：工作区根目录的 Git 仓库标记（只读目录元数据，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 判断工作区根目录本身是不是 Git 仓库。
/// </summary>
/// <remarks>
/// 只认根目录下的 <c>.git</c>：普通仓库是目录，worktree / submodule 是文件，两种形态都算。
/// 子目录里的仓库不代表工作区本身是仓库，因此不向父目录探测；
/// 也不解析 <c>.git</c> 内容，只看是否存在，列表项判定不引入 git 进程或配置文件解析。
/// </remarks>
public static class WorkspaceGit
{
    /// <summary>git 元数据项名。</summary>
    private const string GitEntryName = ".git";

    /// <summary>
    /// 工作区根目录是否为 Git 仓库根。
    /// </summary>
    /// <param name="rootPath">工作区根目录的绝对路径。</param>
    /// <returns>根下存在 <c>.git</c> 目录或文件时为 <c>true</c>；路径空白或不可访问时为 <c>false</c>。</returns>
    public static bool IsRepository(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        try
        {
            var gitEntry = Path.Combine(rootPath.Trim(), GitEntryName);
            return Directory.Exists(gitEntry) || File.Exists(gitEntry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}