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

    /// <summary>下载工作区内文件（不受预览白名单与体积上限限制）。</summary>
    /// <param name="workspaceId">工作区标识；空白时取当前工作区。</param>
    /// <param name="path">相对工作区根的文件路径（链接原值，可带 <c>./</c>、查询串或锚点）。</param>
    /// <returns>文件二进制流；路径越界或文件不存在时回提示页。</returns>
    [HttpGet]
    public Task<ActionResult> Download(string? workspaceId, string? path) => WebFileService.DownloadAsync(workspaceId, path);

    /// <summary>列举工作区内某目录的直接子项（不递归，越界路径拒绝）。</summary>
    /// <param name="workspaceId">工作区标识；空白时取当前工作区。</param>
    /// <param name="path">相对工作区根的目录路径；空白表示工作区根。</param>
    /// <param name="showIgnored">是否包含默认忽略的构建产物目录（<c>1</c>/<c>true</c> 为包含）。</param>
    /// <returns>子目录与文件清单。</returns>
    [HttpGet]
    public Task<ActionResult> List(string? workspaceId, string? path, string? showIgnored)
        => WebFileService.ListAsync(workspaceId, path, showIgnored);
}