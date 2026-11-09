/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Events
*文件名： AgentSseEventNames
*描述：SSE 事件名常量（传输层自定义的部分；框架内建事件名见 AgentEventProtocol）
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Events;

/// <summary>
/// SSE 事件名。前 10 个与框架 <c>LuBan.AIAgent.Protocol.AgentEventProtocol</c> 口径一致；
/// 后 4 个是宿主在传输层补写的事件（框架没有对应的事件类型）。
/// </summary>
public static class AgentSseEventNames
{
    /// <summary>会话首帧；载荷 <c>{ sessionId, created }</c>。</summary>
    public const string Session = "session";

    /// <summary>思考增量；载荷 <c>{ delta, llmRound }</c>。</summary>
    public const string ThinkDelta = "think.delta";

    /// <summary>正文增量；载荷 <c>{ delta, llmRound }</c>。</summary>
    public const string TextDelta = "text.delta";

    /// <summary>工具开始；载荷 <c>{ callId, name, arguments, llmRound }</c>。</summary>
    public const string ToolStarted = "tool.started";

    /// <summary>工具完成；载荷 <c>{ callId, resultSummary, llmRound }</c>。</summary>
    public const string ToolCompleted = "tool.completed";

    /// <summary>工具失败；载荷 <c>{ callId, error, llmRound }</c>。</summary>
    public const string ToolFailed = "tool.failed";

    /// <summary>待确认；载荷 <c>{ confirmationId, toolName, arguments, timeoutSeconds }</c>。</summary>
    public const string ToolConfirm = "tool.confirm";

    /// <summary>附件已加入；载荷 <c>{ fileName, mediaType, fileSize, source }</c>。</summary>
    public const string AttachmentAdded = "attachment.added";

    /// <summary>附件被跳过；载荷 <c>{ source, reason }</c>。</summary>
    public const string AttachmentSkipped = "attachment.skipped";

    /// <summary>错误；载荷 <c>{ message }</c>。</summary>
    public const string Error = "error";

    /// <summary>结束；载荷 <c>{ sessionId }</c>。</summary>
    public const string Done = "done";

    /// <summary>计划项；载荷 <c>{ toolName, arguments }</c>。</summary>
    public const string PlanItem = "plan.item";

    /// <summary>用户拒绝工具调用；载荷 <c>{ toolName, arguments }</c>。</summary>
    public const string ToolDenied = "tool.denied";

    /// <summary>编排进度；载荷见 <see cref="OrchestrationProgressPayload"/>。</summary>
    public const string OrchestrationProgress = "orchestration.progress";
}