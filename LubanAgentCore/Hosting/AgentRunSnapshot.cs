/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： AgentRunSnapshot
*描述：一轮对话到当前时刻的语义快照（接上已进行中的回合时一次性下发）
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 一轮对话到当前时刻的语义快照；仅在「页面刷新、前端内存为空」时下发。
/// </summary>
/// <remarks>
/// 快照是「全量重建」而不是「缝隙填充」：前端收到即整段重建 assistant 气泡，
/// 随后的实时帧继续在其上累积。
/// </remarks>
public sealed record AgentRunSnapshot
{
    /// <summary>轮次标识；前端用它精确取消本轮。</summary>
    public string RunId { get; init; } = "";

    /// <summary>所属会话。</summary>
    public string SessionId { get; init; } = "";

    /// <summary>轮次开始时间。</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>生成快照时已发布的帧序号；其后只补 <c>seq &gt; Seq</c> 的帧。</summary>
    public int Seq { get; init; }

    /// <summary>已累积的正文。</summary>
    public string Text { get; init; } = "";

    /// <summary>已累积的思考。</summary>
    public string Thinking { get; init; } = "";

    /// <summary>工具卡片。</summary>
    public List<AgentRunToolSnapshot> Tools { get; init; } = [];

    /// <summary>计划项。</summary>
    public List<AgentRunPlanSnapshot> Plans { get; init; } = [];

    /// <summary>编排进度文案。</summary>
    public List<string> Progress { get; init; } = [];

    /// <summary>已加入的附件数。</summary>
    public int AttachmentsAdded { get; init; }

    /// <summary>被跳过的附件数。</summary>
    public int AttachmentsSkipped { get; init; }

    /// <summary>前端呈现为独立提示条目的帧（原文保留，由前端用同一套渲染逻辑回放）。</summary>
    public List<AgentRunNoticeSnapshot> Notices { get; init; } = [];

    /// <summary>当前等待中的工具确认；无则为 null。</summary>
    public AgentRunConfirmationSnapshot? PendingConfirmation { get; init; }
}

/// <summary>工具卡快照。</summary>
/// <remarks><c>Arguments</c> 为原始 JSON 文本（缩进与展示由前端决定）。</remarks>
public sealed record AgentRunToolSnapshot
{
    /// <summary>工具调用标识。</summary>
    public string CallId { get; init; } = "";

    /// <summary>工具名。</summary>
    public string Name { get; init; } = "";

    /// <summary>原始 JSON 文本形式的入参。</summary>
    public string Arguments { get; init; } = "";

    /// <summary>状态：<c>running</c> / <c>done</c> / <c>failed</c>。</summary>
    public string Status { get; init; } = "running";

    /// <summary>结果摘要；仅完成时有值。</summary>
    public string? ResultSummary { get; init; }

    /// <summary>错误信息；仅失败时有值。</summary>
    public string? Error { get; init; }
}

/// <summary>计划项快照。</summary>
public sealed record AgentRunPlanSnapshot
{
    /// <summary>工具名。</summary>
    public string ToolName { get; init; } = "";

    /// <summary>原始 JSON 文本形式的入参。</summary>
    public string Arguments { get; init; } = "";
}

/// <summary>提示条目快照：原样保存该帧的事件名与载荷 JSON。</summary>
public sealed record AgentRunNoticeSnapshot
{
    /// <summary>事件名，见 <see cref="Protocol.Events.AgentSseEventNames"/>。</summary>
    public string Event { get; init; } = "";

    /// <summary>载荷 JSON 原文。</summary>
    public string Data { get; init; } = "";
}

/// <summary>等待中的工具确认快照。</summary>
/// <remarks><c>ExpiresAt</c> 让前端按剩余时间倒数，而不是从满值重来。</remarks>
public sealed record AgentRunConfirmationSnapshot
{
    /// <summary>确认请求标识。</summary>
    public string ConfirmationId { get; init; } = "";

    /// <summary>工具名。</summary>
    public string ToolName { get; init; } = "";

    /// <summary>原始 JSON 文本形式的入参。</summary>
    public string Arguments { get; init; } = "";

    /// <summary>原始超时秒数。</summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>过期时刻；前端据剩余时间倒数。</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}