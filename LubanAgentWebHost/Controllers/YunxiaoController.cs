/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： YunxiaoController
*描述：云效工作台 REST（薄路由，照抄 PluginController 形态：只做参数透传，业务在 WebYunxiaoService）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 云效工作台 REST。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebYunxiaoService"/>（壳）→ <see cref="YunxiaoWorkbenchService"/>（编排）。
/// </remarks>
public sealed class YunxiaoController : Controller
{
    /// <summary>读取连接设置。</summary>
    /// <returns><c>{ organizationId, hasPat }</c>（不含 PAT 明文）。</returns>
    [HttpGet]
    public Task<ActionResult> Settings() => WebYunxiaoService.GetSettingsAsync();

    /// <summary>测试连接（只读探活）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoTestConnectionRequest"/> JSON。</param>
    /// <returns><c>{ ok, projectCount, message? }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> TestConnection(string payload) => WebYunxiaoService.TestConnectionAsync(payload);

    /// <summary>保存连接设置（服务端先探活，探活失败拒绝保存）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoSaveSettingsRequest"/> JSON。</param>
    /// <returns><c>{ saved: true }</c>。</returns>
    [HttpPost]
    public Task<ActionResult> SaveSettings(string payload) => WebYunxiaoService.SaveSettingsAsync(payload);

    /// <summary>项目列表。</summary>
    /// <returns><c>{ items, total, page, perPage }</c>。</returns>
    [HttpGet]
    public Task<ActionResult> Projects() => WebYunxiaoService.ListProjectsAsync();
}
