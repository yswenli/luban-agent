/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.TaskDetail
*文件名： TaskDetailModels
*描述：任务详情、评论与工程上下文模型
*****************************************************************************/

namespace LubanAgentWebHost.Services.TaskDetail;

/// <summary>任务详情查询。</summary>
public sealed class TaskDetailQuery
{
    /// <summary>云效项目 ID。</summary>
    public string? ProjectId { get; set; }

    /// <summary>云效工作项 ID。</summary>
    public string? WorkitemId { get; set; }
}

/// <summary>创建任务评论请求。</summary>
public sealed class TaskCommentRequest
{
    /// <summary>云效项目 ID（用于校验页面上下文）。</summary>
    public string? ProjectId { get; set; }

    /// <summary>云效工作项 ID。</summary>
    public string? WorkitemId { get; set; }

    /// <summary>Markdown 评论正文。</summary>
    public string? Content { get; set; }

    /// <summary>父评论 ID。</summary>
    public string? ParentId { get; set; }
}

/// <summary>父需求摘要。</summary>
public sealed class TaskParentRequirement
{
    /// <summary>工作项 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>编号。</summary>
    public string? SerialNumber { get; set; }

    /// <summary>标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>编号与标题的展示文本。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(SerialNumber) ? Title : $"{SerialNumber} {Title}";
}

/// <summary>单端工程上下文。</summary>
public sealed class TaskCodeEnd
{
    /// <summary>端名。</summary>
    public string End { get; set; } = string.Empty;

    /// <summary>仓库内目录。</summary>
    public string? Dir { get; set; }

    /// <summary>流水线名。</summary>
    public string? PipelineName { get; set; }
}

/// <summary>初始化时登记的工程上下文。</summary>
public sealed class TaskCodeContext
{
    /// <summary>宿主工作区 ID。</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>宿主工作区根目录。</summary>
    public string? WorkspaceRoot { get; set; }

    /// <summary>代码库名。</summary>
    public string? RepoName { get; set; }

    /// <summary>目标分支。</summary>
    public string? Branch { get; set; }

    /// <summary>各端目录与流水线。</summary>
    public List<TaskCodeEnd> Ends { get; set; } = [];
}

/// <summary>任务评论展示项。</summary>
public sealed class TaskCommentItem
{
    /// <summary>评论 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>作者显示名。</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>纯文本或 Markdown 正文。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>是否最新一条。</summary>
    public bool IsLatest { get; set; }
}

/// <summary>任务详情聚合结果。</summary>
public sealed class TaskDetailResult
{
    /// <summary>项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>工作项 ID。</summary>
    public string WorkitemId { get; set; } = string.Empty;

    /// <summary>工作项编号。</summary>
    public string? SerialNumber { get; set; }

    /// <summary>标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>描述。</summary>
    public string? Description { get; set; }

    /// <summary>验收标准。</summary>
    public string? Acceptance { get; set; }

    /// <summary>负责人。</summary>
    public string? Assignee { get; set; }

    /// <summary>预计完成日期。</summary>
    public string? DueDate { get; set; }

    /// <summary>状态 ID。</summary>
    public string? StatusId { get; set; }

    /// <summary>状态显示名。</summary>
    public string? StatusName { get; set; }

    /// <summary>状态英文名。</summary>
    public string? StatusNameEn { get; set; }

    /// <summary>是否允许开始工作区会话。</summary>
    public bool CanStart { get; set; }

    /// <summary>迭代 ID。</summary>
    public string? SprintId { get; set; }

    /// <summary>迭代名。</summary>
    public string? SprintName { get; set; }

    /// <summary>父需求。</summary>
    public TaskParentRequirement? ParentRequirement { get; set; }

    /// <summary>代码库与工作区上下文；未初始化时为空。</summary>
    public TaskCodeContext? CodeContext { get; set; }

    /// <summary>评论（时间倒序，最新一条在首位）。</summary>
    public List<TaskCommentItem> Comments { get; set; } = [];

    /// <summary>评论总数。</summary>
    public int CommentCount { get; set; }

    /// <summary>评论读取失败原因；成功时为空。</summary>
    public string? CommentError { get; set; }

    /// <summary>首轮实际带入评论数。</summary>
    public int SeedCommentCount { get; set; }

    /// <summary>服务端装配的首轮输入。</summary>
    public string SeedPrompt { get; set; } = string.Empty;

    /// <summary>非致命告警。</summary>
    public List<string> Warnings { get; set; } = [];
}
