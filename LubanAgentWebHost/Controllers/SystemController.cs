/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： SystemController
*描述：系统接口（健康检查，供客户端启动器探测就绪）
*
*****************************************************************************/

using System.Reflection;

using LubanAgentCore.Protocol.Dtos;

using LubanAgentWebHost.Hosting;

using SAEA.MVC;

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 系统接口。
/// </summary>
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
    public Task<ActionResult> Health() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync(requireToken: false, ensureUser: false) is { } denied)
        {
            return denied;
        }

        return WebApi.OkJson(new HealthDto(
            "ok",
            typeof(SystemController).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            WebApi.Runtime.UserId,
            DateTime.Now));
    });
}