/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： WorktreeSessionManager
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：worktree 会话生命周期协调器（IWorktreeCoordinator 的宿主实现）
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions.Models;

namespace LubanAgentCore.Services;

/// <summary>
/// worktree 会话生命周期协调器：在会话作用域内用 git worktree 提供目录隔离，
/// 会话停留在 worktree 直至显式「完成/放弃」。
/// </summary>
public sealed class WorktreeSessionManager : IWorktreeCoordinator
{
    private readonly ISessionContext _sessionContext;
    private readonly SessionContextState _state;
    private readonly WorktreeSessionRepository _repo;
    private readonly GitProcess _git;

    /// <summary>
    /// 创建 worktree 会话协调器。
    /// </summary>
    /// <param name="sessionContext">会话上下文（提供有效根）。</param>
    /// <param name="state">会话可写状态（登记活动 worktree 与允许根）。</param>
    /// <param name="repo">worktree 会话仓储。</param>
    /// <param name="git">git 命令封装。</param>
    public WorktreeSessionManager(
        ISessionContext sessionContext,
        SessionContextState state,
        WorktreeSessionRepository repo,
        GitProcess git)
    {
        _sessionContext = sessionContext;
        _state = state;
        _repo = repo;
        _git = git;
    }

    /// <inheritdoc />
    public async Task<WorktreeSession> BeginAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (_state.ActiveWorktree is not null)
            throw new InvalidOperationException("当前会话已处于 worktree 隔离中");
        if (string.IsNullOrWhiteSpace(_state.SessionId))
            throw new InvalidOperationException("会话未就绪，无法进入 worktree");
        if (string.IsNullOrWhiteSpace(_state.WorkspaceId))
            throw new InvalidOperationException("会话未绑定工作区，无法进入 worktree");

        var root = _sessionContext.EffectiveRoot;
        await EnsureGitRepoAsync(root, cancellationToken).ConfigureAwait(false);

        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var worktreesDir = Path.Combine(root, ".worktrees");
        var path = Path.Combine(worktreesDir, ts);
        var branch = $"luban-worktree/{ts}";

        var baseBranch = (await _git.RunAsync(root, ["symbolic-ref", "--short", "HEAD"], cancellationToken).ConfigureAwait(false)).Trim();
        var baseCommit = (await _git.RunAsync(root, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false)).Trim();

        Directory.CreateDirectory(worktreesDir);
        await _git.RunAsync(root, ["worktree", "add", "-b", branch, path, "HEAD"], cancellationToken).ConfigureAwait(false);

        var session = new WorktreeSession
        {
            SessionId = _state.SessionId!,
            WorkspaceId = _state.WorkspaceId!,
            WorktreePath = path,
            Branch = branch,
            BaseBranch = baseBranch,
            BaseCommit = baseCommit,
            CreatedAt = DateTimeOffset.Now,
        };

        _state.ActiveWorktree = session;
        if (!_state.AllowedRoots.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
            _state.AllowedRoots.Add(path);

        await _repo.SaveAsync(session).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public async Task<WorktreeMergeResult> EndAsync(CancellationToken cancellationToken = default)
    {
        var active = _state.ActiveWorktree;
        if (active is null)
            return new WorktreeMergeResult(false, false, "当前会话不在 worktree 中");

        var mainRoot = _state.WorkspaceRoot
            ?? throw new InvalidOperationException("会话未设置工作区根");

        // 基线分支漂移检测：主树当前分支需与进入时一致，否则停止交人工
        var currentBranch = (await _git.RunAsync(mainRoot, ["symbolic-ref", "--short", "HEAD"], cancellationToken).ConfigureAwait(false)).Trim();
        if (!string.Equals(currentBranch, active.BaseBranch, StringComparison.Ordinal))
            return new WorktreeMergeResult(false, false,
                $"主树当前分支 {currentBranch} 与基线分支 {active.BaseBranch} 不一致，已停止合并，请人工处理");

        // 主树必须干净，避免 merge 与本地未提交改动纠缠
        var mainStatus = (await _git.RunAsync(mainRoot, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false)).Trim();
        if (mainStatus.Length > 0)
            return new WorktreeMergeResult(false, false, "主树存在未提交改动，已停止合并，请先提交或 stash");

        // worktree 内提交（无改动则跳过）
        var wtStatus = (await _git.RunAsync(active.WorktreePath, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false)).Trim();
        if (wtStatus.Length > 0)
        {
            await _git.RunAsync(active.WorktreePath, ["add", "-A"], cancellationToken).ConfigureAwait(false);
            await _git.RunAsync(active.WorktreePath, ["commit", "-m", $"luban-worktree: {active.Branch}"], cancellationToken).ConfigureAwait(false);
        }

        // 主树合并临时分支；冲突则保留 worktree 与分支交人工
        var merge = await _git.RunRawAsync(mainRoot, ["merge", "--no-edit", active.Branch], cancellationToken).ConfigureAwait(false);
        if (merge.ExitCode != 0)
        {
            var diff = await _git.RunRawAsync(mainRoot, ["diff", "--name-only", "--diff-filter=U"], cancellationToken).ConfigureAwait(false);
            var conflictFiles = diff.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (conflictFiles.Count > 0)
                return new WorktreeMergeResult(false, true,
                    $"合并冲突，已保留 worktree 与分支 {active.Branch}，请人工解决", conflictFiles);

            var detail = string.IsNullOrWhiteSpace(merge.StandardError) ? merge.StandardOutput : merge.StandardError;
            return new WorktreeMergeResult(false, false, $"合并失败（exit={merge.ExitCode}）：{detail.Trim()}");
        }

        await CleanupAsync(mainRoot, active, cancellationToken).ConfigureAwait(false);
        return new WorktreeMergeResult(true, false, $"已合并 {active.Branch} 并清理 worktree");
    }

    /// <inheritdoc />
    public async Task AbandonAsync(CancellationToken cancellationToken = default)
    {
        var active = _state.ActiveWorktree;
        if (active is null) return;

        var mainRoot = _state.WorkspaceRoot
            ?? throw new InvalidOperationException("会话未设置工作区根");

        try
        {
            await _git.RunAsync(mainRoot, ["worktree", "remove", "--force", active.WorktreePath], cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // worktree 目录可能已被外部删除，继续清理分支与登记
        }

        try
        {
            await _git.RunAsync(mainRoot, ["branch", "-D", active.Branch], cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // 分支可能已被外部删除
        }

        await _repo.DeleteBySessionAsync(active.SessionId).ConfigureAwait(false);
        ResetState(active);
    }

    /// <inheritdoc />
    public WorktreeSession? GetActive(string sessionId)
        => string.Equals(_state.SessionId, sessionId, StringComparison.Ordinal)
            ? _state.ActiveWorktree
            : null;

    private async Task CleanupAsync(string mainRoot, WorktreeSession active, CancellationToken cancellationToken)
    {
        try
        {
            await _git.RunAsync(mainRoot, ["worktree", "remove", active.WorktreePath], cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            await _git.RunAsync(mainRoot, ["worktree", "remove", "--force", active.WorktreePath], cancellationToken).ConfigureAwait(false);
        }

        await _git.RunAsync(mainRoot, ["branch", "-D", active.Branch], cancellationToken).ConfigureAwait(false);
        await _repo.DeleteBySessionAsync(active.SessionId).ConfigureAwait(false);
        ResetState(active);

        try
        {
            TempDirectory.Cleanup(TimeSpan.Zero);
        }
        catch
        {
            // 临时目录清理为尽力而为，失败不影响合并结果
        }
    }

    private void ResetState(WorktreeSession active)
    {
        _state.ActiveWorktree = null;
        _state.AllowedRoots.RemoveAll(r => string.Equals(r, active.WorktreePath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnsureGitRepoAsync(string dir, CancellationToken cancellationToken)
    {
        var result = await _git.RunRawAsync(dir, ["rev-parse", "--is-inside-work-tree"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"当前目录不是 git 仓库，无法进入 worktree：{dir}");
    }
}