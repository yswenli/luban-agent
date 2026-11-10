/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： GatedHttpContext
*描述：全站访问门禁：在 MVC 出结果的唯一入口判定令牌
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 带访问门禁的请求上下文。
/// </summary>
/// <remarks>
/// <para>
/// SAEA.MVC 的每个 GET/POST/PUT/DELETE 请求最终都会落到 <see cref="GetActionResult"/>：
/// 命中路由则交给控制器动作，未命中则按静态文件返回。因此在这里判定令牌，
/// 就能一次覆盖接口、SSE、附件与静态资源，不必在每个控制器里重复校验。
/// </para>
/// <para>
/// 该类型由 <see cref="WebHostGate"/> 在启动时替换进 <c>SAEAMvcApplication</c> 的内部 <c>WebHost</c>，
/// 替换失败会直接抛出启动异常，避免门禁静默失效。
/// </para>
/// </remarks>
public sealed class GatedHttpContext : SAEA.MVC.HttpContext
{
    private const string UnauthorizedHtml =
        "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
        + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        + "<title>需要访问令牌</title></head>"
        + "<body style=\"margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;"
        + "background:#12141a;color:#e6e8ee;font-family:system-ui,-apple-system,'Segoe UI',sans-serif\">"
        + "<main style=\"max-width:32rem;padding:2rem;line-height:1.7\">"
        + "<h1 style=\"font-size:1.25rem;margin:0 0 .75rem\">需要访问令牌</h1>"
        + "<p style=\"margin:0 0 .75rem;color:#a8aec0\">直接打开这个地址不会被放行。"
        + "请到启动宿主的终端，复制其中「浏览器入口」那一整条地址（带 <code>?t=</code> 参数）再打开。</p>"
        + "<p style=\"margin:0;color:#a8aec0\">找不到终端？在宿主机上执行 "
        + "<code style=\"background:#1d212b;padding:.15rem .4rem;border-radius:.25rem\">"
        + "LubanAgentWebHost token</code> 重新打印地址。</p>"
        + "</main></body></html>";

    /// <summary>创建上下文实例（SAEA 按 <c>(IWebHost)</c> 构造）。</summary>
    /// <param name="webHost">宿主。</param>
    public GatedHttpContext(IWebHost webHost)
        : base(webHost)
    {
    }

    /// <summary>
    /// 先做令牌判定，再交回 MVC 的默认处理。
    /// </summary>
    /// <returns>响应结果。</returns>
    public override IHttpResult GetActionResult()
    {
        var expected = AgentWebRuntime.Current.Settings.Token;
        var entryToken = Request.Query.TryGetValue(WebHostAccess.TokenQueryName, out var query) ? query : null;
        var cookieToken = Request.Cookies.TryGetValue(WebHostAccess.AuthCookieName, out var cookie) ? cookie.Value : null;
        var authorization = Request.Headers.TryGetValue("Authorization", out var header) ? header : null;

        var decision = WebHostAccess.Decide(Request.Url, entryToken, cookieToken, authorization, expected);
        if (decision == WebEntryDecision.Allow)
        {
            return base.GetActionResult();
        }

        if (decision == WebEntryDecision.Redirect && !string.IsNullOrEmpty(expected))
        {
            // 令牌已换成 Cookie：地址栏回到干净路径，避免令牌留在历史记录与分享链接里
            Response.Headers["Set-Cookie"] = WebHostAccess.BuildAuthCookie(expected);
            if (!IsReadRequest())
            {
                // 302 会把非 GET/HEAD 改成 GET 并丢掉请求体；这类请求直接按令牌已校验放行
                return base.GetActionResult();
            }

            Response.Headers["Location"] = WebHostAccess.StripTokenQuery(Request.RelativeUrl);
            return new ContentResult(string.Empty, HttpStatusCode.Found);
        }

        return WantsHtml()
            ? new ContentResult(UnauthorizedHtml, HttpStatusCode.Unauthorized)
            : WebApi.FailJson(401, "未授权：缺少或错误的访问令牌");
    }

    private bool IsReadRequest()
        => string.Equals(Request.Method, "GET", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);

    private bool WantsHtml()
        => Request.Headers.TryGetValue("Accept", out var accept)
           && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
}