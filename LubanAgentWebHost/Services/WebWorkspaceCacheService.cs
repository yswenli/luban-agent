/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebWorkspaceCacheService
*描述：工作区缓存（临时目录）扫描与回收业务
*
*****************************************************************************/

using System.Text.Json;

namespace LubanAgentWebHost.Services;

/// <summary>
/// 工作区缓存业务。
/// </summary>
/// <remarks>
/// <para>
/// 范围固定为当前用户各工作区下的 <c>{ConfigPath}/temp</c>，不触碰技能、规则、MCP、知识库索引与代码。
/// 清理前按「该范围内是否有运行中的会话」拦截，避免删掉本轮刚写入的附件。
/// </para>
/// <para>
/// 引用集合取自当前用户全部历史消息的附件元数据，并补上绑定到目标工作区的共用者会话：
/// 跨工作区统一计算，避免会话迁移后旧工作区里的附件被误判为无引用；
/// 同一目录被多用户共用时，也不会删掉他人仍在引用的附件。
/// </para>
/// </remarks>
public static class WebWorkspaceCacheService
{
    /// <summary>缺省保留天数。</summary>
    private const int DefaultKeepDays = 7;

    /// <summary>扫描工作区缓存占用。</summary>
    /// <returns>各工作区分类统计。</returns>
    public static Task<ActionResult> ScanAsync() => WebApi.RunGuardedAsync(async () =>
    {
        var keepDays = NormalizeKeepDays(WebApi.QueryInt("keepDays"));

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var workspaces = (await scope.GetRequiredService<IWorkspaceManager>()
                .GetUserWorkspacesAsync()
                .ConfigureAwait(false))
            .ToList();

        var referenced = await GetReferencedPathsAsync(
                scope,
                workspaces.Select(workspace => workspace.WorkspaceId))
            .ConfigureAwait(false);

        var items = new List<WorkspaceCacheItemDto>(workspaces.Count);
        foreach (var workspace in workspaces)
        {
            items.Add(ScanWorkspace(workspace, keepDays, referenced));
        }

        return WebApi.OkJson(new WorkspaceCacheScanDto(
            keepDays,
            items,
            items.Sum(item => item.TempBytes + item.UploadBytes),
            items.Sum(item => item.ReclaimableBytes)));
    });

    /// <summary>清理工作区缓存。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceCacheCleanRequest"/> JSON。</param>
    /// <returns>清理结果。</returns>
    public static Task<ActionResult> CleanAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<WorkspaceCacheCleanRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体无效");
        }

        if (!input.AllWorkspaces && string.IsNullOrWhiteSpace(input.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        if (!input.IncludeTempFiles && !input.IncludeOrphanUploads && !input.IncludeReferencedUploads)
        {
            return WebApi.FailJson(400, "至少选择一项清理内容");
        }

        var keepDays = NormalizeKeepDays(input.KeepDays);

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var workspaces = (await scope.GetRequiredService<IWorkspaceManager>()
                .GetUserWorkspacesAsync()
                .ConfigureAwait(false))
            .ToList();

        List<WorkspaceInfo> targets;
        if (input.AllWorkspaces)
        {
            targets = workspaces;
        }
        else
        {
            var target = workspaces.FirstOrDefault(item =>
                string.Equals(item.WorkspaceId, input.WorkspaceId!.Trim(), StringComparison.Ordinal));
            if (target is null)
            {
                return WebApi.FailJson(404, "工作区不存在");
            }

            targets = [target];
        }

        if (targets.Count == 0)
        {
            return WebApi.OkJson(new WorkspaceCacheCleanDto(0, 0, 0, 0));
        }

        // 清理期间若有轮次在跑，它可能正在写本轮附件：整体拒绝，不做部分删除。
        // 工作区可被多个用户共用登记，同一份 temp 由各用户的会话共享，故按工作区取全部会话（不按用户过滤）。
        var sharedQuery = scope.GetRequiredService<IWorkspaceSharedSessionQuery>();
        var activeSessionIds = await sharedQuery
            .GetAllSessionIdsByWorkspacesAsync(targets.Select(target => target.WorkspaceId).ToList())
            .ConfigureAwait(false);
        if (WebApi.Runtime.Runs.IsAnyRunning(activeSessionIds))
        {
            return input.AllWorkspaces
                ? WebApi.FailJson(409, "有会话正在进行中，请先停止")
                : WebApi.FailJson(409, "该工作区下有正在进行中的会话，请先停止");
        }

        var referenced = await GetReferencedPathsAsync(
                scope,
                targets.Select(target => target.WorkspaceId))
            .ConfigureAwait(false);

        long freedBytes = 0;
        int freedFiles = 0, failedFiles = 0;
        foreach (var target in targets)
        {
            var tempPath = ResolveTempPath(target);
            if (tempPath is null) continue;

            var result = WorkspaceCacheScanner.Clean(
                tempPath,
                keepDays,
                referenced,
                input.IncludeTempFiles,
                input.IncludeOrphanUploads,
                input.IncludeReferencedUploads);
            freedBytes += result.FreedBytes;
            freedFiles += result.FreedFiles;
            failedFiles += result.FailedFiles;
        }

        return WebApi.OkJson(new WorkspaceCacheCleanDto(freedBytes, freedFiles, failedFiles, targets.Count));
    });

    /// <summary>扫描单个工作区。</summary>
    private static WorkspaceCacheItemDto ScanWorkspace(
        WorkspaceInfo workspace,
        int keepDays,
        IReadOnlyCollection<string> referenced)
    {
        var tempPath = ResolveTempPath(workspace);
        if (tempPath is null)
        {
            return new WorkspaceCacheItemDto(
                workspace.WorkspaceId, workspace.Name, workspace.RootPath, workspace.Type, "",
                false, "工作区根目录未设置", 0, 0, 0, 0, 0, 0, 0, 0);
        }

        try
        {
            var exists = Directory.Exists(tempPath);
            var result = WorkspaceCacheScanner.Scan(tempPath, keepDays, referenced);
            return new WorkspaceCacheItemDto(
                workspace.WorkspaceId,
                workspace.Name,
                workspace.RootPath,
                workspace.Type,
                tempPath,
                exists,
                null,
                result.TempBytes,
                result.TempFiles,
                result.UploadBytes,
                result.UploadFiles,
                result.ReferencedBytes,
                result.ReferencedFiles,
                result.ReclaimableBytes,
                result.ReclaimableFiles);
        }
        catch (Exception ex)
        {
            return new WorkspaceCacheItemDto(
                workspace.WorkspaceId, workspace.Name, workspace.RootPath, workspace.Type, tempPath,
                false, WebApi.SafeErrorMessage(ex), 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    /// <summary>取仍被历史消息引用的附件路径（源文件与处理后副本）。</summary>
    /// <remarks>
    /// 引用集合由两部分组成：当前用户的全部会话（跨工作区，兼容会话迁移），以及绑定到目标工作区的
    /// 全部会话（不按用户过滤——同一目录可被多个用户共用登记）。两者都可能引用同一物理目录下的附件，
    /// 少算任何一方都会把仍在使用的附件误判为无引用。
    /// </remarks>
    private static async Task<IReadOnlyCollection<string>> GetReferencedPathsAsync(
        DiContainer scope,
        IEnumerable<string> workspaceIds)
    {
        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        var sessions = await scope.GetRequiredService<ISessionManager>()
            .GetUserSessionsAsync()
            .ConfigureAwait(false);
        foreach (var session in sessions)
        {
            sessionIds.Add(session.SessionId);
        }

        var workspaceIdList = workspaceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (workspaceIdList.Count > 0)
        {
            var sharedSessionIds = await scope.GetRequiredService<IWorkspaceSharedSessionQuery>()
                .GetAllSessionIdsByWorkspacesAsync(workspaceIdList)
                .ConfigureAwait(false);
            foreach (var sessionId in sharedSessionIds)
            {
                sessionIds.Add(sessionId);
            }
        }

        if (sessionIds.Count == 0) return Array.Empty<string>();

        var payloads = await scope.GetRequiredService<ISessionAttachmentQuery>()
            .GetAttachmentsJsonAsync(sessionIds)
            .ConfigureAwait(false);
        if (payloads.Count == 0) return Array.Empty<string>();

        var paths = new List<string>();
        foreach (var json in payloads)
        {
            List<AttachmentRecord>? records;
            try
            {
                records = JsonSerializer.Deserialize<List<AttachmentRecord>>(json);
            }
            catch (JsonException)
            {
                // 历史脏数据：跳过这条消息，不影响其它附件的引用判定
                continue;
            }

            if (records is null) continue;
            foreach (var record in records)
            {
                if (!string.IsNullOrWhiteSpace(record.SourcePath)) paths.Add(record.SourcePath);
                if (!string.IsNullOrWhiteSpace(record.ProcessedPath)) paths.Add(record.ProcessedPath!);
            }
        }

        return paths;
    }

    /// <summary>解析工作区的 temp 目录；根目录为空时返回 null。</summary>
    private static string? ResolveTempPath(WorkspaceInfo workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.RootPath)) return null;

        var configPath = string.IsNullOrWhiteSpace(workspace.ConfigPath) ? ".luban-agent" : workspace.ConfigPath!;
        return Path.GetFullPath(Path.Combine(workspace.RootPath, configPath, "temp"));
    }

    /// <summary>规范化保留天数：只接受 0 / 1 / 7 / 30，其余回落到缺省 7 天。</summary>
    private static int NormalizeKeepDays(int? keepDays)
        => keepDays is 0 or 1 or 7 or 30 ? keepDays.Value : DefaultKeepDays;
}