/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： AttachmentController
*描述：浏览器端附件上传（JSON + Base64，落盘到工作区暂存目录）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 附件上传接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebAttachmentService"/>。
/// 不走 multipart：SAEA 的 multipart 解析随体积超线性变慢，故客户端改传 JSON + Base64。
/// </remarks>
public sealed class AttachmentController : Controller
{
    /// <summary>
    /// 上传附件。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="AttachmentUploadRequest"/> JSON。</param>
    /// <returns>已落盘与被拒文件清单。</returns>
    [HttpPost]
    public Task<ActionResult> Upload(string payload) => WebAttachmentService.UploadAsync(payload);
}