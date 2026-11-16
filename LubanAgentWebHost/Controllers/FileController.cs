/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： FileController
*描述：工作区文件原文接口（会话内相对链接点击后打开）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 工作区文件接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebFileService"/>。
/// </remarks>
public sealed class FileController : Controller
{
    /// <summary>读取工作区内文件原文。</summary>
    /// <param name="workspaceId">工作区标识；空白时取当前工作区。</param>
    /// <param name="path">相对工作区根的路径（链接原值，可带 <c>./</c>、查询串或锚点）。</param>
    /// <returns>纯文本内容，或浏览器可直接展示的提示页。</returns>
    [HttpGet]
    public Task<ActionResult> Read(string? workspaceId, string? path) => WebFileService.ReadAsync(workspaceId, path);
}