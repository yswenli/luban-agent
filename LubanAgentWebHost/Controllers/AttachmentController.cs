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
/// <para>
/// 不走 multipart：SAEA 的 multipart 解析随体积超线性变慢（1MB 约 4 秒、5MB 约 77 秒），
/// 同样的文件走 JSON 只需约 2 秒。代价是请求体约为文件体积的 1.37 倍。
/// </para>
/// <para>
/// 只写工作区固定子目录 <c>.luban-agent/temp/uploads/&lt;yyyyMMdd&gt;/</c>，
/// 不接受调用方指定路径。
/// </para>
/// </remarks>
public sealed class AttachmentController : Controller
{
    /// <summary>文本类附件单文件上限（与框架附件处理器的阈值一致）。</summary>
    private const long MaxTextBytes = 10L * 1024 * 1024;

    /// <summary>
    /// 上传附件。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="AttachmentUploadRequest"/> JSON。</param>
    /// <returns>已落盘与被拒文件清单。</returns>
    [HttpPost]
    public Task<ActionResult> Upload(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<AttachmentUploadRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        var files = input.Files ?? Array.Empty<AttachmentUploadFile>();
        if (files.Count == 0)
        {
            return WebApi.FailJson(400, "files 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);

        var workspaceManager = scope.GetRequiredService<IWorkspaceManager>();
        WorkspaceInfo? workspace = null;
        if (!string.IsNullOrWhiteSpace(input.SessionId))
        {
            var workspaceId = await scope.GetRequiredService<ISessionWorkspaceQuery>()
                .GetSessionWorkspaceIdAsync(input.SessionId.Trim(), WebApi.Runtime.UserId)
                .ConfigureAwait(false);
            if (workspaceId is null)
            {
                return WebApi.FailJson(404, "会话不存在或未绑定工作区");
            }

            workspace = await workspaceManager.GetWorkspaceAsync(workspaceId).ConfigureAwait(false);
        }

        workspace ??= workspaceManager.CurrentWorkspace;
        if (workspace is null)
        {
            return WebApi.FailJson(400, "尚未选择工作区，无法暂存附件");
        }

        var directory = Path.Combine(
            workspace.RootPath,
            string.IsNullOrWhiteSpace(workspace.ConfigPath) ? ".luban-agent" : workspace.ConfigPath!,
            "temp",
            "uploads",
            DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(directory);

        var accepted = new List<AcceptedAttachmentDto>();
        var rejected = new List<RejectedAttachmentDto>();

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var originalName = string.IsNullOrWhiteSpace(file.FileName) ? "未命名文件" : file.FileName.Trim();

            // 超出本批次上限：明确拒绝，不做静默截断
            if (index >= AttachmentService.MaxAttachmentsPerMessage)
            {
                rejected.Add(new RejectedAttachmentDto(
                    originalName,
                    $"超出单次上限（最多 {AttachmentService.MaxAttachmentsPerMessage} 个）"));
                continue;
            }

            var safeName = SafeFileName(originalName);
            var extension = Path.GetExtension(safeName);

            if (string.IsNullOrEmpty(extension)
                || !MediaTypeUtil.ExtensionToMediaType.ContainsKey(extension))
            {
                rejected.Add(new RejectedAttachmentDto(originalName, "不支持的文件类型"));
                continue;
            }

            if (MediaTypeUtil.IsHeicLike(safeName))
            {
                rejected.Add(new RejectedAttachmentDto(originalName, "HEIC/HEIF 无法解码，请先转换为 PNG 或 JPEG"));
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(file.ContentBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                rejected.Add(new RejectedAttachmentDto(originalName, "文件内容不是合法的 Base64"));
                continue;
            }

            if (bytes.Length == 0)
            {
                rejected.Add(new RejectedAttachmentDto(originalName, "文件内容为空"));
                continue;
            }

            if (!MediaTypeUtil.IsImage(safeName) && bytes.LongLength > MaxTextBytes)
            {
                rejected.Add(new RejectedAttachmentDto(originalName, "文本类附件超过 10MB"));
                continue;
            }

            var storedName = $"{Guid.NewGuid():N}"[..8] + "_" + safeName;
            var path = Path.Combine(directory, storedName);
            await System.IO.File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);

            accepted.Add(new AcceptedAttachmentDto(
                path,
                storedName,
                MediaTypeUtil.ResolveMediaType(safeName),
                bytes.LongLength));
        }

        return WebApi.OkJson(new AttachmentUploadResult(accepted, rejected));
    });

    /// <summary>
    /// 去掉路径与非法字符，限制长度；只保留文件名本身。
    /// </summary>
    /// <param name="fileName">调用方给的文件名。</param>
    /// <returns>可安全落盘的文件名。</returns>
    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            return "attachment";
        }

        if (name.Length <= 120)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        return stem[..Math.Min(80, stem.Length)] + extension;
    }
}