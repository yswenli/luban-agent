/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： AgentOptimizeDtos
*描述：提示词改写接口的入参与出参
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>
/// 提示词改写入参。
/// </summary>
public sealed record AgentOptimizeRequest
{
    /// <summary>待改写的原始输入；空白按 400 处理。</summary>
    public string Prompt { get; init; } = "";

    /// <summary>会话标识：用它取该会话当前选中的模型；null 表示用全局默认模型。</summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// 提示词改写结果。
/// </summary>
/// <param name="Text">改写后的正文，已去除首尾空白。</param>
public sealed record AgentOptimizeResponse(string Text);