/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： SessionGenerationState
*描述：一次解析得到的会话生成参数（思考强度 + 上下文预算）
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 会话生成参数的一次性解析结果。
/// </summary>
/// <remarks>
/// 思考强度与上下文预算共用同一次「会话 → 模型」解析与会话行读取，
/// 供需要同时展示两者的调用方使用（如 Web 会话设置读取）。
/// </remarks>
/// <param name="ReasoningEffort">生效的思考强度枚举名；<c>null</c> 表示跟随模型。</param>
/// <param name="Budget">上下文预算；模型窗口未知时为 <c>null</c>。</param>
public readonly record struct SessionGenerationState(string? ReasoningEffort, ContextBudget? Budget);