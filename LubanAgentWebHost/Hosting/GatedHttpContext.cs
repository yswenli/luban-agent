/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： GatedHttpContext
*描述：全站访问门禁：在 MVC 出结果的唯一入口判定令牌
*
*****************************************************************************/

using SAEA.Sockets.Interface;

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
    /// <summary>无令牌提示页（浏览器直接访问时返回，纯 HTML 不依赖前端脚本）。</summary>
    private static readonly string UnauthorizedHtml = BuildUnauthorizedHtml();

    /// <summary>创建上下文实例（SAEA 按 <c>(IWebHost)</c> 构造）。</summary>
    /// <param name="webHost">宿主。</param>
    public GatedHttpContext(IWebHost webHost)
        : base(webHost)
    {
    }

    /// <summary>本次请求所属的连接载体（SSE 写出前据此判定对端是否还在）。</summary>
    public IUserToken? Connection { get; private set; }

    /// <summary>
    /// 记录连接载体后交回 MVC 的默认处理。
    /// </summary>
    /// <param name="userToken">本次请求的连接载体。</param>
    /// <param name="httpMessage">已解析的请求报文。</param>
    public override void HttpHandle(IUserToken userToken, HttpMessage httpMessage)
    {
        Connection = userToken;
        base.HttpHandle(userToken, httpMessage);
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

    /// <summary>
    /// 生成无令牌提示页：正文之外附「帮助」与「关于」，让拿不到令牌的用户也能自行排查。
    /// </summary>
    /// <remarks>
    /// 该页在门禁拦截时直接返回，拿不到任何接口数据，故作者、仓库与版本只能内置（版本取宿主程序集）。
    /// </remarks>
    /// <returns>完整 HTML。</returns>
    private static string BuildUnauthorizedHtml()
    {
        var assembly = typeof(GatedHttpContext).Assembly;
        // InformationalVersion 形如 1.0.0+<commit>，页面只显示语义版本
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+')[0]
                      ?? assembly.GetName().Version?.ToString()
                      ?? "unknown";

        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>需要访问令牌 · LuBan Agent</title>
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
  footer { color: #7b8194; font-size: .8rem; text-align: center; }
</style>
</head>
<body>
<main>
  <h1>需要访问令牌</h1>
  <p>直接打开这个地址不会被放行。</p>
  <details open>
    <summary>帮助</summary>
    <p>请到启动宿主的终端，复制其中「浏览器入口」那一整条地址（带 <code>?t=</code> 参数）再打开。</p>
    <ul>
      <li>找不到终端？在宿主机上执行 <code>LubanAgentWebHost token</code> 重新打印地址。</li>
      <li>令牌随本次运行有效：宿主重启后地址会变，旧书签需要替换。</li>
      <li>停止常驻实例：<code>LubanAgentWebHost --stop</code>。</li>
      <li>带令牌的地址等同于完整操作权限，不要转发给他人。</li>
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
<footer>Luban Agent @ yswenli</footer>
</body>
</html>
""";
    }

    private bool IsReadRequest()
        => string.Equals(Request.Method, "GET", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);

    private bool WantsHtml()
        => Request.Headers.TryGetValue("Accept", out var accept)
           && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
}