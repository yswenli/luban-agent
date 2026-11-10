/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： RuleController
*描述：规则设置接口（浏览器端，文件型条目）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 规则设置接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebRuleService"/>。
/// 与桌面端设置页一致，只暴露文件型条目（<c>rules/&lt;id&gt;.json</c>）：不列内置与 config 自定义规则，也不提供启停。
/// </remarks>
public sealed class RuleController : Controller
{
    /// <summary>列出作用域内的规则 Id。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识；缺省按全局。</param>
    /// <returns>规则 Id 数组。</returns>
    [HttpGet]
    public Task<ActionResult> List(string? scope) => WebRuleService.ListAsync(scope);

    /// <summary>读取规则内容。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识。</param>
    /// <param name="key">规则 Id（文件名去扩展名）。</param>
    /// <returns>规则内容；不存在时 404。</returns>
    [HttpGet]
    public Task<ActionResult> Read(string? scope, string? key) => WebRuleService.ReadAsync(scope, key);

    /// <summary>保存规则。</summary>
    /// <param name="payload">包装后的 <see cref="RuleSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键。</returns>
    [HttpPost]
    public Task<ActionResult> Save(string payload) => WebRuleService.SaveAsync(payload);

    /// <summary>删除规则。</summary>
    /// <param name="payload">包装后的 <see cref="SettingsKeyRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebRuleService.DeleteAsync(payload);
}