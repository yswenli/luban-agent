/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： IModelContextWindowLookup
*描述：按模型限定名查上下文窗口，供摘要输入按摘要模型自身窗口收敛
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 按模型限定名查上下文窗口；查不到时返回 <c>null</c>（视为未知）。
/// </summary>
public interface IModelContextWindowLookup
{
    /// <summary>查模型窗口。</summary>
    /// <param name="modelKey"><c>provider:model</c>。</param>
    /// <returns>窗口 token 数；模型未登记时为 <c>null</c>。</returns>
    int? GetContextWindow(string modelKey);
}

/// <summary>
/// 从模型能力档案读窗口：用户登记优先，其次内置表。
/// </summary>
public sealed class ConfigModelContextWindowLookup(ConfigManager config) : IModelContextWindowLookup
{
    /// <inheritdoc />
    public int? GetContextWindow(string modelKey) => config.GetModelProfile(modelKey)?.ContextWindow;
}