/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Models
*文件名： StreamEvent
*版本号： V1.0.0.0
*唯一标识：流式事件类型定义
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：定义 Agent 流式对话过程中的事件类型
*
*****************************************************************************/

namespace LubanAgentCore.Models;

/// <summary>
/// 流式事件基类
/// </summary>
public abstract record StreamEvent
{
    /// <summary>
    /// 产生该事件的 LLM 请求/回复轮次（从 1 开始，按“一次 LLM 请求/回复”计）。
    /// 由框架在 <c>LuBanAgent.RunStreamingAsync</c> 中以工具结果边界计算后标注；0 表示未标注。
    /// UI 据此把同一轮的思考内容归并到同一思考块。
    /// </summary>
    public int LlmRound { get; init; }
}

/// <summary>
/// 文本增量事件
/// </summary>
public sealed record TextDeltaEvent(string Delta) : StreamEvent;

/// <summary>
/// 思考内容增量事件
/// </summary>
public sealed record ThinkingDeltaEvent(string Delta) : StreamEvent;

/// <summary>
/// 工具调用开始事件
/// </summary>
public sealed record ToolCallStartedEvent(string Name, string CallId, IReadOnlyDictionary<string, object?> Arguments) : StreamEvent;

/// <summary>
/// 工具调用完成事件
/// </summary>
/// <param name="CallId">调用ID</param>
/// <param name="ResultSummary">结果摘要（前 200 字符，超长末尾加 …，可为 null）</param>
public sealed record ToolCallCompletedEvent(string CallId, string? ResultSummary = null) : StreamEvent;

/// <summary>
/// 工具调用失败事件
/// </summary>
public sealed record ToolCallFailedEvent(string CallId, string Error) : StreamEvent;

/// <summary>
/// 错误事件
/// </summary>
public sealed record ErrorEvent(string Message) : StreamEvent;

/// <summary>
/// 编排进度事件（规划中/节点开始/节点完成/节点失败/反思重规划等）。
/// 用于自动编排这类长耗时非流式阶段向 UI 提供实时反馈。
/// </summary>
/// <param name="EventType">进度事件类型。</param>
/// <param name="NodeId">节点标识（节点级事件）。</param>
/// <param name="Message">事件描述（如节点描述、节点数提示）。</param>
/// <param name="ElapsedSeconds">节点耗时秒数（节点完成事件）。</param>
/// <param name="Error">节点失败原因（节点失败事件）。</param>
/// <param name="Activity">节点活动明细（思考段落/正文增量/工具调用/工具结果）。</param>
public sealed record OrchestrationProgressEvent(
    ProgressEventType EventType,
    string? NodeId,
    string? Message,
    double? ElapsedSeconds,
    string? Error,
    NodeActivityItem? Activity = null) : StreamEvent
{
    /// <summary>
    /// 由框架编排进度模型转换。耗时优先取 <c>ElapsedMs</c>，回退到节点结果耗时（秒）。
    /// </summary>
    /// <param name="progress">框架编排进度。</param>
    public static OrchestrationProgressEvent From(OrchestrationProgress progress)
        => Create(progress.EventType, progress.NodeId, progress.Message, progress.ElapsedMs, progress.NodeResult, progress.Activity);

    /// <summary>
    /// 由框架编排进度各字段转换（统一耗时口径：毫秒优先，回退到节点结果秒数）。
    /// </summary>
    public static OrchestrationProgressEvent Create(
        ProgressEventType eventType,
        string? nodeId,
        string? message,
        long? elapsedMs,
        NodeResult? nodeResult,
        NodeActivityItem? activity) => new(
            eventType,
            nodeId,
            message,
            elapsedMs.HasValue ? elapsedMs.Value / 1000.0 : nodeResult?.Elapsed.TotalSeconds,
            nodeResult?.Error,
            activity);
}

/// <summary>附件已加入。</summary>
/// <param name="FileName">文件名。</param>
/// <param name="MediaType">MIME 类型。</param>
/// <param name="FileSize">字节数。</param>
public sealed record AttachmentAddedEvent(
    string FileName, string MediaType, long FileSize) : StreamEvent;

