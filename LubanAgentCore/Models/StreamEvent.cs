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
public abstract record StreamEvent;

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
public sealed record ToolCallCompletedEvent(string CallId) : StreamEvent;

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
    NodeActivityItem? Activity = null) : StreamEvent;
