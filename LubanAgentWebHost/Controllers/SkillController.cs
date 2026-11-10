/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： SkillController
*描述：技能设置接口（浏览器端，文件型条目）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 技能设置接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebSkillService"/>。
/// 与桌面端设置页一致，只暴露文件型条目（<c>SKILL.md</c>）：不列内置与 config 自定义技能，也不提供启停。
/// </remarks>
public sealed class SkillController : Controller
{
    /// <summary>列出作用域内的技能名。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识；缺省按全局。</param>
    /// <returns>技能名数组。</returns>
    [HttpGet]
    public Task<ActionResult> List(string? scope) => WebSkillService.ListAsync(scope);

    /// <summary>读取技能内容。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识。</param>
    /// <param name="key">技能名（目录名）。</param>
    /// <returns>技能内容；不存在时 404。</returns>
    [HttpGet]
    public Task<ActionResult> Read(string? scope, string? key) => WebSkillService.ReadAsync(scope, key);

    /// <summary>保存技能。</summary>
    /// <param name="payload">包装后的 <see cref="SkillSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键。</returns>
    [HttpPost]
    public Task<ActionResult> Save(string payload) => WebSkillService.SaveAsync(payload);

    /// <summary>删除技能。</summary>
    /// <param name="payload">包装后的 <see cref="SettingsKeyRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebSkillService.DeleteAsync(payload);
}