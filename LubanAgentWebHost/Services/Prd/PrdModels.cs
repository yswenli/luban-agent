/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Prd
*文件名： PrdModels
*描述：PRD 同步 / 拆分 / 提交的请求与结果模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Services.Prd;

#region 同步

/// <summary>
/// PRD 同步请求。
/// </summary>
/// <param name="ProjectId">云效项目 ID。</param>
/// <param name="SprintId">迭代 ID（可选，非空时只同步该迭代下的需求）。</param>
public sealed record PrdSyncRequest(string ProjectId, string? SprintId);

/// <summary>
/// 同步得到的单条需求。
/// </summary>
public sealed class PrdRequirementItem
{
    /// <summary>工作项 ID。</summary>
    public string WorkitemId { get; set; } = string.Empty;

    /// <summary>需求编号（如 <c>DSDD-123</c>）。</summary>
    public string? SerialNumber { get; set; }

    /// <summary>标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>描述正文。</summary>
    public string? Description { get; set; }

    /// <summary>验收标准（取自定义字段「验收标准」）。</summary>
    public string? Acceptance { get; set; }

    /// <summary>来源标记（同步固定为 <c>sync</c>）。</summary>
    public string Source { get; set; } = "sync";
}

/// <summary>
/// 同步得到的需求附件（仅元数据）。
/// </summary>
public sealed class PrdAttachmentItem
{
    /// <summary>所属工作项 ID。</summary>
    public string? WorkitemId { get; set; }

    /// <summary>所属工作项标题。</summary>
    public string? WorkitemTitle { get; set; }

    /// <summary>文件 ID。</summary>
    public string? FileId { get; set; }

    /// <summary>文件名。</summary>
    public string? FileName { get; set; }

    /// <summary>后缀。</summary>
    public string? Suffix { get; set; }

    /// <summary>大小（字节）。</summary>
    public long? Size { get; set; }

    /// <summary>临时下载地址（有时效）。</summary>
    public string? Url { get; set; }
}

/// <summary>
/// PRD 同步结果。
/// </summary>
public sealed class PrdSyncResult
{
    /// <summary>项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>项目名称。</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>状态：<c>ready</c>（全量成功）/ <c>partial</c>（部分失败）/ <c>empty</c>（无需求）/ <c>failed</c>（整体失败）。</summary>
    public string Status { get; set; } = "empty";

    /// <summary>整体失败原因（<see cref="Status"/> 为 <c>failed</c> 时非空）。</summary>
    public string? Message { get; set; }

    /// <summary>需求清单。</summary>
    public List<PrdRequirementItem> Requirements { get; set; } = [];

    /// <summary>附件清单（仅元数据）。</summary>
    public List<PrdAttachmentItem> Attachments { get; set; } = [];

    /// <summary>逐项告警（某条需求详情 / 附件读取失败等）。</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>同步时间。</summary>
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// PRD 来源描述（页面头部信息）。
/// </summary>
public sealed class PrdSourceInfo
{
    /// <summary>项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>项目名称。</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>可选测试用例库。</summary>
    public List<PrdTestRepoRef> TestRepos { get; set; } = [];

    /// <summary>是否存在可写入的用例库（无则测试用例与测试计划会失败）。</summary>
    public bool HasTestRepo { get; set; }

    /// <summary>云效连通性说明（读取失败时的原因）。</summary>
    public string? Message { get; set; }

    /// <summary>可用状态候选（从项目现有工作项反查去重；云效无独立状态枚举接口）。</summary>
    public List<PrdStatusOption> Statuses { get; set; } = [];
}

/// <summary>
/// 工作项状态候选。
/// </summary>
public sealed class PrdStatusOption
{
    /// <summary>状态 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>状态名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>所属分类：<c>Req</c> / <c>Task</c>。</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>英文名（如 <c>TODO</c> / <c>DOING</c> / <c>DONE</c>，看板按此排序列序）。</summary>
    public string? NameEn { get; set; }
}

/// <summary>
/// 测试用例库引用。
/// </summary>
public sealed class PrdTestRepoRef
{
    /// <summary>用例库 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>用例库名称。</summary>
    public string Name { get; set; } = string.Empty;
}

#endregion

#region 拆分

/// <summary>
/// 补充文档（本地 PRD / 工作区文档）。
/// </summary>
public sealed class PrdSupplementDocument
{
    /// <summary>文档名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>文档正文（纯文本）。</summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// 拆分预览请求。
/// </summary>
public sealed class PrdSplitRequest
{
    /// <summary>云效项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>迭代 ID（可选，作为同步过滤与拆分上下文）。</summary>
    public string? SprintId { get; set; }

    /// <summary>目标迭代名称（可选，留空则由模型按需求内容命名）。</summary>
    public string? IterationName { get; set; }

    /// <summary>补充文档（可选）。</summary>
    public List<PrdSupplementDocument> Documents { get; set; } = [];
}

/// <summary>
/// 迭代拆分项。
/// </summary>
public sealed class PrdIterationPlan
{
    /// <summary>迭代名称。</summary>
    public string? Name { get; set; }

    /// <summary>开始日期（<c>yyyy-MM-dd</c>）。</summary>
    public string? StartDate { get; set; }

    /// <summary>结束日期（<c>yyyy-MM-dd</c>）。</summary>
    public string? EndDate { get; set; }

    /// <summary>迭代描述。</summary>
    public string? Description { get; set; }
}

/// <summary>
/// 需求拆分项。
/// </summary>
public sealed class PrdRequirementPlan
{
    /// <summary>拆分内唯一键（模型给或归一化补）。</summary>
    public string? Key { get; set; }

    /// <summary>标题。</summary>
    public string? Title { get; set; }

    /// <summary>描述。</summary>
    public string? Description { get; set; }

    /// <summary>验收标准。</summary>
    public string? Acceptance { get; set; }

    /// <summary>优先级（P0 / P1 / P2）。</summary>
    public string? Priority { get; set; }

    /// <summary>来源：<c>sync</c> / <c>supplement</c> / <c>local</c>。</summary>
    public string? Source { get; set; }

    /// <summary>来源标题（同步需求的原始标题 / 补充文档名）。</summary>
    public string? SourceTitle { get; set; }

    /// <summary>来源段落（原文中被拆出的句子，用于回执溯源）。</summary>
    public string? SourceParagraph { get; set; }
}

/// <summary>
/// 任务拆分项。
/// </summary>
public sealed class PrdTaskPlan
{
    /// <summary>拆分内唯一键。</summary>
    public string? Key { get; set; }

    /// <summary>所属需求键（必填）。</summary>
    public string? RequirementKey { get; set; }

    /// <summary>标题。</summary>
    public string? Title { get; set; }

    /// <summary>描述。</summary>
    public string? Description { get; set; }

    /// <summary>优先级（P0 / P1 / P2）。</summary>
    public string? Priority { get; set; }

    /// <summary>截止日期（<c>yyyy-MM-dd</c>）。</summary>
    public string? DueDate { get; set; }

    /// <summary>来源段落（继承父需求的原文片段）。</summary>
    public string? SourceParagraph { get; set; }
}

/// <summary>
/// 测试用例拆分项。
/// </summary>
public sealed class PrdTestcasePlan
{
    /// <summary>拆分内唯一键。</summary>
    public string? Key { get; set; }

    /// <summary>所属需求键（可选）。</summary>
    public string? RequirementKey { get; set; }

    /// <summary>用例标题。</summary>
    public string? Title { get; set; }

    /// <summary>前置条件。</summary>
    public string? PreCondition { get; set; }

    /// <summary>测试步骤（纯文本，换行分隔）。</summary>
    public string? Steps { get; set; }

    /// <summary>来源段落（继承父需求 / 补充文档的原文片段）。</summary>
    public string? SourceParagraph { get; set; }
}

/// <summary>
/// 测试计划拆分项。
/// </summary>
public sealed class PrdTestplanPlan
{
    /// <summary>计划名称。</summary>
    public string? Name { get; set; }

    /// <summary>测试环境。</summary>
    public string? Environment { get; set; }

    /// <summary>执行人 userId（可选）。</summary>
    public string? Executor { get; set; }
}

/// <summary>
/// 拆分计划。
/// </summary>
public sealed class PrdSplitPlan
{
    /// <summary>迭代。</summary>
    public PrdIterationPlan? Iteration { get; set; }

    /// <summary>需求清单。</summary>
    public List<PrdRequirementPlan> Requirements { get; set; } = [];

    /// <summary>任务清单。</summary>
    public List<PrdTaskPlan> Tasks { get; set; } = [];

    /// <summary>测试用例清单。</summary>
    public List<PrdTestcasePlan> Testcases { get; set; } = [];

    /// <summary>测试计划。</summary>
    public PrdTestplanPlan? TestPlan { get; set; }

    /// <summary>归一化阶段的告警（丢弃无关联任务等）。</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// 拆分预览结果。
/// </summary>
public sealed class PrdSplitPreviewResult
{
    /// <summary>拆分计划。</summary>
    public PrdSplitPlan Plan { get; set; } = new();

    /// <summary>同步状态（<c>ready</c> / <c>partial</c> / <c>empty</c> / <c>failed</c>）。</summary>
    public string SyncStatus { get; set; } = "empty";

    /// <summary>本次同步到的需求条数。</summary>
    public int RequirementCount { get; set; }

    /// <summary>使用的补充文档名。</summary>
    public List<string> DocumentNames { get; set; } = [];

    /// <summary>告警汇总。</summary>
    public List<string> Warnings { get; set; } = [];
}

#endregion

#region 提交

/// <summary>
/// 拆分提交请求。
/// </summary>
public sealed class PrdSubmitRequest
{
    /// <summary>云效项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>指定迭代 ID（可选，留空则按计划创建新迭代）。</summary>
    public string? SprintId { get; set; }

    /// <summary>负责人 userId（可选，留空取当前用户）。</summary>
    public string? AssignedTo { get; set; }

    /// <summary>拆分计划。</summary>
    public PrdSplitPlan? Plan { get; set; }
}

/// <summary>
/// 单条提交回执。
/// </summary>
public sealed class PrdSubmitReceipt
{
    /// <summary>类型：<c>iteration</c> / <c>requirement</c> / <c>task</c> / <c>relation</c> / <c>testcase</c> / <c>testplan</c>。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>幂等键。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>展示标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>是否成功。</summary>
    public bool Ok { get; set; }

    /// <summary>云效侧对象 ID。</summary>
    public string? RemoteId { get; set; }

    /// <summary>失败原因（成功为 <c>null</c>）。</summary>
    public string? Message { get; set; }

    /// <summary>是否复用了上次已成功的回执（幂等命中）。</summary>
    public bool Reused { get; set; }

    /// <summary>来源类型：<c>sync</c>（云效同步）/ <c>supplement</c> / <c>local</c>（本地上传）。</summary>
    public string? SourceType { get; set; }

    /// <summary>来源段落（原文片段，供溯源核对）。</summary>
    public string? SourceParagraph { get; set; }

    /// <summary>回执时间。</summary>
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// 拆分提交结果。
/// </summary>
public sealed class PrdSubmitResult
{
    /// <summary>迭代 ID。</summary>
    public string? SprintId { get; set; }

    /// <summary>逐项回执。</summary>
    public List<PrdSubmitReceipt> Receipts { get; set; } = [];

    /// <summary>成功条数（不含关系回执）。</summary>
    public int OkCount { get; set; }

    /// <summary>失败条数（不含关系回执）。</summary>
    public int FailedCount { get; set; }

    /// <summary>告警（账本落盘失败等）。</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// 幂等回执账本（按项目落盘）。
/// </summary>
public sealed class PrdReceiptLedger
{
    /// <summary>项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>最近一次使用的迭代 ID。</summary>
    public string? SprintId { get; set; }

    /// <summary>更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>历史回执（按 <see cref="PrdSubmitReceipt.Key"/> 去重，保留最新）。</summary>
    public List<PrdSubmitReceipt> Receipts { get; set; } = [];
}

#endregion

#region 迭代测试（F9）

/// <summary>
/// 用例生成请求（按来源多选）。
/// </summary>
public sealed class IterationTestRequest
{
    /// <summary>云效项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>迭代 ID（可选，用于缩小来源范围）。</summary>
    public string? SprintId { get; set; }

    /// <summary>来源多选：<c>needs</c> / <c>prd</c> / <c>tasks</c> / <c>manual</c>。</summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>手工输入的来源正文（<see cref="Sources"/> 含 <c>manual</c> 时使用）。</summary>
    public string? ManualContent { get; set; }

    /// <summary>需求编号范围（空表示全部需求）。</summary>
    public List<string>? RequirementKeys { get; set; }
}

/// <summary>
/// 生成出的单条测试用例。
/// </summary>
public sealed class IterationTestCaseItem
{
    /// <summary>用例键。</summary>
    public string? Key { get; set; }

    /// <summary>用例标题。</summary>
    public string? Title { get; set; }

    /// <summary>前置条件。</summary>
    public string? PreCondition { get; set; }

    /// <summary>测试步骤（纯文本，换行分隔）。</summary>
    public string? Steps { get; set; }

    /// <summary>关联需求键（可选）。</summary>
    public string? RequirementKey { get; set; }

    /// <summary>来源段落（原文片段）。</summary>
    public string? SourceParagraph { get; set; }

    /// <summary>合并后保留的来源列表（重复用例合并时取并集）。</summary>
    public List<string> Sources { get; set; } = [];
}

/// <summary>
/// 用例生成结果。
/// </summary>
public sealed class IterationTestPreviewResult
{
    /// <summary>用例清单（已按标题归一化去重合并）。</summary>
    public List<IterationTestCaseItem> Cases { get; set; } = [];

    /// <summary>被合并掉的重复用例条数。</summary>
    public int MergedCount { get; set; }

    /// <summary>参与生成的来源需求条数。</summary>
    public int SourceRequirementCount { get; set; }

    /// <summary>告警。</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// 测试计划设置。
/// </summary>
public sealed class IterationTestPlanInput
{
    /// <summary>计划名称。</summary>
    public string? Name { get; set; }

    /// <summary>测试环境。</summary>
    public string? Environment { get; set; }

    /// <summary>执行人 userId（可选，留空取当前用户）。</summary>
    public string? Executor { get; set; }

    /// <summary>开始日期（<c>yyyy-MM-dd</c>）。</summary>
    public string? StartDate { get; set; }

    /// <summary>结束日期（<c>yyyy-MM-dd</c>）。</summary>
    public string? EndDate { get; set; }
}

/// <summary>
/// 用例 + 测试计划保存请求。
/// </summary>
public sealed class IterationTestSaveRequest
{
    /// <summary>云效项目 ID。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>关联迭代 ID（可选）。</summary>
    public string? SprintId { get; set; }

    /// <summary>用例清单（<see cref="IterationTestCaseItem.Sources"/> 非空的才保存）。</summary>
    public List<IterationTestCaseItem> Cases { get; set; } = [];

    /// <summary>测试计划设置。</summary>
    public IterationTestPlanInput? TestPlan { get; set; }
}

/// <summary>
/// 保存结果。
/// </summary>
public sealed class IterationTestSaveResult
{
    /// <summary>逐项回执。</summary>
    public List<PrdSubmitReceipt> Receipts { get; set; } = [];

    /// <summary>成功条数。</summary>
    public int OkCount { get; set; }

    /// <summary>失败条数。</summary>
    public int FailedCount { get; set; }

    /// <summary>新建测试计划标识（未建则为 <c>null</c>）。</summary>
    public string? TestPlanId { get; set; }

    /// <summary>告警。</summary>
    public List<string> Warnings { get; set; } = [];
}

#endregion
