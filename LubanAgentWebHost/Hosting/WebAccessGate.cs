/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebAccessGate
*描述：全站访问门禁的判定：公开页面、宿主固定令牌与用户 JWT
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 访问门禁的判定规则（纯逻辑，便于单测）。
/// </summary>
/// <remarks>
/// <para>
/// 两类凭据分工不变：<c>Authorization: Bearer</c> 仍是进程内固定的宿主令牌，给 CLI / Codex / 插件用；
/// 浏览器改用登录后签发的 JWT（<c>luban_auth</c> Cookie 或 <c>?t=</c>），身份落在令牌声明里。
/// </para>
/// <para>
/// 登录页、静态资源与认证接口必须免令牌，否则首次访问连登录界面都加载不出来；
/// 其余接口与插件页一律要求有效凭据。
/// </para>
/// </remarks>
public static class WebAccessGate
{
    private static readonly HashSet<string> ReadOnlyPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/index.html",
        "/runtime-config.js",
        "/favicon.ico",
        "/system/health"
    };

    /// <summary>认证接口：登录/注册/登出必须免令牌，否则拿不到令牌就没人能登录。</summary>
    private static readonly HashSet<string> PublicAuthPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/auth/status",
        "/auth/login",
        "/auth/register",
        "/auth/logout"
    };

    /// <summary>
    /// 公开前缀：前端构建产物与入口短链。
    /// </summary>
    /// <remarks>
    /// 短链本身就是凭据：没有短码的人拿不到跳转目标，命中后才换成 <c>?t=</c> 走正常门禁。
    /// </remarks>
    private static readonly string[] PublicPrefixes =
    [
        "/assets/",
        "/e/"
    ];

    /// <summary>
    /// 本次请求是否无需登录即可访问。
    /// </summary>
    /// <param name="url">请求路径（可含查询串）。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <returns>公开返回 true。</returns>
    public static bool IsPublic(string? url, string? method)
    {
        var path = NormalizePath(url);
        if (path.Length == 0)
        {
            return false;
        }

        if (PublicAuthPaths.Contains(path))
        {
            return true;
        }

        // 静态壳只接受读请求：避免把任意 POST 直接放进 SPA 根
        var read = string.IsNullOrEmpty(method)
                   || string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
        if (!read)
        {
            return false;
        }

        if (ReadOnlyPaths.Contains(path))
        {
            return true;
        }

        return PublicPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判定一次请求是否放行。
    /// </summary>
    /// <param name="url">请求路径（可含查询串）。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="entryToken">地址栏 <c>?t=</c>；未携带为 null。</param>
    /// <param name="cookieToken">Cookie 里的 JWT；未携带为 null。</param>
    /// <param name="authorization"><c>Authorization</c> 头原值；未携带为 null。</param>
    /// <param name="hostToken">进程内固定的宿主令牌。</param>
    /// <param name="auth">用户认证服务。</param>
    /// <param name="claims">放行时的用户声明；宿主令牌或公开页面为 null。</param>
    /// <param name="issueToken">需要回写 Cookie 的 JWT；无需回写时为 null。</param>
    /// <returns>判定结果。</returns>
    public static WebEntryDecision Decide(
        string? url,
        string? method,
        string? entryToken,
        string? cookieToken,
        string? authorization,
        string? hostToken,
        UserAuthService auth,
        out WebTokenClaims? claims,
        out string? issueToken)
    {
        claims = null;
        issueToken = null;

        // 公开页面只做「顺手认人」：登录页要知道当前是否已登录，但一律不用令牌挡路。
        var isPublic = IsPublic(url, method);

        // 客户端（CLI / Codex / 插件）用固定令牌：不受浏览器登录会话影响
        if (WebHostAccess.IsBearerAuthorized(authorization, hostToken))
        {
            return WebEntryDecision.Allow;
        }

        // 地址栏带了令牌就只认它：带错令牌不该靠旧 Cookie 蒙混过关
        if (!string.IsNullOrEmpty(entryToken))
        {
            if (auth.TryValidateToken(entryToken, out claims))
            {
                issueToken = entryToken;
                return WebEntryDecision.Redirect;
            }

            if (isPublic)
            {
                // 过期或伪造的入口地址不阻断登录页；地址栏的坏令牌随后被前端忽略
                claims = null;
                return WebEntryDecision.Allow;
            }

            // 签名正确但已过期：提示重新登录，而不是当成伪造令牌
            return auth.TryValidateToken(entryToken, out _, ignoreExpiry: true)
                ? WebEntryDecision.Expired
                : WebEntryDecision.Unauthorized;
        }

        if (auth.TryValidateToken(cookieToken, out claims) && claims is not null)
        {
            // 活动页面在令牌过半时换新，等价于旧实现的"轮换 + 宽限"
            var remaining = claims.ExpiresAt - auth.Now;
            if (remaining <= auth.TokenLifetime / 2)
            {
                issueToken = auth.RenewToken(claims);
            }

            return WebEntryDecision.Allow;
        }

        if (isPublic)
        {
            claims = null;
            return WebEntryDecision.Allow;
        }

        return auth.TryValidateToken(cookieToken, out _, ignoreExpiry: true)
            ? WebEntryDecision.Expired
            : WebEntryDecision.Unauthorized;
    }

    /// <summary>把请求路径归一化：去掉查询串、折叠尾斜杠（根路径保留）。</summary>
    /// <param name="url">请求路径。</param>
    /// <returns>归一化后的路径。</returns>
    public static string NormalizePath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var path = url.Trim();
        var query = path.IndexOf('?');
        if (query >= 0)
        {
            path = path[..query];
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        return path.Length == 0 ? "/" : path;
    }
}