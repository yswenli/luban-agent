/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： ModelController
*描述：模型设置接口（浏览器端，按供应商列出与维护自定义模型）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 模型设置接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebModelService"/>。
/// 只读本地清单（内置预设 + 缓存 + 自定义），不触发远端刷新；刷新走 <see cref="WebProviderService.ModelsAsync"/>。
/// 全局模型选择走 <see cref="WebProviderService.SelectAsync"/>。
/// </remarks>
public sealed class ModelController : Controller
{
    /// <summary>按供应商列出模型清单。</summary>
    /// <param name="providerId">供应商名；缺省返回全部供应商。</param>
    /// <returns>按供应商分组的模型清单。</returns>
    [HttpGet]
    public Task<ActionResult> List(string? providerId) => WebModelService.ListAsync(providerId);

    /// <summary>新增自定义模型。</summary>
    /// <param name="payload">包装后的 <see cref="ModelAddRequest"/> JSON。</param>
    /// <returns>新增后的模型条目。</returns>
    [HttpPost]
    public Task<ActionResult> Add(string payload) => WebModelService.AddAsync(payload);

    /// <summary>重命名自定义模型。</summary>
    /// <param name="payload">包装后的 <see cref="ModelRenameRequest"/> JSON。</param>
    /// <returns>重命名后的模型条目。</returns>
    [HttpPost]
    public Task<ActionResult> Rename(string payload) => WebModelService.RenameAsync(payload);

    /// <summary>删除自定义模型。</summary>
    /// <param name="payload">包装后的 <see cref="ModelTargetRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebModelService.DeleteAsync(payload);
}