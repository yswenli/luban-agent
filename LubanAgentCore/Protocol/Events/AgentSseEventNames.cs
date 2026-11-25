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
/// 其余是宿主在传输层补写的事件（框架没有对应的事件类型）。
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

    /// <summary>待用户作答；载荷 <c>{ questionId, questions, timeoutSeconds, origin }</c>。</summary>
    public const string ToolQuestion = "tool.question";

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

    /// <summary>LLM 调用自动重试；载荷 <c>{ category, label, attempt, maxAttempts, delayMs }</c>。</summary>
    public const string ApiRetry = "api.retry";

    /// <summary>上下文压缩；载荷 <c>{ phase, trigger, archivedCount, keptCount, estimatedTokens, budget, message }</c>。</summary>
    public const string ContextCompacted = "context.compacted";

    /// <summary>续流快照；载荷即 <see cref="Hosting.AgentRunSnapshot"/>（仅在接上已进行中的回合时下发一次）。</summary>
    public const string RunSnapshot = "run.snapshot";

    /// <summary>会话标题变更（自动命名完成）；载荷 <c>{ sessionId, title }</c>。</summary>
    public const string SessionTitle = "session.title";
}