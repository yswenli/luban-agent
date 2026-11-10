/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： SystemController
*描述：系统接口（健康检查、浏览器端首屏引导与命令清单）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 系统接口。
/// </summary>
/// <remarks>
/// 只做路由透传，业务在 <see cref="WebSystemService"/>。
/// </remarks>
public sealed class SystemController : Controller
{
    /// <summary>
    /// 健康检查。
    /// </summary>
    /// <remarks>
    /// 免令牌校验（客户端启动器在拿到令牌前就要探测端口），但仍受来源校验保护；
    /// 不触发用户初始化，避免探测被初始化耗时拖长、也不让初始化失败拖垮端口就绪判定。
    /// </remarks>
    /// <returns>宿主信息。</returns>
    [HttpGet]
    public Task<ActionResult> Health() => WebSystemService.HealthAsync();

    /// <summary>
    /// 浏览器端首屏数据：工作区、供应商与模型、权限模式、附件与确认策略。
    /// </summary>
    /// <returns>引导数据。</returns>
    [HttpGet]
    public Task<ActionResult> Bootstrap() => WebSystemService.BootstrapAsync();

    /// <summary>
    /// 浏览器端斜杠命令清单（过滤掉 CLI 专属命令与「退出程序」）。
    /// </summary>
    /// <returns>命令清单。</returns>
    [HttpGet]
    public Task<ActionResult> Commands() => WebSystemService.CommandsAsync();
}