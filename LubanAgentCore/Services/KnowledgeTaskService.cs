/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： KnowledgeTaskService
*版本号： V1.0.0.0
*唯一标识：8f2b6d51-3c47-4a9e-b0d5-2e6f7a8c9d10
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/21
*描述：知识库后台任务服务（索引/wiki ingest 提取/wiki 重建），串行执行 + 节流进度 + 显式工作区
*
*****************************************************************************/
using LuBan.AIAgent.Wiki;
using LuBan.AIAgent.Wiki.Extractors;

using LubanAgentCore.Retrieval;

using System.Collections.Concurrent;

namespace LubanAgentCore.Services;

/// <summary>知识库任务类型</summary>
public enum KnowledgeTaskKind
{
    /// <summary>工作区文件索引</summary>
    Index,
    /// <summary>wiki ingest 的源文件提取阶段</summary>
    WikiIngest,
    /// <summary>wiki 向量索引重建</summary>
    WikiRebuild
}

/// <summary>知识库任务状态</summary>
public enum KnowledgeTaskState
{
    /// <summary>执行中</summary>
    Running,
    /// <summary>已完成</summary>
    Completed,
    /// <summary>失败</summary>
    Failed,
    /// <summary>已取消</summary>
    Canceled
}

/// <summary>知识库任务快照（不可变）</summary>
/// <param name="Id">任务ID</param>
/// <param name="Kind">任务类型</param>
/// <param name="WorkspaceId">目标任务工作区ID（显式传递，不依赖当前工作区）</param>
/// <param name="Label">展示名称</param>
/// <param name="State">状态</param>
/// <param name="Done">已完成数量</param>
/// <param name="Total">总数量（未知为 0）</param>
/// <param name="Detail">进度或结果描述</param>
/// <param name="StartedAt">开始时间</param>
/// <param name="EndedAt">结束时间（终态才有值）</param>
/// <param name="ArtifactPath">产物路径（仅 ingest 提取阶段有值：合并后的临时文件）</param>
public sealed record KnowledgeTaskInfo(
    string Id,
    KnowledgeTaskKind Kind,
    string WorkspaceId,
    string Label,
    KnowledgeTaskState State,
    int Done,
    int Total,
    string? Detail,
    DateTime StartedAt,
    DateTime? EndedAt,
    string? ArtifactPath = null);

/// <summary>
/// 知识库后台任务服务：把长耗时的知识库操作（索引/wiki 提取/wiki 重建）放到后台串行执行，
/// 通过 <see cref="Updated"/> 上报开始、进度与终态，调用方无需阻塞 UI。
/// </summary>
/// <remarks>
/// 设计要点（spec 5.1）：
/// <list type="bullet">
/// <item>串行：内部信号量保证同一时刻只有一个知识库任务，避免并发写库；</item>
/// <item>节流：框架 <c>IProgress</c> 按文件回调，此处合并为 ≥500ms 一次且内容去重；</item>
/// <item>显式工作区：全部调用向框架传 <c>workspaceId</c>，后台运行期间用户切库也不串写。</item>
/// </list>
/// </remarks>
public sealed class KnowledgeTaskService
{
    private const string TempConfigDir = ".luban-agent";
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ConcurrentDictionary<string, KnowledgeTaskInfo> _tasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancelSources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<KnowledgeTaskInfo>> _completions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>任务开始/进度/终态统一回调（参数为该任务的最新快照）</summary>
    public event Action<KnowledgeTaskInfo>? Updated;

    /// <summary>创建服务实例</summary>
    /// <param name="scopeFactory">
    /// 根级作用域工厂。本服务为应用级单例（全局任务表 + 跨任务串行写库门），
    /// 任务体内部自建 scope 解析 scoped 的检索/wiki 服务，避免 captive dependency。
    /// </param>
    public KnowledgeTaskService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>全部任务快照（按开始时间倒序）</summary>
    public IReadOnlyList<KnowledgeTaskInfo> Snapshot
        => _tasks.Values.OrderByDescending(t => t.StartedAt).ToList();

    /// <summary>按ID取任务快照；不存在返回 null</summary>
    public KnowledgeTaskInfo? Get(string taskId)
        => _tasks.TryGetValue(taskId, out var info) ? info : null;

    /// <summary>启动工作区文件索引（后台）。<paramref name="glob"/> 为 null 时按默认过滤。</summary>
    public KnowledgeTaskInfo StartIndex(string workspaceId, string root, string? glob = null)
    {
        var label = $"索引工作区「{Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}」";
        return Start(KnowledgeTaskKind.Index, workspaceId, root, label, async (id, ct) =>
        {
            using var scope = _scopeFactory.CreateScope();
            var retrieval = scope.ServiceProvider.GetRequiredService<IRetrievalService>();

            var progress = new ThrottledProgress(p => Mutate(id, t => t with
            {
                Done = p.Done,
                Total = p.Total,
                Detail = p.Stage switch
                {
                    // 扫描阶段由 framework 按目录上报（Total 为已发现文件数），据此显示动态文案而非静态“正在扫描文件…”
                    IndexStage.Scanning => RetrievalIndexPlanner.IndexScanningStatus(p.CurrentFile ?? root, p.Total),
                    IndexStage.Deleting => "正在清理失联文件的切块…",
                    IndexStage.Done => "正在汇总结果…",
                    _ => string.IsNullOrEmpty(p.CurrentFile) ? "正在生成向量…" : $"正在生成向量：{p.CurrentFile}"
                }
            }), TimeSpan.FromMilliseconds(500));

            var report = await retrieval.IndexDirectoryAsync(root, glob, false, progress, ct, workspaceId);
            return (FormatIndexReport(report), (string?)null);
        });
    }

    /// <summary>
    /// 启动 wiki 重建（后台，逐页排除 log.md）。
    /// </summary>
    public KnowledgeTaskInfo StartWikiRebuild(string workspaceId, string root)
        => Start(KnowledgeTaskKind.WikiRebuild, workspaceId, root, "重建 wiki 索引", async (id, ct) =>
        {
            using var scope = _scopeFactory.CreateScope();
            var wiki = scope.ServiceProvider.GetRequiredService<IWikiService>();

            Mutate(id, t => t with { Detail = "正在逐页重建 wiki 向量索引…" });
            var report = await wiki.RebuildIndexAsync(ct, workspaceId);
            return (FormatIndexReport(report), (string?)null);
        });

    /// <summary>
    /// 启动 wiki ingest 的源文件提取阶段（后台）：解析目标（文件/glob/目录）→ 合并为临时 markdown。
    /// 完成后的 <see cref="KnowledgeTaskInfo.ArtifactPath"/> 供调用方继续交给 Agent 整理成 wiki 页。
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    /// <param name="root">工作区根目录</param>
    /// <param name="target">工作区内的文件 / glob / 目录</param>
    public KnowledgeTaskInfo StartWikiIngest(string workspaceId, string root, string target)
    {
        var label = $"提取源文件（{target}）";
        return Start(KnowledgeTaskKind.WikiIngest, workspaceId, root, label, async (id, ct) =>
        {
            var files = ResolveFiles(root, target);
            if (files.Count == 0)
                throw new InvalidOperationException($"未找到匹配文件: {target}");

            Mutate(id, t => t with { Total = files.Count, Detail = "正在提取源文件…" });
            var rootFull = Path.GetFullPath(root);
            var registry = SourceExtractorRegistry.CreateDefault();
            var sb = new StringBuilder();
            sb.Append("# Wiki Ingest 源文件\n\n");
            sb.Append($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            sb.Append($"- 源文件数：{files.Count}\n\n");

            var merged = 0;
            var skipped = new List<string>();
            for (var i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = files[i];
                var rel = ToRelativePath(rootFull, file);
                Mutate(id, t => t with { Done = i, Detail = $"正在提取：{rel}" });
                try
                {
                    var extracted = await registry.ExtractAsync(file, ct);
                    var body = string.IsNullOrWhiteSpace(extracted.Markdown) ? extracted.Text : extracted.Markdown;
                    sb.Append("## ").Append(rel).Append("\n\n");
                    if (extracted.Truncated) sb.Append("> （内容已截断）\n\n");
                    sb.Append(body).Append("\n\n");
                    merged++;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"跳过 {rel}: {ex.Message}");
                    skipped.Add($"{rel}（{ex.Message}）");
                }
            }

            if (merged == 0)
                throw new InvalidOperationException($"所有源文件均提取失败: {target}");

            var mergedText = sb.ToString();
            var truncatedTotal = false;
            if (mergedText.Length > TextExtractor.MaxChars)
            {
                mergedText = mergedText[..TextExtractor.MaxChars];
                truncatedTotal = true;
            }

            var tempDir = Path.Combine(rootFull, TempConfigDir, "temp");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, $"wiki-ingest-{DateTime.Now:yyyyMMddHHmmss}.md");
            await File.WriteAllTextAsync(tempPath, mergedText, ct);

            var detail = $"已提取 {merged}/{files.Count} 个源文件 → {ToRelativePath(rootFull, tempPath)}";
            if (truncatedTotal) detail += $"，汇总超 {TextExtractor.MaxChars} 字符已截断";
            if (skipped.Count > 0) detail += $"，跳过 {skipped.Count} 个（{string.Join("；", skipped.Take(3))}）";
            return (detail, tempPath);
        });
    }

    /// <summary>取消指定任务；任务不存在或已终态返回 false</summary>
    public bool Cancel(string taskId)
    {
        if (!_cancelSources.TryGetValue(taskId, out var cts)) return false;
        try { cts.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }

    /// <summary>等待任务进入终态（供 <c>--wait</c> 等同步场景使用）</summary>
    public async Task<KnowledgeTaskInfo> WaitAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_tasks.TryGetValue(taskId, out var info) && IsTerminal(info.State)) return info;
        if (!_completions.TryGetValue(taskId, out var tcs))
            throw new InvalidOperationException($"任务不存在: {taskId}");
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task;
    }

    private KnowledgeTaskInfo Start(KnowledgeTaskKind kind, string workspaceId, string root, string label,
        Func<string, CancellationToken, Task<(string Detail, string? ArtifactPath)>> body)
    {
        var id = Guid.NewGuid().ToString("N");
        var info = new KnowledgeTaskInfo(id, kind, workspaceId, label, KnowledgeTaskState.Running,
            0, 0, "已转入后台，等待执行…", DateTime.Now, null);
        _tasks[id] = info;
        var cts = new CancellationTokenSource();
        _cancelSources[id] = cts;
        _completions[id] = new TaskCompletionSource<KnowledgeTaskInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        Raise(info);

        _ = Task.Run(async () =>
        {
            try
            {
                await _gate.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Finish(id, KnowledgeTaskState.Canceled, "已取消");
                return;
            }

            try
            {
                Mutate(id, t => t with { Detail = "开始执行…" });
                var (detail, artifactPath) = await body(id, cts.Token);
                Finish(id, KnowledgeTaskState.Completed, detail, artifactPath);
            }
            catch (OperationCanceledException)
            {
                Finish(id, KnowledgeTaskState.Canceled, "已取消");
            }
            catch (Exception ex)
            {
                Logger.Error($"知识库后台任务失败: {label}", ex, workspaceId);
                Finish(id, KnowledgeTaskState.Failed, $"{ex.Message}（可稍后重试）");
            }
            finally
            {
                _gate.Release();
            }
        });

        return info;
    }

    private void Finish(string id, KnowledgeTaskState state, string detail, string? artifactPath = null)
    {
        var final = Mutate(id, t => t with
        {
            State = state,
            Detail = detail,
            EndedAt = DateTime.Now,
            ArtifactPath = artifactPath ?? t.ArtifactPath,
            Total = t.Total
        });
        if (_cancelSources.TryRemove(id, out var cts)) cts.Dispose();
        if (_completions.TryGetValue(id, out var tcs)) tcs.TrySetResult(final);
    }

    private KnowledgeTaskInfo Mutate(string id, Func<KnowledgeTaskInfo, KnowledgeTaskInfo> change)
    {
        var updated = _tasks.AddOrUpdate(id, _ => throw new InvalidOperationException($"任务不存在: {id}"),
            (_, current) => change(current));
        Raise(updated);
        return updated;
    }

    private void Raise(KnowledgeTaskInfo info)
    {
        try { Updated?.Invoke(info); }
        catch (Exception ex) { Logger.Error("知识库任务通知失败", ex, info.Id); }
    }

    private static bool IsTerminal(KnowledgeTaskState state)
        => state is KnowledgeTaskState.Completed or KnowledgeTaskState.Failed or KnowledgeTaskState.Canceled;

    private static string FormatIndexReport(IndexReport report)
    {
        var text = $"扫描 {report.ScannedFiles}，新增 {report.NewFiles}，更新 {report.UpdatedFiles}，" +
                   $"跳过 {report.SkippedFiles}，删除 {report.DeletedFiles}，切块 {report.TotalChunks}";
        if (report.Errors.Count > 0) text += $"；{report.Errors.Count} 个文件失败（{string.Join("；", report.Errors.Take(3))}）";
        if (report.Warnings.Count > 0) text += $"；{report.Warnings.Count} 个文件内容被截断（{string.Join("；", report.Warnings.Take(3))}）";
        return text;
    }

    /// <summary>
    /// 解析工作区根下的目标：单文件 / glob / 目录，结果限制在工作区内（防 ../ 越界）
    /// </summary>
    private static List<string> ResolveFiles(string rootPath, string target)
    {
        var rootFull = Path.GetFullPath(rootPath);
        var full = Path.IsPathRooted(target) ? target : Path.Combine(rootFull, target);
        if (!IsInsideRoot(rootFull, full)) return new List<string>();

        if (File.Exists(full)) return new List<string> { full };

        var dir = Path.GetDirectoryName(full);
        var pattern = Path.GetFileName(full);
        if (dir != null && Directory.Exists(dir) && (pattern.Contains('*') || pattern.Contains('?')))
            return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
                .Where(f => IsInsideRoot(rootFull, f)).ToList();

        return Directory.Exists(full)
            ? Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Where(f => IsInsideRoot(rootFull, f)).ToList()
            : new List<string>();
    }

    private static bool IsInsideRoot(string rootFull, string candidate)
    {
        var full = Path.GetFullPath(candidate);
        var root = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRelativePath(string rootFull, string path)
        => Path.GetRelativePath(rootFull, path).Replace('\\', '/');

    /// <summary>
    /// 进度节流器：同一阶段/文件重复回调不发通知，且两次通知间隔不小于 <c>interval</c>（终态阶段除外）。
    /// </summary>
    private sealed class ThrottledProgress : IProgress<IndexProgress>
    {
        private readonly Action<IndexProgress> _onReport;
        private readonly TimeSpan _interval;
        private readonly object _lock = new();
        private DateTime _last = DateTime.MinValue;
        private IndexProgress? _lastReported;

        public ThrottledProgress(Action<IndexProgress> onReport, TimeSpan interval)
        {
            _onReport = onReport;
            _interval = interval;
        }

        public void Report(IndexProgress value)
        {
            lock (_lock)
            {
                var last = _lastReported;
                var changed = last == null || last.Stage != value.Stage || last.Done != value.Done
                              || last.CurrentFile != value.CurrentFile;
                if (!changed) return;
                if (value.Stage != IndexStage.Done && DateTime.UtcNow - _last < _interval) return;
                _last = DateTime.UtcNow;
                _lastReported = value;
            }
            _onReport(value);
        }
    }
}