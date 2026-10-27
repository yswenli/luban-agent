using LuBan.AIAgent.Retrieval;
using LuBan.AIAgent.Wiki;
using LuBan.AIAgent.Wiki.Extractors;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// LLM Wiki 知识库管理实现。
/// </summary>
/// <remarks>
/// 索引/检索/wiki 能力依赖嵌入模型，故 <see cref="IRetrievalService"/>、<see cref="IWikiService"/>、
/// <see cref="KnowledgeTaskService"/> 均为可选依赖（宿主仅在配置嵌入模型时注册）；
/// 未启用时相关方法抛带说明的 <see cref="InvalidOperationException"/>，而工作区与文件索引的增删查仍可用。
/// 授权确认、关键字匹配、进度窗与提示文案仍留在命令层。
/// </remarks>
public sealed class LlmWikiManagementService : ILlmWikiManagementService
{
    private const string DefaultConfigDir = ".luban-agent";
    private const string NoEmbedderMessage = "当前未配置嵌入模型，无法进行知识库索引或检索。";

    private readonly IWorkspaceManager _workspaces;
    private readonly ILlmWikiFileStore _files;
    private readonly ILlmWikiChunkStore _chunks;
    private readonly IRetrievalService? _retrieval;
    private readonly IWikiService? _wiki;
    private readonly KnowledgeTaskService? _tasks;

    /// <summary>创建 LLM Wiki 管理服务。</summary>
    /// <param name="workspaces">工作区管理器（解析根路径与归属用户）。</param>
    /// <param name="files">LLM Wiki 文件索引仓储缝。</param>
    /// <param name="chunks">LLM Wiki 切块索引仓储缝。</param>
    /// <param name="retrieval">检索服务；未配置嵌入模型时为 null。</param>
    /// <param name="wiki">wiki 知识库服务；未配置嵌入模型时为 null。</param>
    /// <param name="tasks">后台知识库任务服务；未配置嵌入模型时为 null。</param>
    public LlmWikiManagementService(
        IWorkspaceManager workspaces,
        ILlmWikiFileStore files,
        ILlmWikiChunkStore chunks,
        IRetrievalService? retrieval = null,
        IWikiService? wiki = null,
        KnowledgeTaskService? tasks = null)
    {
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        _retrieval = retrieval;
        _wiki = wiki;
        _tasks = tasks;
    }

    /// <inheritdoc />
    public Task<WorkspaceInfo> CreateAsync(string rootPath, string? name, CancellationToken ct = default)
        => _workspaces.CreateWorkspaceAsync(rootPath, name, WorkspaceTypes.LlmWiki);

    /// <inheritdoc />
    public async Task DeleteAsync(string workspaceId, CancellationToken ct = default)
    {
        await _workspaces.DeleteWorkspaceAsync(workspaceId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WikiFileInfo>> ListFilesAsync(string workspaceId, CancellationToken ct = default)
    {
        var files = await _files.GetByWorkspaceAsync(workspaceId);
        return files
            .Select(f => new WikiFileInfo(f.FilePath, f.Language, f.ChunkCount, f.IndexedTime))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IndexReport> IndexFilesAsync(string workspaceId, string? glob = null, CancellationToken ct = default)
    {
        var workspace = await RequireWorkspaceAsync(workspaceId);
        return await RequireRetrieval().IndexDirectoryAsync(
            workspace.RootPath, glob, force: false, progress: null, cancellationToken: ct, workspaceId: workspaceId);
    }

    /// <inheritdoc />
    public async Task<KnowledgeTaskInfo?> StartIndexAsync(string workspaceId, string? glob = null, CancellationToken ct = default)
    {
        if (_tasks is null) return null;

        var workspace = await RequireWorkspaceAsync(workspaceId);
        return _tasks.StartIndex(workspaceId, workspace.RootPath, glob, workspace.UserId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string workspaceId, string query, WikiSearchScope scope, CancellationToken ct = default)
    {
        if (scope == WikiSearchScope.RawOnly)
        {
            return await RequireRetrieval().SearchAsync(
                query, topK: 5, pathPrefix: null, language: null, cancellationToken: ct, workspaceId: workspaceId);
        }

        return await RequireWiki().SearchAsync(
            query, topK: null, includeRaw: scope != WikiSearchScope.WikiOnly, cancellationToken: ct, workspaceId: workspaceId);
    }

    /// <inheritdoc />
    public async Task<WikiStatus> GetStatusAsync(string? workspaceId = null, CancellationToken ct = default)
    {
        var id = string.IsNullOrEmpty(workspaceId)
            ? _workspaces.CurrentWorkspace?.WorkspaceId
            : workspaceId;
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("请先使用 /work -switch 切换工作区");

        var stats = await RequireWiki().GetStatsAsync(ct, id);
        var files = await _files.GetByWorkspaceAsync(id);
        var chunks = await _chunks.CountByWorkspaceAsync(id);

        return new WikiStatus(stats.PageCount, stats.LastRebuildSummary, files.Count, chunks);
    }

    /// <inheritdoc />
    public Task<WikiLintReport> LintAsync(string workspaceId, CancellationToken ct = default)
        => RequireWiki().LintAsync(ct, workspaceId);

    /// <inheritdoc />
    public Task<IndexReport> RebuildAsync(string workspaceId, CancellationToken ct = default)
        => RequireWiki().RebuildIndexAsync(ct, workspaceId);

    /// <inheritdoc />
    public async Task<KnowledgeTaskInfo?> StartRebuildAsync(string workspaceId, CancellationToken ct = default)
    {
        if (_tasks is null) return null;

        var workspace = await RequireWorkspaceAsync(workspaceId);
        return _tasks.StartWikiRebuild(workspaceId, workspace.RootPath, workspace.UserId);
    }

    /// <inheritdoc />
    public async Task<WikiIngestDraft> PrepareIngestAsync(
        string workspaceId, string target, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var workspace = await RequireWorkspaceAsync(workspaceId);
        var files = KnowledgeTaskService.ResolveFiles(workspace.RootPath, target);
        if (files.Count == 0)
            throw new InvalidOperationException($"未找到匹配文件: {target}");

        var rootFull = Path.GetFullPath(workspace.RootPath);
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
            var rel = KnowledgeTaskService.ToRelativePath(rootFull, file);
            progress?.Report($"正在提取源文件 {i + 1}/{files.Count}：{rel}");

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
                skipped.Add($"跳过 {rel}: {ex.Message}");
            }
        }

        if (merged == 0)
            return new WikiIngestDraft(string.Empty, string.Empty, 0, files.Count, false, skipped);

        var mergedText = sb.ToString();
        var truncated = false;
        if (mergedText.Length > TextExtractor.MaxChars)
        {
            mergedText = mergedText[..TextExtractor.MaxChars];
            truncated = true;
        }

        var tempDir = Path.Combine(workspace.RootPath, workspace.ConfigPath ?? DefaultConfigDir, "temp");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"wiki-ingest-{DateTime.Now:yyyyMMddHHmmss}.md");
        await File.WriteAllTextAsync(tempPath, mergedText, ct);

        return new WikiIngestDraft(
            tempPath,
            KnowledgeTaskService.ToRelativePath(rootFull, tempPath),
            merged,
            files.Count,
            truncated,
            skipped);
    }

    /// <inheritdoc />
    public async Task<KnowledgeTaskInfo?> StartIngestAsync(string workspaceId, string target, CancellationToken ct = default)
    {
        if (_tasks is null) return null;

        var workspace = await RequireWorkspaceAsync(workspaceId);
        return _tasks.StartWikiIngest(workspaceId, workspace.RootPath, target);
    }

    /// <inheritdoc />
    public async Task ClearAsync(string workspaceId, CancellationToken ct = default)
    {
        await _files.DeleteByWorkspaceAsync(workspaceId);
        await _chunks.DeleteByWorkspaceAsync(workspaceId);
    }

    private async Task<WorkspaceInfo> RequireWorkspaceAsync(string workspaceId)
        => await _workspaces.GetWorkspaceAsync(workspaceId)
           ?? throw new InvalidOperationException($"找不到工作区: {workspaceId}");

    private IRetrievalService RequireRetrieval()
        => _retrieval ?? throw new InvalidOperationException(NoEmbedderMessage);

    private IWikiService RequireWiki()
        => _wiki ?? throw new InvalidOperationException(NoEmbedderMessage);
}