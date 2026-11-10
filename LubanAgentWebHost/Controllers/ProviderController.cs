/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： ProviderController
*描述：供应商与模型接口（浏览器端目前只用到「全局模型选择」读写）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 供应商与模型接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebProviderService"/>。
/// </remarks>
public sealed class ProviderController : Controller
{
    /// <summary>读取全局模型选择。</summary>
    /// <returns><c>{ providerId, modelId }</c>；未设置时两者为 null。</returns>
    [HttpGet]
    public Task<ActionResult> Selection() => WebProviderService.SelectionAsync();

    /// <summary>
    /// 拉取指定供应商的可用模型。
    /// </summary>
    /// <remarks>
    /// 与桌面端设置页「刷新模型」同一入口：有 API Key 时向远端刷新并写入该用户缓存，失败自动回落本地列表；
    /// 没有只读缓存接口可用，所以首屏 bootstrap 拿到的模型可能是空的（缓存未建），由前端在需要时调用本接口补齐。
    /// </remarks>
    /// <param name="providerId">供应商名（即 <c>ProviderConfig.Name</c>）。</param>
    /// <returns><c>{ providerId, models }</c>。</returns>
    [HttpGet]
    public Task<ActionResult> Models(string? providerId) => WebProviderService.ModelsAsync(providerId);

    /// <summary>切换全局模型选择。</summary>
    /// <param name="payload">包装后的 <see cref="ModelSelectRequest"/> JSON。</param>
    /// <returns>切换后的选择。</returns>
    [HttpPost]
    public Task<ActionResult> Select(string payload) => WebProviderService.SelectAsync(payload);

    /// <summary>列出供应商（API Key 掩码）。</summary>
    /// <returns>供应商清单。</returns>
    [HttpGet]
    public Task<ActionResult> List() => WebProviderService.ListAsync();

    /// <summary>
    /// 新建或更新供应商。
    /// </summary>
    /// <remarks>
    /// 与桌面端设置页同一语义：同名只更新密钥与基址，显示名 / 超时 / 自定义模型按表单差值落盘；
    /// <see cref="ProviderSaveRequest.ApiKey"/> 留空表示沿用已有密钥（新建时不接受留空）。
    /// </remarks>
    /// <param name="payload">包装后的 <see cref="ProviderSaveRequest"/> JSON。</param>
    /// <returns>保存后的供应商项（密钥仍为掩码）。</returns>
    [HttpPost]
    public Task<ActionResult> Save(string payload) => WebProviderService.SaveAsync(payload);

    /// <summary>删除供应商；当前模型属于该供应商时一并清空选择。</summary>
    /// <param name="payload">包装后的 <see cref="ProviderDeleteRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebProviderService.DeleteAsync(payload);
}