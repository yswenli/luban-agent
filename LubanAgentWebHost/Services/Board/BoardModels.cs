/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Board
*文件名： BoardModels
*描述：迭代任务看板（F1）的请求 / 响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Services.Board;

/// <summary>看板请求（GET /yunxiao/board 的查询参数）。</summary>
public sealed class BoardQuery
{
    /// <summary>项目 ID（必填）。</summary>
    public string? ProjectId { get; set; }

    /// <summary>迭代 ID（可空：不传时取进行中的迭代，没有则取最近一个）。</summary>
    public string? SprintId { get; set; }
}

/// <summary>迭代下拉项。</summary>
public sealed class BoardSprintOption
{
    /// <summary>迭代 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>迭代名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>迭代状态：<c>TODO</c> / <c>DOING</c> / <c>ARCHIVED</c>。</summary>
    public string? Status { get; set; }

    /// <summary>开始日期（<c>yyyy-MM-dd</c>，可空）。</summary>
    public string? StartDate { get; set; }

    /// <summary>结束日期（<c>yyyy-MM-dd</c>，可空）。</summary>
    public string? EndDate { get; set; }
}

/// <summary>看板任务卡。</summary>
public sealed class BoardTaskItem
{
    /// <summary>工作项 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>编号（如 <c>MGQH-10231</c>）。</summary>
    public string? SerialNumber { get; set; }

    /// <summary>标题（完整值；前端做两行截断 + 悬停全文）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>状态 ID。</summary>
    public string? StatusId { get; set; }

    /// <summary>状态名称。</summary>
    public string? StatusName { get; set; }

    /// <summary>负责人显示名（未指派时为空）。</summary>
    public string? Assignee { get; set; }

    /// <summary>预计完成日期（<c>yyyy-MM-dd</c>，未取到时为空）。</summary>
    public string? DueDate { get; set; }

    /// <summary>是否逾期（预计完成日期早于今天且未完成）。</summary>
    public bool Overdue { get; set; }
}

/// <summary>看板状态列。</summary>
public sealed class BoardColumn
{
    /// <summary>状态 ID。</summary>
    public string StatusId { get; set; } = string.Empty;

    /// <summary>状态名称。</summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary>是否完成态（按状态名 / 英文名启发式判定，用于进度与逾期统计）。</summary>
    public bool IsDone { get; set; }

    /// <summary>列内任务（按预计完成日期升序，空值置末）。</summary>
    public List<BoardTaskItem> Tasks { get; set; } = [];
}

/// <summary>看板结果。</summary>
public sealed class BoardResult
{
    /// <summary>项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>项目名称。</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>迭代下拉选项（进行中优先，其次按开始日期倒序）。</summary>
    public List<BoardSprintOption> Sprints { get; set; } = [];

    /// <summary>当前迭代 ID（无迭代时为空）。</summary>
    public string? SprintId { get; set; }

    /// <summary>当前迭代名称。</summary>
    public string? SprintName { get; set; }

    /// <summary>当前迭代开始日期。</summary>
    public string? SprintStartDate { get; set; }

    /// <summary>当前迭代结束日期。</summary>
    public string? SprintEndDate { get; set; }

    /// <summary>状态列（列名 / 列序来自云效实例反查，不硬编码）。</summary>
    public List<BoardColumn> Columns { get; set; } = [];

    /// <summary>任务总数。</summary>
    public int TotalTasks { get; set; }

    /// <summary>已完成任务数。</summary>
    public int DoneCount { get; set; }

    /// <summary>逾期任务数。</summary>
    public int OverdueCount { get; set; }

    /// <summary>预计完成时间字段名（动态解析所得；未解析到时为空）。</summary>
    public string? DueDateFieldName { get; set; }

    /// <summary>是否成功解析到预计完成时间字段（false 时所有任务 DueDate 为空）。</summary>
    public bool DueDateResolved { get; set; }

    /// <summary>告警（非致命，如详情解析条数被截断）。</summary>
    public List<string> Warnings { get; set; } = [];
}
