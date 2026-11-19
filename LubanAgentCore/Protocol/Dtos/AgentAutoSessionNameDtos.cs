/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： AgentAutoSessionNameDtos
*描述：会话自动命名接口的入参与出参
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>
/// 会话自动命名的入参。
/// </summary>
public sealed record AgentAutoSessionNameRequest
{
    /// <summary>待概括的对话内容；空白按 400 处理。</summary>
    public string Text { get; init; } = "";

    /// <summary>会话标识：用它取该会话当前选中的模型；null 表示用全局默认模型。</summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// 会话自动命名的结果。
/// </summary>
/// <param name="Title">不超过 15 字的主题，已去除装饰与末尾标点。</param>
public sealed record AgentAutoSessionNameResponse(string Title);