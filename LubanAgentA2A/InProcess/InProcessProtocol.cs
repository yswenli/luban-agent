/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.InProcess
*文件名： InProcessProtocol
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：进程内 A2A 传输的 message/event metadata 约定键，供执行器与句柄适配器共享
*
*****************************************************************************/

namespace LubanAgentA2A.InProcess;

/// <summary>
/// 进程内 A2A 传输的 metadata 约定。
/// <para>请求方向：<see cref="SpecMarker"/> 标记子代理规格，其余键重建 <c>SubAgentSpec</c>。</para>
/// <para>响应方向：活动事件以 <see cref="EventType"/> + <see cref="ActivityKind"/> 描述，
/// <see cref="SessionId"/> 透出子代理会话标识，<see cref="Detail"/> 携带工具调用参数摘要。</para>
/// </summary>
internal static class InProcessProtocol
{
    /// <summary>规格标记键。</summary>
    public const string SpecMarker = "spec";

    /// <summary>规格标记值，表示该消息来自编排子代理执行器。</summary>
    public const string SpecSubAgent = "subagent";

    /// <summary>节点标识。</summary>
    public const string NodeId = "nodeId";

    /// <summary>角色名称。</summary>
    public const string Role = "role";

    /// <summary>工具组（JSON 数组字符串）。</summary>
    public const string ToolGroups = "toolGroups";

    /// <summary>模型名称。</summary>
    public const string ModelName = "modelName";

    /// <summary>工作区根目录。</summary>
    public const string WorkspaceRoot = "workspaceRoot";

    /// <summary>跨节点共享上下文。</summary>
    public const string SharedContext = "sharedContext";

    /// <summary>编排父会话标识。</summary>
    public const string ParentSessionId = "parentSessionId";

    /// <summary>事件类型键。</summary>
    public const string EventType = "eventType";

    /// <summary>活动种类键。</summary>
    public const string ActivityKind = "activityKind";

    /// <summary>子代理会话标识键。</summary>
    public const string SessionId = "sessionId";

    /// <summary>附加说明键（工具调用参数摘要）。</summary>
    public const string Detail = "detail";

    /// <summary>事件类型：活动。</summary>
    public const string EventTypeActivity = "activity";

    /// <summary>活动：子代理已创建。</summary>
    public const string ActivityStarted = "started";

    /// <summary>活动：思考增量。</summary>
    public const string ActivityThinking = "thinking";

    /// <summary>活动：正文增量。</summary>
    public const string ActivityText = "text";

    /// <summary>活动：工具调用。</summary>
    public const string ActivityToolCall = "toolCall";

    /// <summary>活动：工具结果。</summary>
    public const string ActivityToolResult = "toolResult";
}