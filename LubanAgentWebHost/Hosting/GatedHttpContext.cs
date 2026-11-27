/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： GatedHttpContext
*描述：全站访问门禁：在 MVC 路由与静态文件解析之前判定令牌
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 带访问门禁的请求上下文。
/// </summary>
/// <remarks>
/// <para>
/// SAEA.MVC 在请求解析完成后、路由之前调用 <see cref="OnRequestExecutingAsync"/>：
/// 命中路由则交给控制器动作，未命中则按静态文件返回。因此在这里判定令牌，
/// 就能一次覆盖接口、SSE、附件与静态资源，不必在每个控制器里重复校验。
/// </para>
/// <para>
/// 该类型由 <c>SAEAMvcApplicationConfig.ContextType</c> 在启动前指定，等价于把门禁放在路由与静态文件之前的中间件，
/// 不需要反射改写 SAEA 的内部字段。
/// </para>
/// </remarks>
public sealed class GatedHttpContext : SAEA.MVC.Hosting.HttpContext
{
    /// <summary>无令牌提示页（浏览器直接访问时返回，纯 HTML 不依赖前端脚本）。</summary>
    private static readonly string UnauthorizedHtml = BuildUnauthorizedHtml(expired: false);

    /// <summary>会话到期提示页（浏览器会话超过绝对上限后返回）。</summary>
    private static readonly string ExpiredHtml = BuildUnauthorizedHtml(expired: true);

    /// <summary>创建上下文实例（SAEA 按 <c>(IWebHost)</c> 构造）。</summary>
    /// <param name="webHost">宿主。</param>
    public GatedHttpContext(IWebHost webHost)
        : base(webHost)
    {
    }

    /// <summary>本次请求所属的连接通道（SSE 写出前据此判定对端是否还在）。</summary>
    public IChannel? Connection { get; private set; }

    /// <summary>本次请求的用户标识；未登录（公开页面）时为 null。</summary>
    public string? UserId { get; private set; }

    /// <summary>本次请求携带的 JWT 声明；宿主固定令牌与公开页面为 null。</summary>
    public WebTokenClaims? Claims { get; private set; }

    /// <summary>本次请求是否由 <c>Authorization: Bearer</c>（宿主固定令牌）放行。</summary>
    public bool IsBearer { get; private set; }

    /// <summary>
    /// 记录连接通道后交回 MVC 的默认处理。
    /// </summary>
    /// <remarks>
    /// 同步入口 <c>HttpHandle</c> 内部也转到本方法，因此两条路径都能记录到连接；
    /// 上下文按连接复用，每条请求都会重写该值。
    /// </remarks>
    /// <param name="channel">本次请求的连接通道。</param>
    /// <param name="httpMessage">已解析的请求报文。</param>
    /// <returns>响应写出完成后的任务。</returns>
    public override Task HttpHandleAsync(IChannel channel, HttpMessage httpMessage)
    {
        Connection = channel;
        return base.HttpHandleAsync(channel, httpMessage);
    }

    /// <summary>
    /// 把本次连接从 SAEA 的会话空闲回收中摘出（SSE 长连接专用）。
    /// </summary>
    /// <remarks>
    /// SAEA 只在收到数据时刷新会话活跃时间，服务端单向下发的心跳不算「活跃」；
    /// 因此 <c>LubanAgentWeb:TimeoutSeconds</c> 一旦调小，进行中的 SSE 会在该时长后被静默切断。
    /// 这里用 SAEA 的 <c>SetNoTimeout</c> 把长连接排除在空闲回收之外，对端是否还在交给写出泵判定。
    /// </remarks>
    public void DisableIdleTimeout()
    {
        if (_webHost is WebHost webHost && Connection is { } connection)
        {
            webHost.SetNoTimeout(connection.Id, true);
        }
    }

    /// <summary>
    /// 路由前做令牌判定：未命中令牌时返回接管结果，放行时返回 null 交给 MVC 正常路由。
    /// </summary>
    /// <returns>接管结果；放行时为 null。</returns>
    public override Task<IHttpResult> OnRequestExecutingAsync()
    {
        UserId = null;
        Claims = null;
        IsBearer = false;

        var runtime = AgentWebRuntime.Current;
        var entryToken = Request.Query.TryGetValue(WebHostAccess.TokenQueryName, out var query) ? query : null;
        var cookieToken = Request.Cookies.TryGetValue(WebHostAccess.AuthCookieName, out var cookie) ? cookie.Value : null;
        var authorization = Request.Headers.TryGetValue("Authorization", out var header) ? header : null;

        // 写进 Cookie 的 Secure 跟「请求实际协议」而不是宿主配置：反向代理终结 TLS 时
        // 宿主自身讲的是明文，只有 X-Forwarded-Proto 才能说明浏览器那边是 https
        var secureCookie = string.Equals(
            WebRequestScheme.Resolve(Request, runtime.Settings.UseHttps),
            WebRequestScheme.Https,
            StringComparison.Ordinal);

        var decision = WebAccessGate.Decide(
            Request.Url,
            Request.Method,
            entryToken,
            cookieToken,
            authorization,
            runtime.Settings.Token,
            runtime.Auth,
            out var claims,
            out var issueToken);

        if (decision == WebEntryDecision.Allow)
        {
            // 宿主固定令牌：沿用进程级身份（CLI / Codex / 插件）
            if (WebHostAccess.IsBearerAuthorized(authorization, runtime.Settings.Token))
            {
                UserId = runtime.UserId;
                IsBearer = true;
            }
            else if (claims is not null)
            {
                UserId = claims.UserId;
                Claims = claims;
            }

            // 令牌过半时续期：活动页面因此不必刷新就能一直续下去
            if (!string.IsNullOrEmpty(issueToken)
                && !string.Equals(issueToken, cookieToken, StringComparison.Ordinal))
            {
                Response.Headers["Set-Cookie"] = WebHostAccess.BuildAuthCookie(issueToken, secureCookie);
            }

            return ContinueRouting();
        }

        if (decision == WebEntryDecision.Redirect && !string.IsNullOrEmpty(issueToken))
        {
            // 令牌已换成 Cookie：地址栏回到干净路径，避免令牌留在历史记录与分享链接里
            Response.Headers["Set-Cookie"] = WebHostAccess.BuildAuthCookie(issueToken, secureCookie);
            if (!IsReadRequest())
            {
                // 302 会把非 GET/HEAD 改成 GET 并丢掉请求体；这类请求直接按令牌已校验放行
                UserId = claims?.UserId;
                Claims = claims;
                return ContinueRouting();
            }

            Response.Headers["Location"] = WebHostAccess.StripTokenQuery(Request.RelativeUrl);
            return Task.FromResult<IHttpResult>(new ContentResult(string.Empty, HttpStatusCode.Found));
        }

        var expired = decision == WebEntryDecision.Expired;
        return Task.FromResult<IHttpResult>(WantsHtml()
            ? new ContentResult(expired ? ExpiredHtml : UnauthorizedHtml, HttpStatusCode.Unauthorized)
            : WebApi.FailJson(401, expired
                ? "未授权：登录已过期，请重新登录"
                : "未授权：请先登录"));
    }

    /// <summary>
    /// 放行本次请求：返回 null 让 MVC 继续合并参数并正常路由。
    /// </summary>
    /// <returns>值为 null 的结果任务。</returns>
    private static Task<IHttpResult> ContinueRouting()
    {
        return Task.FromResult<IHttpResult>(null!);
    }

    /// <summary>
    /// 生成无令牌/会话过期提示页：正文之外附「帮助」与「关于」，让拿不到令牌的用户也能自行排查。
    /// </summary>
    /// <remarks>
    /// 该页在门禁拦截时直接返回，拿不到任何接口数据，故作者、仓库与版本只能内置（版本取宿主程序集）。
    /// </remarks>
    /// <param name="expired">是否为「会话已过绝对上限」而非「缺令牌」。</param>
    /// <returns>完整 HTML。</returns>
    private static string BuildUnauthorizedHtml(bool expired)
    {
        var assembly = typeof(GatedHttpContext).Assembly;
        // InformationalVersion 形如 1.0.0+<commit>，页面只显示语义版本
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+')[0]
                      ?? assembly.GetName().Version?.ToString()
                      ?? "unknown";

        var favicon = BuildFaviconLink();
        var heading = expired ? "会话已过期" : "需要访问令牌";
        var lead = expired
            ? "浏览器会话已超过绝对上限，之前那个入口地址不再放行。"
            : "直接打开这个地址不会被放行。";

        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
{{favicon}}
<title>{{heading}} · LuBan Agent</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body {
    margin: 0; min-height: 100vh; padding: 32px 20px;
    display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px;
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
  details { margin-top: 14px; border-top: 1px solid #262b36; padding-top: 12px; }
  summary { cursor: pointer; color: #cdd3e1; font-weight: 600; font-size: .95rem; }
  details p, details ul { color: #a8aec0; font-size: .92rem; }
  details ul { margin: .5rem 0 0; padding-left: 1.2rem; }
  details li { margin: .25rem 0; }
  a { color: #9bb0ff; text-decoration: none; }
  a:hover { text-decoration: underline; }
</style>
</head>
<body>
<main>
  <h1>{{heading}}</h1>
  <p>{{lead}}</p>
  <details open>
    <summary>帮助</summary>
    <p>请先登录。浏览器入口地址不再携带永久令牌，登录成功后由宿主下发短期会话凭据。</p>
    <ul>
      <li>不知道密码？内置管理员初始密码在首次启动宿主的终端里只打印一次。</li>
      <li>忘记密码：请联系管理员在「设置 → 用户」重置，或由管理员在宿主机执行 <code>LubanAgentWebHost --token</code> 取管理员入口地址。</li>
      <li>浏览器会话会持续续期，但默认 24 小时后需重新登录（<code>SessionMaxHours</code> 可调）。</li>
      <li>停止常驻实例：<code>LubanAgentWebHost --stop</code>。</li>
      <li>登录凭据等同于完整操作权限，不要转发给他人。</li>
    </ul>
  </details>
  <details>
    <summary>关于</summary>
    <p>LuBan Agent——基于 Microsoft Agent Framework 的 AI Agent 解决方案：一套核心运行时，命令行 TUI、跨平台桌面客户端与本地 Agent 服务三端统一。</p>
    <ul>
      <li>版本：v{{version}}</li>
      <li>仓库：<a href="https://github.com/yswenli/luban-agent" target="_blank" rel="noreferrer">github.com/yswenli/luban-agent</a></li>
      <li>作者：yswenli · <a href="https://github.com/yswenli" target="_blank" rel="noreferrer">@yswenli</a> · <a href="mailto:yswenli@outlook.com">yswenli@outlook.com</a></li>
      <li>许可：MIT License</li>
    </ul>
  </details>
</main>
</body>
</html>
""";
    }

    /// <summary>
    /// 生成 401 页的站点图标标签。
    /// </summary>
    /// <remarks>
    /// 该页返回后浏览器还会单独拉取图标，而那次请求同样没有令牌、同样会被门禁拦成 401 HTML，
    /// 所以不能指向 <c>/assets/favicon-*.ico</c> 这类站点路径；这里把构建时拷到
    /// <c>wwwroot/favicon.ico</c> 的图标编码成 data URI 直接内联。文件缺失时返回空串，页面只是少个图标。
    /// </remarks>
    /// <returns><c>&lt;link rel="icon"&gt;</c> 标签；图标不可用时为空串。</returns>
    private static string BuildFaviconLink()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "favicon.ico");
            if (!File.Exists(path)) return string.Empty;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return string.Empty;

            var base64 = Convert.ToBase64String(bytes);
            return $"""<link rel="icon" type="image/x-icon" href="data:image/x-icon;base64,{base64}">""";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private bool IsReadRequest()
        => string.Equals(Request.Method, "GET", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);

    private bool WantsHtml()
        => Request.Headers.TryGetValue("Accept", out var accept)
           && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
}