/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： ISessionGenerationSettings
*描述：解析会话生效的思考强度与上下文预算
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 解析会话生效的生成参数（思考强度 / 上下文预算）。
/// </summary>
/// <remarks>
/// 解析链一律是「会话覆盖 → 全局默认 → 模型自身」；调用方不需要自己拼接。
/// </remarks>
public interface ISessionGenerationSettings
{
    /// <summary>解析本轮思考强度：会话列 → 全局默认 → null（跟随模型）。</summary>
    /// <param name="sessionId">会话标识；空表示只按全局默认解析。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns><c>ReasoningEffort</c> 枚举名；跟随模型时为 <c>null</c>。</returns>
    ValueTask<string?> ResolveReasoningEffortAsync(string? sessionId, CancellationToken ct = default);

    /// <summary>解析上下文预算：选中档位 + 按模型窗口留白收敛后的实际触发阈值；窗口未知返回 null。</summary>
    /// <param name="sessionId">会话标识；空表示只按全局默认解析。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>选中档位与实际触发阈值；模型窗口未知且无覆盖时为 <c>null</c>。</returns>
    ValueTask<ContextBudget?> ResolveContextBudgetAsync(string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 一次解析思考强度与上下文预算：共用一次模型解析与会话行读取。
    /// </summary>
    /// <param name="sessionId">会话标识；空表示只按全局默认解析。</param>
    /// <param name="profile">已解析的模型档案；传 <c>null</c> 时按会话重新解析。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>思考强度与上下文预算；各自不可解析时为 <c>null</c>。</returns>
    ValueTask<SessionGenerationState> ResolveAsync(
        string? sessionId,
        ModelProfile? profile = null,
        CancellationToken ct = default);

    /// <summary>按模型窗口生成档位清单；窗口未知返回空。</summary>
    /// <param name="modelKey"><c>provider:model</c>。</param>
    IReadOnlyList<int> GetTiers(string modelKey);
}