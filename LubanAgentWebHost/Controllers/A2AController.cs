/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： A2AController
*描述：A2A 协作状态接口（只读）
*
*****************************************************************************/

using LubanAgentCore.Protocol;

using LubanAgentWebHost.Hosting;

using SAEA.MVC;

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// A2A 协作状态接口。
/// </summary>
/// <remarks>
/// A2A 运行时随宿主进程启动与退出，客户端不提供启停，故这里只有只读状态。
/// </remarks>
public sealed class A2AController : Controller
{
    /// <summary>读取 A2A 运行状态。</summary>
    /// <returns>状态快照。</returns>
    [HttpGet]
    public Task<ActionResult> Status() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var status = await scope.GetRequiredService<IA2AStatusProvider>()
            .GetStatusAsync()
            .ConfigureAwait(false);
        return WebApi.OkJson(status);
    });
}