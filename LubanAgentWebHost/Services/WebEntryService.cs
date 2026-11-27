/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebEntryService
*描述：入口短链业务：把 /e/{code} 换回带会话令牌的地址
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 入口短链业务。
/// </summary>
/// <remarks>
/// 短码只在本进程内有效，换回的地址仍是 <c>/?t=&lt;令牌&gt;</c>，
/// 随后的「令牌换 Cookie、跳回干净地址」完全复用全站门禁，不在这里重复实现。
/// </remarks>
public static class WebEntryService
{
    /// <summary>短码无效或已过期时的提示页。</summary>
    private static readonly string InvalidHtml = """
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>入口链接已失效 · LuBan Agent</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body {
    margin: 0; min-height: 100vh; padding: 32px 20px;
    display: flex; align-items: center; justify-content: center;
    background: #12141a; color: #e6e8ee;
    font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
  }
  main {
    width: 100%; max-width: 34rem; line-height: 1.75;
    background: #171a21; border: 1px solid #262b36; border-radius: 12px; padding: 20px 22px;
  }
  h1 { font-size: 1.25rem; margin: 0 0 .75rem; }
  p { margin: 0 0 .75rem; color: #a8aec0; }
  code { background: #1d212b; padding: .15rem .4rem; border-radius: .25rem; font-size: .92em; }
</style>
</head>
<body>
<main>
  <h1>入口链接已失效</h1>
  <p>短链只在本次宿主运行期间有效，并与浏览器会话令牌同时到期。</p>
  <p>请在宿主机执行 <code>LubanAgentWebHost --token</code> 取新的入口地址与二维码。</p>
</main>
</body>
</html>
""";

    /// <summary>
    /// 用短码换回入口地址：命中时 302 到 <c>/?t=&lt;令牌&gt;</c>，未命中时返回提示页。
    /// </summary>
    /// <param name="code">路径里的短码。</param>
    /// <returns>302 或 410。</returns>
    public static Task<ActionResult> OpenAsync(string? code)
    {
        if (!WebApi.Runtime.EntryLinks.TryResolve(code, out var token) || string.IsNullOrEmpty(token))
        {
            return Task.FromResult<ActionResult>(
                new ContentResult(InvalidHtml, Encoding.UTF8, "text/html; charset=utf-8")
                {
                    Status = HttpStatusCode.Gone
                });
        }

        WebApi.Response.Headers["Location"] =
            $"/?{WebHostAccess.TokenQueryName}={Uri.EscapeDataString(token)}";
        return Task.FromResult<ActionResult>(new ContentResult(string.Empty, HttpStatusCode.Found));
    }
}