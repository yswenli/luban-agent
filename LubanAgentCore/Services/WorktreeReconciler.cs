/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： WorktreeReconciler
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：启动时对账 worktree 登记（DB）与磁盘 worktree 的一致性
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions.Models;

namespace LubanAgentCore.Services;

/// <summary>
/// worktree 启动对账器：比对 <c>ai_worktree_session</c> 登记、各工作区磁盘上的
/// <c>.worktrees/</c> 目录与 <c>git worktree list</c>，输出不一致提示（不自动修改）。
/// </summary>
public sealed class WorktreeReconciler
{
    private readonly WorktreeSessionRepository _repo;
    private readonly IWorkspaceManager _workspaces;
    private readonly GitProcess _git;

    /// <summary>
    /// 创建 worktree 对账器。
    /// </summary>
    /// <param name="repo">worktree 会话仓储。</param>
    /// <param name="workspaces">工作区管理器（枚举工作区根）。</param>
    /// <param name="git">git 命令封装。</param>
    public WorktreeReconciler(WorktreeSessionRepository repo, IWorkspaceManager workspaces, GitProcess git)
    {
        _repo = repo;
        _workspaces = workspaces;
        _git = git;
    }

    /// <summary>
    /// 执行一次全量对账。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>不一致提示列表；为空表示一致。</returns>
    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();

        List<WorktreeSession> records;
        try
        {
            records = await _repo.GetAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            messages.Add($"读取 worktree 登记失败：{ex.Message}");
            return messages;
        }

        // 1) 有登记但目录已缺失：需人工恢复或放弃
        foreach (var record in records)
        {
            if (!Directory.Exists(record.WorktreePath))
                messages.Add($"worktree 登记 {record.SessionId} 对应目录缺失：{record.WorktreePath}（可放弃或人工恢复）");
        }

        // 2) 各工作区磁盘上存在但无登记：孤儿 worktree，可 prune
        IEnumerable<WorkspaceInfo> workspaceList;
        try
        {
            workspaceList = await _workspaces.GetUserWorkspacesAsync(null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            messages.Add($"列举工作区失败：{ex.Message}");
            return messages;
        }

        foreach (var workspace in workspaceList)
        {
            if (string.IsNullOrWhiteSpace(workspace.RootPath) || !Directory.Exists(workspace.RootPath))
                continue;

            var worktreesRoot = Path.Combine(workspace.RootPath, ".worktrees");
            if (!Directory.Exists(worktreesRoot))
                continue;

            foreach (var path in await ListGitWorktreesAsync(workspace.RootPath, cancellationToken).ConfigureAwait(false))
            {
                if (!IsWithin(worktreesRoot, path))
                    continue;
                if (!records.Any(r => PathEquals(r.WorktreePath, path)))
                    messages.Add($"发现孤儿 worktree（无登记）：{path}（可执行 git worktree prune 清理）");
            }
        }

        return messages;
    }

    private async Task<List<string>> ListGitWorktreesAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _git.RunRawAsync(root, ["worktree", "list", "--porcelain"], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return [];

            return result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("worktree ", StringComparison.Ordinal))
                .Select(line => line["worktree ".Length..].Trim())
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsWithin(string root, string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathEquals(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}