/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： RagPrecisionRule
*版本号： V1.0.0.0
*唯一标识：RAG 精度规则
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：RAG 精度规则，多版本知识点优先使用最新版本的标记规则
*
*=================================================
*修改标记
*修改时间：2026/7/31
*修改人： yswenli
*版本号： V1.0.0.0
*描述：RAG 精度规则，多版本知识点优先使用最新版本的标记规则
*
*****************************************************************************/

namespace LubanAgent.Services;

/// <summary>
/// RAG 精度规则 - 标记多版本知识点优先使用最新版本。
/// </summary>
/// <remarks>
/// 当前框架的 <see cref="RuleEngine"/> 不支持运行时注册，此规则作为标记存在。
/// 实际的多版本去重逻辑在 <c>AgiCommand.InjectRetrievalContextAsync</c> 中实现。
/// </remarks>
public class RagPrecisionRule : IRule
{
    /// <summary>
    /// 规则 ID。
    /// </summary>
    public string Id => "rag-precision";

    /// <summary>
    /// 规则名称。
    /// </summary>
    public string Name => "RAG精度规则";

    /// <summary>
    /// 规则描述。
    /// </summary>
    public string Description => "多版本知识点优先使用最新版本";

    /// <summary>
    /// 规则优先级（数字越大优先级越高）。
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// 规则是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 检查规则是否适用于检索操作。
    /// </summary>
    /// <param name="context">规则上下文</param>
    /// <returns>当操作类型为 retrieval 时返回 true</returns>
    public bool IsApplicable(RuleContext context)
    {
        return context.ActionType == "retrieval";
    }

    /// <summary>
    /// 执行规则，返回允许结果。
    /// </summary>
    /// <param name="context">规则上下文</param>
    /// <returns>允许结果</returns>
    public Task<RuleResult> ExecuteAsync(RuleContext context)
    {
        return Task.FromResult(RuleResult.AllowResult("RAG精度规则已应用"));
    }
}
