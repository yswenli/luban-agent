/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Board
*文件名： BoardService
*描述：迭代任务看板（F1）：迭代列表 + 迭代内任务 + 动态状态列 + 预计完成时间排序
*
*****************************************************************************/

using System.Globalization;
using LubanAgentCore.Connectors.AliyunDevOps;
using LubanAgentWebHost.Services.Prd;

namespace LubanAgentWebHost.Services.Board;

/// <summary>
/// 迭代任务看板读取服务（只读）。
/// </summary>
/// <remarks>
/// <para>
/// 状态列一律走「实例反查」：云效未暴露可用的状态枚举接口，<c>workitemTypes/{id}/workflows</c>
/// 实测 <c>400 get statusFlowList failed</c>，故复用 PRD 侧已有的工作项实例反查；
/// 未被任何工作项使用的状态取不到，属已知限制。
/// </para>
/// <para>
/// 预计完成时间字段名动态解析：不写死 <c>dueDate</c>，而是逐条取任务详情的
/// <c>customFieldValues</c>，按字段名关键词（预计完成 / 计划完成 / 预计结束 / 截止）匹配；
/// 解析不到时 <see cref="BoardResult.DueDateResolved"/> 为 false，页面据此提示而不是静默留空。
/// </para>
/// </remarks>
public sealed class BoardService
{
    /// <summary>任务分页上限（1–200，云效限制）。</summary>
    private const int TaskPageSize = 200;

    /// <summary>任务最多拉取页数，防止超大迭代把看板拖慢。</summary>
    private const int MaxTaskPages = 3;

    /// <summary>逐条取详情解析字段的上限（超出部分 DueDate 为空并告警）。</summary>
    private const int MaxDetailProbes = 60;

    /// <summary>预计完成时间字段名关键词。</summary>
    private static readonly string[] DueDateFieldKeywords = ["预计完成", "计划完成", "预计结束", "截止"];

    private readonly IProjexClient _projex;
    private readonly IYunxiaoSettingsService _settings;
    private readonly PrdSyncService _sync;

    /// <summary>
    /// 创建看板服务。
    /// </summary>
    /// <param name="projex">云效研发项目域客户端。</param>
    /// <param name="settings">云效设置（取组织 ID）。</param>
    /// <param name="sync">PRD 同步服务（复用状态反查）。</param>
    public BoardService(IProjexClient projex, IYunxiaoSettingsService settings, PrdSyncService sync)
    {
        _projex = projex;
        _settings = settings;
        _sync = sync;
    }

    /// <summary>
    /// 读取看板数据。
    /// </summary>
    /// <param name="query">查询条件（项目 + 可选迭代）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>看板结果；缺项目 / 组织时返回 400。</returns>
    public async Task<YunxiaoWorkbenchResult<BoardResult>> GetBoardAsync(
        BoardQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.ProjectId))
            return YunxiaoWorkbenchResult<BoardResult>.Failure(400, "项目 ID 不能为空");

        var orgId = _settings.GetOrganizationId();
        if (string.IsNullOrWhiteSpace(orgId))
            return YunxiaoWorkbenchResult<BoardResult>.Failure(400, "未配置组织 ID");

        var result = new BoardResult
        {
            ProjectId = query.ProjectId.Trim(),
            ProjectName = query.ProjectId.Trim()
        };

        try
        {
            var project = await _projex.GetProjectAsync(orgId, result.ProjectId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(project?.Name))
                result.ProjectName = project!.Name!;
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"读取项目名称失败：{ex.Message}");
        }

        try
        {
            var sprints = await _projex
                .ListSprintsAsync(orgId, result.ProjectId, null, 1, 100, cancellationToken)
                .ConfigureAwait(false);
            result.Sprints = OrderSprints(sprints);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"读取迭代列表失败：{ex.Message}");
        }

        var current = PickSprint(result.Sprints, query.SprintId);
        if (current is not null)
        {
            result.SprintId = current.Id;
            result.SprintName = current.Name;
            result.SprintStartDate = current.StartDate;
            result.SprintEndDate = current.EndDate;
        }

        var tasks = new List<YunxiaoWorkitem>();
        if (current is not null)
        {
            try
            {
                tasks = await LoadTasksAsync(orgId, result.ProjectId, current.Id, result.Warnings, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"读取迭代任务失败：{ex.Message}");
            }
        }

        var dueDates = await ResolveDueDatesAsync(orgId, tasks, result, cancellationToken).ConfigureAwait(false);
        var items = tasks
            .Select(t => ToTaskItem(t, dueDates))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        result.Columns = await BuildColumnsAsync(orgId, result.ProjectId, items, cancellationToken).ConfigureAwait(false);
        result.TotalTasks = items.Count;
        result.DoneCount = items.Count(t => result.Columns.Any(c => c.StatusId == t.StatusId && c.IsDone));
        result.OverdueCount = items.Count(t => t.Overdue);

        return YunxiaoWorkbenchResult<BoardResult>.Success(result);
    }

    /// <summary>
    /// 迭代排序：进行中优先，其次开始日期倒序，无开始日期置末。
    /// </summary>
    /// <param name="sprints">接口返回的迭代。</param>
    /// <returns>下拉选项。</returns>
    internal static List<BoardSprintOption> OrderSprints(IEnumerable<YunxiaoSprint> sprints)
        => sprints
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .Select(s => new BoardSprintOption
            {
                Id = s.Id!,
                Name = string.IsNullOrWhiteSpace(s.Name) ? s.Id! : s.Name!,
                Status = s.Status,
                StartDate = ToDateText(s.StartDate),
                EndDate = ToDateText(s.EndDate)
            })
            .OrderByDescending(s => string.Equals(s.Status, "DOING", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => s.StartDate ?? string.Empty, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 选当前迭代：请求指定优先（命中才用），否则第一个进行中的，再否则第一个。
    /// </summary>
    /// <param name="sprints">已排序迭代。</param>
    /// <param name="requested">请求指定迭代 ID。</param>
    /// <returns>当前迭代；列表为空时为 <c>null</c>。</returns>
    internal static BoardSprintOption? PickSprint(IReadOnlyList<BoardSprintOption> sprints, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var hit = sprints.FirstOrDefault(s => s.Id == requested.Trim());
            if (hit is not null)
                return hit;
        }

        return sprints.FirstOrDefault(s => string.Equals(s.Status, "DOING", StringComparison.OrdinalIgnoreCase))
            ?? sprints.FirstOrDefault();
    }

    /// <summary>
    /// 列内排序：预计完成时间升序、空值置末；日期相同按编号升序保证稳定。
    /// </summary>
    /// <param name="tasks">列内任务。</param>
    /// <returns>排序后的新列表。</returns>
    internal static List<BoardTaskItem> SortTasks(IEnumerable<BoardTaskItem> tasks)
        => tasks
            .OrderBy(t => string.IsNullOrWhiteSpace(t.DueDate) ? 1 : 0)
            .ThenBy(t => t.DueDate ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.SerialNumber ?? string.Empty, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 完成态判定（启发式：状态英文名 / 名称命中完成、关闭、归档类词）。
    /// </summary>
    /// <param name="nameEn">状态英文名。</param>
    /// <param name="name">状态名称。</param>
    /// <returns>完成态为 <c>true</c>。</returns>
    internal static bool IsDoneStatus(string? nameEn, string? name)
    {
        var en = (nameEn ?? string.Empty).Trim().ToUpperInvariant();
        if (en is "DONE" or "CLOSED" or "COMPLETED")
            return true;

        var text = name ?? string.Empty;
        return text.Contains("已完成", StringComparison.Ordinal)
            || text.Contains("已关闭", StringComparison.Ordinal)
            || text.Contains("已归档", StringComparison.Ordinal);
    }

    /// <summary>解析字段展示值为日期文本（支持 <c>yyyy-MM-dd</c>、含时间后缀、epoch 毫秒）。</summary>
    /// <param name="raw">字段展示值。</param>
    /// <returns><c>yyyy-MM-dd</c>；解析失败为空。</returns>
    internal static string? ParseDueDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch)
            && epoch > 1_000_000_000_000)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epoch).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return text.Length >= 10 ? text[..10] : null;
    }

    /// <summary>日期字段统一为 <c>yyyy-MM-dd</c>（接口既可能给日期串，也可能给 epoch 毫秒）。</summary>
    private static string? ToDateText(string? raw) => ParseDueDate(raw);

    private async Task<List<YunxiaoWorkitem>> LoadTasksAsync(
        string orgId,
        string projectId,
        string sprintId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var all = new List<YunxiaoWorkitem>();

        for (var page = 1; page <= MaxTaskPages; page++)
        {
            var paged = await _projex.SearchWorkitemsAsync(
                orgId,
                new YunxiaoWorkitemSearchCriteria
                {
                    ProjectId = projectId,
                    Category = "Task",
                    SprintId = sprintId,
                    OrderBy = "gmtCreate",
                    Sort = "asc",
                    Page = page,
                    PerPage = TaskPageSize
                },
                cancellationToken).ConfigureAwait(false);

            var items = paged?.Items ?? [];
            all.AddRange(items);

            if (items.Count < TaskPageSize)
                break;

            if (page == MaxTaskPages)
                warnings.Add($"任务超过 {MaxTaskPages * TaskPageSize} 条，只展示前 {MaxTaskPages * TaskPageSize} 条");
        }

        return all;
    }

    /// <summary>逐条取详情，动态解析「预计完成时间」字段名与取值。</summary>
    private async Task<Dictionary<string, string?>> ResolveDueDatesAsync(
        string orgId,
        IReadOnlyList<YunxiaoWorkitem> tasks,
        BoardResult result,
        CancellationToken cancellationToken)
    {
        var dueDates = new Dictionary<string, string?>(StringComparer.Ordinal);

        var probes = tasks.Where(t => !string.IsNullOrWhiteSpace(t.Id)).Take(MaxDetailProbes).ToList();
        if (tasks.Count > probes.Count)
            result.Warnings.Add($"预计完成时间只解析前 {probes.Count} 条任务，其余显示为空");

        foreach (var task in probes)
        {
            try
            {
                var detail = await _projex.GetWorkitemAsync(orgId, task.Id!, cancellationToken).ConfigureAwait(false);
                var field = detail?.CustomFieldValues?
                    .FirstOrDefault(f => IsDueDateField(f.FieldName));
                if (field is null)
                    continue;

                result.DueDateFieldName ??= field.FieldName;
                var raw = field.Values?
                    .Select(v => v.DisplayValue ?? v.Identifier)
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                dueDates[task.Id!] = ParseDueDate(raw);
            }
            catch
            {
                // 单条详情失败只影响该任务的预计完成时间，不中断看板
            }
        }

        result.DueDateResolved = result.DueDateFieldName is not null;
        if (probes.Count > 0 && !result.DueDateResolved)
            result.Warnings.Add("任务未填写「预计完成时间」（或该项目无此字段），列内按编号排序");

        return dueDates;
    }

    private static bool IsDueDateField(string? fieldName)
        => !string.IsNullOrWhiteSpace(fieldName)
            && DueDateFieldKeywords.Any(k => fieldName.Contains(k, StringComparison.Ordinal));

    private static BoardTaskItem? ToTaskItem(YunxiaoWorkitem workitem, IReadOnlyDictionary<string, string?> dueDates)
    {
        if (string.IsNullOrWhiteSpace(workitem.Id))
            return null;

        dueDates.TryGetValue(workitem.Id!, out var dueDate);

        return new BoardTaskItem
        {
            Id = workitem.Id!,
            SerialNumber = workitem.SerialNumber,
            Title = string.IsNullOrWhiteSpace(workitem.Subject) ? "(无标题)" : workitem.Subject!,
            StatusId = workitem.Status?.Id,
            StatusName = workitem.Status?.Name,
            Assignee = workitem.AssignedTo?.Name,
            DueDate = dueDate,
            Overdue = dueDate is not null
                && string.CompareOrdinal(dueDate, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0
        };
    }

    /// <summary>按实例反查的状态生成列，并把任务分到对应列。</summary>
    private async Task<List<BoardColumn>> BuildColumnsAsync(
        string orgId,
        string projectId,
        IReadOnlyList<BoardTaskItem> tasks,
        CancellationToken cancellationToken)
    {
        var options = await _sync.ListStatusOptionsAsync(orgId, projectId, cancellationToken).ConfigureAwait(false);
        var statusOptions = options
            .Where(o => string.Equals(o.Category, "Task", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var columns = statusOptions
            .Select(o => new BoardColumn
            {
                StatusId = o.Id,
                StatusName = o.Name,
                IsDone = IsDoneStatus(o.NameEn, o.Name)
            })
            .ToList();

        // 任务上出现但反查漏掉的状态（如翻页未覆盖）补列，保证每条任务可见
        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.StatusId) || columns.Any(c => c.StatusId == task.StatusId))
                continue;

            columns.Add(new BoardColumn
            {
                StatusId = task.StatusId!,
                StatusName = string.IsNullOrWhiteSpace(task.StatusName) ? task.StatusId! : task.StatusName!,
                IsDone = IsDoneStatus(null, task.StatusName)
            });
        }

        foreach (var task in tasks)
        {
            var column = columns.FirstOrDefault(c => c.StatusId == task.StatusId);
            if (column is not null)
                column.Tasks.Add(task);
        }

        foreach (var column in columns)
        {
            column.Tasks = SortTasks(column.Tasks);
            // 逾期判定要用完成态：完成列里的任务不算逾期
            if (column.IsDone)
                foreach (var task in column.Tasks)
                    task.Overdue = false;
        }

        return columns;
    }
}
