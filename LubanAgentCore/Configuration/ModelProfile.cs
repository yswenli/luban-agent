/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： ModelProfile
*描述：模型能力档案与全局默认生成参数
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 模型能力档案：该模型能接受哪些思考档位、上下文窗口多大。
/// </summary>
/// <remarks>
/// 语义区分靠 <c>null</c> 与空数组：<see cref="Reasoning"/> 为 <c>null</c> 表示未知（只给「跟随模型」），
/// 空数组表示标准 <c>reasoning_effort</c> 链路没有档位（UI 不出控件），非空即该模型可选的枚举名子集。
/// </remarks>
public sealed class ModelProfile
{
    /// <summary>可选的思考档位（<c>ReasoningEffort</c> 枚举名）；null=未知，空数组=不支持。</summary>
    public List<string>? Reasoning { get; set; }

    /// <summary>模型上下文窗口（token）；null=未知，不允许 0。</summary>
    public int? ContextWindow { get; set; }

    /// <summary>复制一份，避免调用方直接改到配置对象。</summary>
    public ModelProfile Clone() => new()
    {
        Reasoning = Reasoning is null ? null : [.. Reasoning],
        ContextWindow = ContextWindow
    };
}

/// <summary>
/// 全局默认生成参数；会话未单独覆盖时生效。
/// </summary>
public sealed class GenerationDefaults
{
    /// <summary>思考强度（<c>ReasoningEffort</c> 枚举名）；null=跟随模型。</summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>上下文预算档位（token）；null=按窗口取默认档（300K）。</summary>
    public int? ContextTier { get; set; }

    /// <summary>复制一份，避免调用方直接改到配置对象。</summary>
    public GenerationDefaults Clone() => new()
    {
        ReasoningEffort = ReasoningEffort,
        ContextTier = ContextTier
    };
}