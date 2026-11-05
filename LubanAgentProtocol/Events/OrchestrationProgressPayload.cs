/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Protocol.Events
*文件名： OrchestrationProgressPayload
*描述：编排进度事件（orchestration.progress）的传输载荷与框架类型互转
*
*****************************************************************************/

using LuBan.AIAgent.Hosting;
using LuBan.AIAgent.Orchestration.Models;

namespace LubanAgent.Protocol.Events;

/// <summary>
/// 子代理节点内部活动条目的传输载荷。
/// </summary>
/// <remarks>
/// 只在传输层出现：客户端收到后转回框架的 <see cref="NodeActivityItem"/>，
/// 保证界面消费方（如桌面端的子代理活动卡片）拿到的仍是框架类型。
/// </remarks>
public sealed record OrchestrationActivityPayload
{
    /// <summary>活动类型。</summary>
    public NodeActivityKind Kind { get; init; }

    /// <summary>活动文本（思考/正文内容，或工具参数、结果摘要）。</summary>
    public string? Content { get; init; }

    /// <summary>工具名（工具调用时提供）。</summary>
    public string? ToolName { get; init; }

    /// <summary>工具调用标识。</summary>
    public string? CallId { get; init; }

    /// <summary>活动发生时间。</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>由框架活动条目转换。</summary>
    /// <param name="item">框架活动条目。</param>
    /// <returns>传输载荷。</returns>
    public static OrchestrationActivityPayload From(NodeActivityItem item) => new()
    {
        Kind = item.Kind,
        Content = item.Content,
        ToolName = item.ToolName,
        CallId = item.CallId,
        Timestamp = item.Timestamp
    };

    /// <summary>转回框架活动条目。</summary>
    /// <returns>框架活动条目。</returns>
    public NodeActivityItem ToActivity() => new()
    {
        Kind = Kind,
        Content = Content,
        ToolName = ToolName,
        CallId = CallId,
        Timestamp = Timestamp
    };
}

/// <summary>
/// 编排进度（<c>orchestration.progress</c>）的传输载荷。
/// </summary>
/// <remarks>
/// 编排在服务端工具调用内部执行，进度不经过对话流；WebHost 订阅
/// <c>IOrchestrationProgressSink</c> 后按本载荷逐条推给客户端，
/// 客户端还原为 <see cref="OrchestrationProgressEvent"/> 交给界面。
/// </remarks>
public sealed record OrchestrationProgressPayload
{
    /// <summary>进度事件类型。</summary>
    public required ProgressEventType EventType { get; init; }

    /// <summary>节点标识（节点级事件）。</summary>
    public string? NodeId { get; init; }

    /// <summary>事件描述（如节点描述、节点数提示）。</summary>
    public string? Message { get; init; }

    /// <summary>节点耗时秒数（节点完成事件）。</summary>
    public double? ElapsedSeconds { get; init; }

    /// <summary>节点失败原因。</summary>
    public string? Error { get; init; }

    /// <summary>节点活动明细。</summary>
    public OrchestrationActivityPayload? Activity { get; init; }

    /// <summary>由框架进度事件转换。</summary>
    /// <param name="evt">框架进度事件。</param>
    /// <returns>传输载荷。</returns>
    public static OrchestrationProgressPayload From(OrchestrationProgressEvent evt) => new()
    {
        EventType = evt.EventType,
        NodeId = evt.NodeId,
        Message = evt.Message,
        ElapsedSeconds = evt.ElapsedSeconds,
        Error = evt.Error,
        Activity = evt.Activity is null ? null : OrchestrationActivityPayload.From(evt.Activity)
    };

    /// <summary>转回框架进度事件。</summary>
    /// <returns>框架进度事件。</returns>
    public OrchestrationProgressEvent ToEvent() => new(
        EventType,
        NodeId,
        Message,
        ElapsedSeconds,
        Error,
        Activity?.ToActivity());
}