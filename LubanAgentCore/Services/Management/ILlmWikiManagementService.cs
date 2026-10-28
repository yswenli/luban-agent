/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： ILlmWikiManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：LLM Wiki 知识库管理。 方法面 = 现有 <c>/llmwiki</c> 命令的九个业务子命令，不新增能力； 授权弹窗、关键字匹配、进度窗与文案仍留命令层。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：LLM Wiki 知识库管理。 方法面 = 现有 <c>/llmwiki</c> 命令的九个业务子命令，不新增能力； 授权弹窗、关键字匹配、进度窗与文案仍留命令层。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// LLM Wiki 知识库管理。
/// 方法面 = 现有 <c>/llmwiki</c> 命令的九个业务子命令，不新增能力；
/// 授权弹窗、关键字匹配、进度窗与文案仍留命令层。
/// </summary>
/// <remarks>
/// 检索/索引/wiki 能力依赖嵌入模型：未配置嵌入模型时相关方法抛
/// <see cref="InvalidOperationException"/>，但工作区创建/删除/列文件等不依赖嵌入模型的方法仍可用。
/// </remarks>
public interface ILlmWikiManagementService
{
    /// <summary>创建 LLM Wiki 知识库工作区（<c>-new</c>）。</summary>
    Task<WorkspaceInfo> CreateAsync(string rootPath, string? name, CancellationToken ct = default);

    /// <summary>删除知识库工作区并级联清理会话与索引（<c>-delete</c>）。</summary>
    Task DeleteAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>列出已索引文件（<c>-list</c>）。</summary>
    Task<IReadOnlyList<WikiFileInfo>> ListFilesAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>同步索引原始资料（<c>-index --wait</c>）。</summary>
    Task<IndexReport> IndexFilesAsync(string workspaceId, string? glob = null, CancellationToken ct = default);

    /// <summary>后台索引原始资料（<c>-index</c>）；未启用后台任务服务时返回 null。</summary>
    Task<KnowledgeTaskInfo?> StartIndexAsync(string workspaceId, string? glob = null, CancellationToken ct = default);

    /// <summary>检索知识库（<c>-search</c>）。</summary>
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(string workspaceId, string query, WikiSearchScope scope, CancellationToken ct = default);

    /// <summary>读取 wiki 统计（<c>-status</c>）；<paramref name="workspaceId"/> 为 null 时取当前工作区。</summary>
    Task<WikiStatus> GetStatusAsync(string? workspaceId = null, CancellationToken ct = default);

    /// <summary>客观项 lint（<c>-lint</c>）。</summary>
    Task<WikiLintReport> LintAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>同步重建 wiki 向量索引（<c>-rebuild --wait</c>）。</summary>
    Task<IndexReport> RebuildAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>后台重建 wiki 向量索引（<c>-rebuild</c>）；未启用后台任务服务时返回 null。</summary>
    Task<KnowledgeTaskInfo?> StartRebuildAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>
    /// 同步提取 ingest 源文件并合并为工作区内临时 markdown（<c>-ingest --wait</c> 的提取段）。
    /// 交给 Agent 整理仍由命令层完成。
    /// </summary>
    /// <param name="workspaceId">工作区标识。</param>
    /// <param name="target">工作区内文件 / glob / 目录。</param>
    /// <param name="progress">逐文件进度文案；可为 null。</param>
    /// <param name="ct">取消令牌。</param>
    Task<WikiIngestDraft> PrepareIngestAsync(string workspaceId, string target, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>后台提取 ingest 源文件（<c>-ingest</c>）；未启用后台任务服务时返回 null。</summary>
    Task<KnowledgeTaskInfo?> StartIngestAsync(string workspaceId, string target, CancellationToken ct = default);

    /// <summary>清空指定工作区的文件与切块索引（不删除工作区记录）。</summary>
    Task ClearAsync(string workspaceId, CancellationToken ct = default);
}