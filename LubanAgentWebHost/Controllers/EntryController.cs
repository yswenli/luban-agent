/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： EntryController
*描述：入口短链路由：/e/{code} 换回带会话令牌的地址
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 入口短链接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebEntryService"/>；
/// 该路径在门禁里免令牌（短码本身就是凭据），命中后统一跳回 <c>/?t=</c>。
/// </remarks>
public sealed class EntryController : Controller
{
    /// <summary>
    /// 打开入口短链。
    /// </summary>
    /// <param name="code">短码。</param>
    /// <returns>302 到带令牌的入口地址；短码无效或过期时为 410 提示页。</returns>
    [HttpGet("e/{code}")]
    public Task<ActionResult> Open(string code) => WebEntryService.OpenAsync(code);
}