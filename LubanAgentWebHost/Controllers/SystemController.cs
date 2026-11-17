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

    /// <summary>
    /// 仓库 README 原文（浏览器端「帮助」弹窗展示）。
    /// </summary>
    /// <returns>Markdown 原文。</returns>
    [HttpGet]
    public Task<ActionResult> Readme() => WebSystemService.ReadmeAsync();

    /// <summary>
    /// 局域网入口二维码（PNG）：手机扫码直接打开本实例。
    /// </summary>
    /// <returns>PNG 图片流。</returns>
    [HttpGet]
    public Task<ActionResult> EntryQr() => WebSystemService.EntryQrAsync();

    /// <summary>
    /// 浏览器会话状态（前端心跳；顺便触发会话令牌轮换）。
    /// </summary>
    /// <returns>令牌与会话的到期时刻。</returns>
    [HttpGet]
    public Task<ActionResult> Session() => WebSystemService.SessionAsync();

    /// <summary>
    /// 当前入口地址（终端 <c>token</c> 命令用，只认固定令牌）。
    /// </summary>
    /// <returns>入口地址与会话到期时刻。</returns>
    [HttpGet]
    public Task<ActionResult> EntryUrl() => WebSystemService.EntryUrlAsync();
}