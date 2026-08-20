/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Services
*文件名： StreamEvent
*版本号： V1.0.0.0
*唯一标识：流式事件类型定义
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：定义 Agent 流式对话过程中的事件类型
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCodex.Services;

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
