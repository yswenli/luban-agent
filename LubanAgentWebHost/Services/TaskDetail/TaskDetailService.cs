/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.TaskDetail
*文件名： TaskDetailService
*描述：任务详情聚合、评论读写与工作区会话首轮上下文装配
*****************************************************************************/

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LubanAgentCore.Connectors.AliyunDevOps;
using LubanAgentWebHost.Services.Board;
using LubanAgentWebHost.Services.Init;
using LubanAgentWebHost.Services.Prd;

namespace LubanAgentWebHost.Services.TaskDetail;

/// <summary>任务详情读取与评论服务。</summary>
public sealed class TaskDetailService
{
    private const int MaxSeedComments = 30;
    private static readonly string[] DueDateKeywords = ["预计完成", "计划完成", "预计结束", "截止"];

    private readonly IProjexClient _projex;
    private readonly IYunxiaoSettingsService _settings;
    private readonly ProjectInitStore _projectInitStore;
    private readonly InitTaskStore _initTaskStore;
    private readonly IWorkspaceDirectoryProvider _workspaces;

    /// <summary>创建任务详情服务。</summary>
    public TaskDetailService(
        IProjexClient projex,
        IYunxiaoSettingsService settings,
        ProjectInitStore projectInitStore,
        InitTaskStore initTaskStore,
        IWorkspaceDirectoryProvider workspaces)
    {
        _projex = projex ?? throw new ArgumentNullException(nameof(projex));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _projectInitStore = projectInitStore ?? throw new ArgumentNullException(nameof(projectInitStore));
        _initTaskStore = initTaskStore ?? throw new ArgumentNullException(nameof(initTaskStore));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
    }

    /// <summary>读取任务、父需求、迭代、评论与初始化登记。</summary>
    public async Task<YunxiaoWorkbenchResult<TaskDetailResult>> GetDetailAsync(
        TaskDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null || string.IsNullOrWhiteSpace(query.ProjectId) || string.IsNullOrWhiteSpace(query.WorkitemId))
            return YunxiaoWorkbenchResult<TaskDetailResult>.Failure(400, "项目 ID 与工作项 ID 不能为空");

        var orgId = _settings.GetOrganizationId();
        if (string.IsNullOrWhiteSpace(orgId))
            return YunxiaoWorkbenchResult<TaskDetailResult>.Failure(400, "未配置组织 ID");

        var projectId = query.ProjectId.Trim();
        var workitemId = query.WorkitemId.Trim();
        var workitem = await _projex.GetWorkitemAsync(orgId, workitemId, cancellationToken).ConfigureAwait(false);
        if (workitem is null)
            return YunxiaoWorkbenchResult<TaskDetailResult>.Failure(404, "任务不存在");

        var result = new TaskDetailResult
        {
            ProjectId = projectId,
            WorkitemId = workitemId,
            SerialNumber = workitem.SerialNumber,
            Title = string.IsNullOrWhiteSpace(workitem.Subject) ? "(无标题)" : workitem.Subject!.Trim(),
            Description = NormalizeText(workitem.Description, workitem.FormatType),
            Acceptance = NormalizeText(PrdSyncService.ExtractAcceptance(workitem.CustomFieldValues), "RICHTEXT"),
            Assignee = workitem.AssignedTo?.Name,
            DueDate = ExtractDueDate(workitem.CustomFieldValues),
            StatusId = workitem.Status?.Id,
            StatusName = string.IsNullOrWhiteSpace(workitem.Status?.DisplayName)
                ? workitem.Status?.Name
                : workitem.Status.DisplayName,
            StatusNameEn = workitem.Status?.NameEn,
            CanStart = CanStart(workitem.Status?.NameEn),
            SprintId = workitem.Sprint?.Id,
            SprintName = workitem.Sprint?.Name
        };

        await FillParentAsync(orgId, workitem.ParentId, result, cancellationToken).ConfigureAwait(false);
        await FillSprintAsync(orgId, projectId, workitem.Sprint?.Id, result, cancellationToken).ConfigureAwait(false);
        await FillCodeContextAsync(projectId, result).ConfigureAwait(false);

        try
        {
            var comments = await _projex.ListWorkitemCommentsAsync(orgId, workitemId, cancellationToken).ConfigureAwait(false);
            result.Comments = MapComments(comments);
            result.CommentCount = result.Comments.Count;
        }
        catch (Exception ex)
        {
            result.CommentError = $"评论暂不可用：{ex.Message}";
        }

        result.SeedPrompt = BuildSeedPrompt(result);
        return YunxiaoWorkbenchResult<TaskDetailResult>.Success(result);
    }

    /// <summary>创建一条 Markdown 评论。</summary>
    public async Task<YunxiaoWorkbenchResult<string?>> CreateCommentAsync(
        TaskCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.WorkitemId))
            return YunxiaoWorkbenchResult<string?>.Failure(400, "项目 ID 与工作项 ID 不能为空");
        if (string.IsNullOrWhiteSpace(request.Content))
            return YunxiaoWorkbenchResult<string?>.Failure(400, "评论正文不能为空");

        var orgId = _settings.GetOrganizationId();
        if (string.IsNullOrWhiteSpace(orgId))
            return YunxiaoWorkbenchResult<string?>.Failure(400, "未配置组织 ID");

        var id = await _projex.CreateWorkitemCommentAsync(
            orgId,
            request.WorkitemId.Trim(),
            request.Content.Trim(),
            string.IsNullOrWhiteSpace(request.ParentId) ? null : request.ParentId.Trim(),
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(id)
            ? YunxiaoWorkbenchResult<string?>.Failure(502, "云效未返回评论 ID")
            : YunxiaoWorkbenchResult<string?>.Success(id);
    }

    /// <summary>是否显示开始任务主操作。</summary>
    internal static bool CanStart(string? statusNameEn)
        => statusNameEn is not null
            && (statusNameEn.Equals("TODO", StringComparison.OrdinalIgnoreCase)
                || statusNameEn.Equals("DOING", StringComparison.OrdinalIgnoreCase));

    /// <summary>富文本降级为纯文本，保留段落、换行与列表行。</summary>
    internal static string ToPlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("htmlValue", out var htmlValue)
                && htmlValue.ValueKind == JsonValueKind.String)
            {
                text = htmlValue.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // 非 JSON 内容继续按原有纯文本或 HTML 处理。
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(?:p|div|section|h[1-6]|li|tr|blockquote)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li(?:\s[^>]*)?>", "- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text).Replace('\u00A0', ' ');
        text = string.Join("\n", text.Split('\n').Select(line => line.Trim()));
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    /// <summary>把评论映射为倒序展示项，并标记最新一条。</summary>
    internal static List<TaskCommentItem> MapComments(IEnumerable<YunxiaoWorkitemComment> comments)
    {
        var mapped = comments
            .OrderByDescending(c => c.GmtCreate ?? long.MinValue)
            .ThenByDescending(c => c.GmtModified ?? long.MinValue)
            .Select(c => new TaskCommentItem
            {
                Id = c.Id ?? string.Empty,
                Author = string.IsNullOrWhiteSpace(c.User?.Name) ? "未知用户" : c.User!.Name!,
                Content = NormalizeText(c.Content, c.ContentFormat),
                CreatedAt = c.GmtCreate.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(c.GmtCreate.Value).ToLocalTime()
                    : null
            })
            .ToList();
        if (mapped.Count > 0)
            mapped[0].IsLatest = true;
        return mapped;
    }

    /// <summary>装配首轮输入；展示评论倒序，发给 Agent 时恢复时间正序。</summary>
    internal static string BuildSeedPrompt(TaskDetailResult detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var builder = new StringBuilder();
        builder.AppendLine("请先使用 UseSkill 加载 brainstorming 技能，再基于以下任务上下文给出方案。当前以 plan 模式启动；在我明确确认方案之前，不写代码、不创建项目骨架、不调用实现类技能。");
        builder.AppendLine();
        builder.AppendLine("# 任务");
        builder.Append("任务 ").Append(detail.SerialNumber ?? detail.WorkitemId).Append(' ').AppendLine(detail.Title);
        builder.AppendLine("描述：");
        builder.AppendLine(string.IsNullOrWhiteSpace(detail.Description) ? "（未填写）" : detail.Description);
        builder.AppendLine("验收标准：");
        builder.AppendLine(string.IsNullOrWhiteSpace(detail.Acceptance) ? "（未填写）" : detail.Acceptance);
        if (detail.ParentRequirement is not null)
            builder.AppendLine("所属需求：" + detail.ParentRequirement.DisplayName);

        builder.AppendLine();
        builder.AppendLine("# 评论");
        if (!string.IsNullOrWhiteSpace(detail.CommentError))
        {
            builder.AppendLine("评论暂不可用");
            detail.SeedCommentCount = 0;
        }
        else
        {
            var seed = detail.Comments
                .OrderByDescending(c => c.CreatedAt ?? DateTimeOffset.MinValue)
                .Take(MaxSeedComments)
                .OrderBy(c => c.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();
            detail.SeedCommentCount = seed.Count;
            if (seed.Count == 0)
                builder.AppendLine("（无评论）");
            foreach (var comment in seed)
            {
                builder.Append(comment.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "时间未知")
                    .Append(' ').AppendLine(comment.Author);
                builder.AppendLine(comment.Content);
            }
            var omitted = detail.Comments.Count - seed.Count;
            if (omitted > 0)
                builder.AppendLine($"更早评论已省略 {omitted} 条");
        }

        builder.AppendLine();
        builder.AppendLine("# 工程上下文");
        builder.AppendLine("项目：" + detail.ProjectId);
        builder.AppendLine("迭代：" + (detail.SprintName ?? detail.SprintId ?? "未登记"));
        var code = detail.CodeContext;
        if (code is null)
        {
            builder.AppendLine("代码库：未登记");
        }
        else
        {
            builder.AppendLine("代码库：" + (code.RepoName ?? "未登记"));
            builder.AppendLine("目标分支：" + (code.Branch ?? "未登记"));
            if (!string.IsNullOrWhiteSpace(code.WorkspaceRoot))
                builder.AppendLine("目标工作区：" + code.WorkspaceRoot);
            foreach (var end in code.Ends)
            {
                var dir = string.IsNullOrWhiteSpace(end.Dir)
                    ? "目录未登记"
                    : JoinRepoDir(code.RepoName, end.Dir);
                builder.AppendLine($"{end.End}：{dir}；流水线：{end.PipelineName ?? "未登记"}");
            }
        }
        return builder.ToString().TrimEnd();
    }

    private static string JoinRepoDir(string? repoName, string dir)
    {
        var normalized = dir.Trim().Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(repoName) ? normalized : repoName.Trim().Trim('/', '\\') + "/" + normalized;
    }

    private static string? NormalizeText(string? value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return string.Equals(format, "RICHTEXT", StringComparison.OrdinalIgnoreCase)
            ? ToPlainText(value)
            : value.Trim();
    }

    private static string? ExtractDueDate(IReadOnlyList<YunxiaoCustomFieldValue>? fields)
    {
        var field = fields?.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.FieldName)
            && DueDateKeywords.Any(keyword => f.FieldName!.Contains(keyword, StringComparison.Ordinal)));
        var raw = field?.Values?
            .Select(v => string.IsNullOrWhiteSpace(v.DisplayValue) ? v.Identifier : v.DisplayValue)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return BoardService.ParseDueDate(raw);
    }

    private async Task FillParentAsync(
        string orgId, string? parentId, TaskDetailResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parentId))
            return;
        try
        {
            var parent = await _projex.GetWorkitemAsync(orgId, parentId.Trim(), cancellationToken).ConfigureAwait(false);
            if (parent is not null)
            {
                result.ParentRequirement = new TaskParentRequirement
                {
                    Id = parent.Id ?? parentId.Trim(),
                    SerialNumber = parent.SerialNumber,
                    Title = string.IsNullOrWhiteSpace(parent.Subject) ? "(无标题)" : parent.Subject!.Trim()
                };
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"读取所属需求失败：{ex.Message}");
        }
    }

    private async Task FillSprintAsync(
        string orgId, string projectId, string? sprintId, TaskDetailResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sprintId))
            return;
        try
        {
            var sprint = await _projex.GetSprintAsync(orgId, projectId, sprintId.Trim(), cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sprint?.Name))
                result.SprintName = sprint.Name;
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"读取所属迭代失败：{ex.Message}");
        }
    }

    private async Task FillCodeContextAsync(string projectId, TaskDetailResult result)
    {
        var record = await _projectInitStore.FindAsync(projectId, null).ConfigureAwait(false);
        if (record is null)
            return;

        var workspaceId = string.IsNullOrWhiteSpace(record.WorkspaceId) ? null : record.WorkspaceId.Trim();
        if (workspaceId is null && !string.IsNullOrWhiteSpace(record.Codename))
        {
            var tasks = await _initTaskStore.ListByCodenameAsync(record.Codename).ConfigureAwait(false);
            workspaceId = tasks.LastOrDefault(t => !string.IsNullOrWhiteSpace(t.WorkspaceId))?.WorkspaceId?.Trim();
        }

        var workspaceRoot = workspaceId is null
            ? null
            : _workspaces.List().FirstOrDefault(w => string.Equals(w.Id, workspaceId, StringComparison.Ordinal))?.RootPath;
        result.CodeContext = new TaskCodeContext
        {
            WorkspaceId = workspaceId,
            WorkspaceRoot = workspaceRoot,
            RepoName = record.RepoName,
            Branch = record.Branch,
            Ends = record.Pipelines.Select(p => new TaskCodeEnd
            {
                End = p.End,
                Dir = p.Dir,
                PipelineName = p.Name
            }).ToList()
        };
    }
}
