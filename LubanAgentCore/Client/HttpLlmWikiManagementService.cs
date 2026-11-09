/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： HttpLlmWikiManagementService
*描述：ILlmWikiManagementService 的 HTTP 实现（知识库仓储、检索与 wiki 服务都在 WebHost 侧）
*
*****************************************************************************/

using LuBan.AIAgent.Retrieval;
using LuBan.AIAgent.Wiki;

using LubanAgentCore.Services.Management;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Client;

/// <summary>
/// LLM Wiki 知识库管理的 HTTP 实现。
/// </summary>
/// <remarks>
/// <para>
/// 索引、检索、wiki 重建都依赖嵌入模型与 SQLite 索引，全部在 WebHost 进程内执行，
/// 客户端只表达意图并取回结果，不解析 <c>IRetrievalService</c> / <c>IWikiService</c>
/// （本地实现会在构造期直接 new 仓储，客户端没有数据库可用）。
/// </para>
/// <para>
/// <c>Start*</c>（后台任务）一律返回 null：宿主侧的后台进度靠 <c>KnowledgeTaskService.Updated</c>
/// 事件推送，客户端没有通道，调用方据此回落到同步 <c>--wait</c> 路径。
/// </para>
/// </remarks>
public sealed class HttpLlmWikiManagementService(AgentServerClient client) : ILlmWikiManagementService
{
    /// <inheritdoc />
    public async Task<WorkspaceInfo> CreateAsync(string rootPath, string? name, CancellationToken ct = default)
    {
        var dto = await client.PostAsync<WorkspaceDto>(
            AgentApiRoutes.LlmWiki.Create,
            new LlmWikiCreateRequest { RootPath = rootPath, Name = name },
            ct).ConfigureAwait(false);

        return ToInfo(dto);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string workspaceId, CancellationToken ct = default)
        => client.PostAsync(
            AgentApiRoutes.LlmWiki.Delete,
            new LlmWikiWorkspaceRequest { WorkspaceId = workspaceId },
            ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WikiFileInfo>> ListFilesAsync(string workspaceId, CancellationToken ct = default)
    {
        var files = await client.GetAsync<List<WikiFileInfo>>(
            AgentApiRoutes.LlmWiki.ListFiles,
            new Dictionary<string, string?> { ["workspaceId"] = workspaceId },
            ct).ConfigureAwait(false);

        return files;
    }

    /// <inheritdoc />
    public Task<IndexReport> IndexFilesAsync(string workspaceId, string? glob = null, CancellationToken ct = default)
        => client.PostAsync<IndexReport>(
            AgentApiRoutes.LlmWiki.Index,
            new LlmWikiIndexRequest { WorkspaceId = workspaceId, Glob = glob },
            ct);

    /// <inheritdoc />
    /// <remarks>客户端没有后台任务进度通道，固定返回 null，由调用方走同步索引。</remarks>
    public Task<KnowledgeTaskInfo?> StartIndexAsync(string workspaceId, string? glob = null, CancellationToken ct = default)
        => Task.FromResult<KnowledgeTaskInfo?>(null);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string workspaceId, string query, WikiSearchScope scope, CancellationToken ct = default)
    {
        var results = await client.PostAsync<List<RetrievalResult>>(
            AgentApiRoutes.LlmWiki.Search,
            new LlmWikiSearchRequest { WorkspaceId = workspaceId, Query = query, Scope = scope.ToString() },
            ct).ConfigureAwait(false);

        return results;
    }

    /// <inheritdoc />
    public Task<WikiStatus> GetStatusAsync(string? workspaceId = null, CancellationToken ct = default)
        => client.GetAsync<WikiStatus>(
            AgentApiRoutes.LlmWiki.Status,
            new Dictionary<string, string?> { ["workspaceId"] = workspaceId },
            ct);

    /// <inheritdoc />
    public Task<WikiLintReport> LintAsync(string workspaceId, CancellationToken ct = default)
        => client.PostAsync<WikiLintReport>(
            AgentApiRoutes.LlmWiki.Lint,
            new LlmWikiWorkspaceRequest { WorkspaceId = workspaceId },
            ct);

    /// <inheritdoc />
    public Task<IndexReport> RebuildAsync(string workspaceId, CancellationToken ct = default)
        => client.PostAsync<IndexReport>(
            AgentApiRoutes.LlmWiki.Rebuild,
            new LlmWikiWorkspaceRequest { WorkspaceId = workspaceId },
            ct);

    /// <inheritdoc />
    /// <remarks>客户端没有后台任务进度通道，固定返回 null，由调用方走同步重建。</remarks>
    public Task<KnowledgeTaskInfo?> StartRebuildAsync(string workspaceId, CancellationToken ct = default)
        => Task.FromResult<KnowledgeTaskInfo?>(null);

    /// <inheritdoc />
    /// <remarks>逐文件进度只在宿主侧可见，<paramref name="progress"/> 在这里被忽略。</remarks>
    public Task<WikiIngestDraft> PrepareIngestAsync(
        string workspaceId, string target, IProgress<string>? progress = null, CancellationToken ct = default)
        => client.PostAsync<WikiIngestDraft>(
            AgentApiRoutes.LlmWiki.PrepareIngest,
            new LlmWikiIngestRequest { WorkspaceId = workspaceId, Target = target },
            ct);

    /// <inheritdoc />
    /// <remarks>客户端没有后台任务进度通道，固定返回 null，由调用方走同步提取。</remarks>
    public Task<KnowledgeTaskInfo?> StartIngestAsync(string workspaceId, string target, CancellationToken ct = default)
        => Task.FromResult<KnowledgeTaskInfo?>(null);

    /// <inheritdoc />
    public Task ClearAsync(string workspaceId, CancellationToken ct = default)
        => client.PostAsync(
            AgentApiRoutes.LlmWiki.Clear,
            new LlmWikiWorkspaceRequest { WorkspaceId = workspaceId },
            ct);

    private static WorkspaceInfo ToInfo(WorkspaceDto workspace) => new()
    {
        WorkspaceId = workspace.WorkspaceId,
        Name = workspace.Name,
        RootPath = workspace.RootPath,
        Type = workspace.Type,
        UserId = workspace.UserId,
        ConfigPath = workspace.ConfigPath,
        LastActiveAt = workspace.LastActiveAt,
        IsAuthorized = workspace.IsAuthorized
    };
}