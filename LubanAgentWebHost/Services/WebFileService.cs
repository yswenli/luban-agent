/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebFileService
*描述：按工作区根目录直出文件原文（会话里点相对链接即看内容）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 工作区文件读取业务。
/// </summary>
/// <remarks>
/// <para>
/// 教育与问答输出里常有 <c>[overview.md](overview.md)</c> 这类相对链接，浏览器按页面地址解析会落到
/// 站点静态根，必然 404。前端把这类链接改写到本接口，由宿主按工作区根目录（含 <c>wiki/</c> 回落）读原文。
/// </para>
/// <para>
/// 只回传工作区内已存在、且经 <see cref="WebFileKinds"/> 判定为文本或图片的文件：路径越界必须在这里拦住
/// （见 <see cref="WebFilePath"/>），未知格式与超限文件一律回「不支持预览」，不把任意本机文件交给浏览器。
/// </para>
/// </remarks>
public static class WebFileService
{
    /// <summary>提示页的响应类型。</summary>
    private const string HtmlContentType = "text/html; charset=utf-8";

    /// <summary>
    /// 读取工作区内文件原文。
    /// </summary>
    /// <param name="workspaceId">工作区标识；空白时取宿主当前工作区。</param>
    /// <param name="path">会话里链接的相对路径。</param>
    /// <returns>纯文本内容，或可用于浏览器直接展示的提示页。</returns>
    public static Task<ActionResult> ReadAsync(string? workspaceId, string? path) => WebApi.RunGuardedAsync(async () =>
    {
        var requestPath = path?.Trim();
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return MessagePage(400, "缺少文件路径", "链接里没有可读取的文件路径。");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var manager = scope.GetRequiredService<IWorkspaceManager>();
        var workspace = string.IsNullOrWhiteSpace(workspaceId)
            ? manager.CurrentWorkspace
            : await manager.GetWorkspaceAsync(workspaceId.Trim()).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(workspace?.RootPath))
        {
            return MessagePage(404, "未找到工作区", "该链接所属的工作区不存在或已被删除，无法读取文件。");
        }

        if (!WebFilePath.TryResolve(workspace.RootPath, requestPath, out var fullPath))
        {
            return MessagePage(
                404,
                "文件不存在",
                $"工作区「{workspace.Name}」中找不到 {requestPath}；若该文件已被移动或删除，请在会话里重新确认路径。");
        }

        var kind = WebFileKinds.Classify(fullPath, out var contentType);
        if (kind == WebFilePreviewKind.Unsupported)
        {
            return MessagePage(
                415,
                "不支持预览",
                $"{requestPath} 不是可预览的文本或图片格式；请在本地打开该文件。");
        }

        try
        {
            var length = new FileInfo(fullPath).Length;
            var limit = WebFileKinds.LimitOf(kind);
            if (length > limit)
            {
                return MessagePage(
                    413,
                    "不支持预览",
                    $"{requestPath} 的体积为 {length / (1024.0 * 1024.0):0.##} MB，超过 {limit / (1024 * 1024)} MB 上限；"
                    + "请在本地打开该文件。");
            }

            if (kind == WebFilePreviewKind.Image)
            {
                var bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
                return new FileResult(new MemoryStream(bytes), contentType, HttpStatusCode.OK);
            }

            var text = await ReadTextAsync(fullPath).ConfigureAwait(false);
            if (text is null)
            {
                return MessagePage(415, "不支持预览", $"{requestPath} 看起来是二进制文件，无法在浏览器中预览。");
            }

            // 会话过程中文件会被改写；ContentResult 由 SAEA 统一带 Cache-Control: no-cache，重新打开即取新内容
            return new ContentResult(text, Encoding.UTF8, WebFileKinds.TextContentType);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 文件被独占、权限不足或读取中途被删除：动作层兜底会回 JSON 壳，浏览器新标签里看不懂
            AgentWebRuntime.Log($"工作区文件预览失败：{fullPath}：{ex}");
            return MessagePage(
                500,
                "无法读取文件",
                $"{requestPath} 读取失败：可能被其他程序占用或权限不足，请在本地打开该文件。");
        }
    });

    /// <summary>
    /// 读取文本正文：按 BOM 识别编码，识别后仍含 NUL 的按二进制处理。
    /// </summary>
    /// <remarks>
    /// 只按 UTF-8 解码并把 NUL 当二进制，会把带 BOM 的 UTF-16 中文文本误判成二进制；
    /// 这里交给 <see cref="StreamReader"/> 做 BOM 探测（UTF-8/UTF-16LE/UTF-16BE）。
    /// </remarks>
    /// <param name="fullPath">绝对路径。</param>
    /// <returns>文本内容；判定为二进制时返回 null。</returns>
    private static async Task<string?> ReadTextAsync(string fullPath)
    {
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        return text.Contains('\0') ? null : text;
    }

    /// <summary>
    /// 生成提示页：浏览器直接打开链接时至少能看懂原因，而不是空白 404。
    /// </summary>
    /// <param name="status">HTTP 状态码。</param>
    /// <param name="title">标题。</param>
    /// <param name="detail">正文说明。</param>
    /// <returns>HTML 结果。</returns>
    private static ActionResult MessagePage(int status, string title, string detail)
    {
        var html = $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
            :root { color-scheme: light dark; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center;
                   font: 14px/1.6 system-ui, -apple-system, "Segoe UI", sans-serif; }
            main { max-width: 34rem; padding: 2rem; }
            h1 { font-size: 1.1rem; margin: 0 0 .75rem; }
            p { margin: 0; opacity: .78; word-break: break-word; }
            </style>
            </head>
            <body>
            <main>
            <h1>{{WebUtility.HtmlEncode(title)}}</h1>
            <p>{{WebUtility.HtmlEncode(detail)}}</p>
            </main>
            </body>
            </html>
            """;

        return new ContentResult(html, Encoding.UTF8, HtmlContentType) { Status = (HttpStatusCode)status };
    }
}