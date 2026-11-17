/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebHostAccessUnitTest
*描述：宿主访问门禁的令牌判定、入口地址与跳转目标
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class WebHostAccessUnitTest
{
    /// <summary>判定一次请求（浏览器会话令牌与固定令牌都给齐）。</summary>
    /// <param name="url">请求路径。</param>
    /// <param name="entryToken">地址栏 <c>?t=</c>。</param>
    /// <param name="cookieToken">Cookie 里的会话令牌。</param>
    /// <param name="authorization"><c>Authorization</c> 头。</param>
    /// <param name="bearerToken">宿主固定令牌。</param>
    /// <param name="sessions">会话令牌状态机。</param>
    /// <returns>判定结果。</returns>
    private static WebEntryDecision Decide(
        string? url,
        string? entryToken,
        string? cookieToken,
        string? authorization,
        string? bearerToken,
        WebSessionTokens? sessions)
        => WebHostAccess.Decide(url, entryToken, cookieToken, authorization, bearerToken, sessions, out _);

    [TestMethod]
    public void 健康检查不需要令牌()
    {
        Assert.AreEqual(WebEntryDecision.Allow, Decide("/system/health", null, null, null, "tok", null));
        Assert.AreEqual(WebEntryDecision.Allow, Decide("/system/health/", null, null, null, "tok", null));
        Assert.AreEqual(WebEntryDecision.Allow, Decide("/SYSTEM/HEALTH", null, null, null, "tok", null));
    }

    [TestMethod]
    public void 没有凭据时一律拒绝()
    {
        var sessions = new WebSessionTokens();
        Assert.AreEqual(WebEntryDecision.Unauthorized, Decide("/", null, null, null, "tok", sessions));
        Assert.AreEqual(WebEntryDecision.Unauthorized, Decide("/session/list", null, null, null, "tok", sessions));
        Assert.AreEqual(WebEntryDecision.Unauthorized, Decide("/assets/index.js", null, null, null, "tok", sessions));
    }

    [TestMethod]
    public void 地址栏令牌正确时换发凭据并跳转()
    {
        var sessions = new WebSessionTokens(initialToken: "sess");
        Assert.AreEqual(WebEntryDecision.Redirect, Decide("/", "sess", null, null, "tok", sessions));
        Assert.AreEqual(WebEntryDecision.Redirect, Decide("/session/list", "sess", null, null, "tok", sessions));
    }

    [TestMethod]
    public void 地址栏令牌错误时不被旧凭据救回()
    {
        var sessions = new WebSessionTokens(initialToken: "sess");

        Assert.AreEqual(
            WebEntryDecision.Unauthorized,
            Decide("/", "bad", "sess", null, "tok", sessions),
            "带错令牌应当直接拒绝，避免把错误链接当成已登录");

        Assert.AreEqual(
            WebEntryDecision.Allow,
            Decide("/", "bad", null, "Bearer tok", "tok", sessions),
            "Bearer 是客户端凭据，地址栏参数写错不该影响已经确认身份的客户端");
    }

    [TestMethod]
    public void Cookie与Bearer都能放行()
    {
        var sessions = new WebSessionTokens(initialToken: "sess");
        Assert.AreEqual(WebEntryDecision.Allow, Decide("/session/list", null, "sess", null, "tok", sessions));
        Assert.AreEqual(WebEntryDecision.Allow, Decide("/session/list", null, null, "Bearer tok", "tok", null));
        Assert.AreEqual(WebEntryDecision.Allow, Decide("/session/list", null, null, "bearer  tok  ", "tok", null));
    }

    [TestMethod]
    public void 固定令牌不认浏览器会话令牌()
    {
        var sessions = new WebSessionTokens(initialToken: "sess");

        Assert.AreEqual(
            WebEntryDecision.Unauthorized,
            Decide("/session/list", null, null, "Bearer sess", "tok", sessions),
            "浏览器会话令牌不能当 Bearer 用：它短命，拿来当客户端凭据会让 CLI 被轮换踢掉");
    }

    [TestMethod]
    public void 令牌未配置时连健康检查也不放行()
    {
        var sessions = new WebSessionTokens(initialToken: "sess");
        Assert.AreEqual(WebEntryDecision.Unauthorized, Decide("/session/list", null, "sess", "Bearer tok", null, null));
        Assert.AreEqual(WebEntryDecision.Unauthorized, Decide("/system/health", null, null, null, "", null));

        // 宿主令牌留空但浏览器会话已启用：健康检查照旧免令牌，其它路径仍要 Cookie
        Assert.AreEqual(WebEntryDecision.Allow, Decide("/system/health", null, null, null, "", sessions));
        Assert.AreEqual(WebEntryDecision.Unauthorized, Decide("/session/list", null, null, null, "", sessions));
    }

    [TestMethod]
    public void 会话令牌到期后自动换新且旧值仍有余量()
    {
        var time = new ManualTime();
        var sessions = new WebSessionTokens(
            WebSessionPolicy.Create(lifetimeMinutes: 15, graceMinutes: 5, maxHours: 8),
            initialToken: "first",
            time);

        // 14 分钟内：仍是同一个令牌，不必换发
        time.Advance(TimeSpan.FromMinutes(14));
        var decision = WebHostAccess.Decide("/", null, "first", null, "tok", sessions, out var issued);
        Assert.AreEqual(WebEntryDecision.Allow, decision);
        Assert.AreEqual("first", issued);
        Assert.AreEqual("first", sessions.Current);

        // 刚过 15 分钟：换新，本次请求直接拿到新值写回 Cookie
        time.Advance(TimeSpan.FromMinutes(1.5));
        decision = WebHostAccess.Decide("/", null, "first", null, "tok", sessions, out issued);
        Assert.AreEqual(WebEntryDecision.Allow, decision);
        Assert.AreNotEqual("first", sessions.Current, "过了有效期必须换新令牌");
        Assert.AreEqual(sessions.Current, issued, "换新后的令牌必须回写给浏览器");

        // 旧令牌在宽限期内仍然放行，但回写的已是当前令牌
        decision = WebHostAccess.Decide("/session/list", null, "first", null, "tok", sessions, out issued);
        Assert.AreEqual(WebEntryDecision.Allow, decision);
        Assert.AreEqual(sessions.Current, issued);

        // 宽限期过后旧令牌彻底失效
        time.Advance(TimeSpan.FromMinutes(6));
        Assert.AreEqual(
            WebEntryDecision.Unauthorized,
            WebHostAccess.Decide("/session/list", null, "first", null, "tok", sessions, out _));
    }

    [TestMethod]
    public void 会话超过绝对上限后一律拒绝()
    {
        var time = new ManualTime();
        var sessions = new WebSessionTokens(
            WebSessionPolicy.Create(lifetimeMinutes: 15, graceMinutes: 5, maxHours: 8),
            initialToken: "first",
            time);

        // 一路续期到 7 小时 59 分：活动页面在绝对上限内不掉线
        for (var i = 0; i < 31; i++)
        {
            time.Advance(TimeSpan.FromMinutes(15));
            var decision = WebHostAccess.Decide("/", null, sessions.Current, null, "tok", sessions, out _);
            Assert.AreEqual(WebEntryDecision.Allow, decision, $"第 {i + 1} 次续期不该被拒");
        }

        time.Advance(TimeSpan.FromHours(1));
        Assert.AreEqual(
            WebEntryDecision.Expired,
            WebHostAccess.Decide("/", null, sessions.Current, null, "tok", sessions, out var issued),
            "过了绝对上限必须明确报「已过期」，而不是「未授权」");
        Assert.IsNull(issued);

        // 固定令牌不受会话语义影响：CLI / Codex 不会被浏览器会话的上限踢下线
        Assert.AreEqual(
            WebEntryDecision.Allow,
            WebHostAccess.Decide("/session/list", null, null, "Bearer tok", "tok", sessions, out _));
    }

    [TestMethod]
    public void 取入口令牌时只在绝对上限后才重开()
    {
        var time = new ManualTime();
        var sessions = new WebSessionTokens(
            WebSessionPolicy.Create(lifetimeMinutes: 15, graceMinutes: 5, maxHours: 8),
            initialToken: "first",
            time);

        Assert.AreEqual("first", sessions.TakeEntryToken(), "上限内重复取地址不该换令牌，否则别的设备手上的地址会提前失效");

        time.Advance(TimeSpan.FromHours(9));
        var reopened = sessions.TakeEntryToken();

        Assert.AreNotEqual("first", reopened, "过了绝对上限必须重开会话族");
        Assert.IsTrue(WebHostAccess.Decide("/", null, reopened, null, "tok", sessions, out _) == WebEntryDecision.Allow);
    }

    [TestMethod]
    public void 会话参数越界时直接报错()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WebSessionPolicy.Create(0, 5, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WebSessionPolicy.Create(15, -1, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WebSessionPolicy.Create(15, 5, 0));
    }

    [TestMethod]
    public void 跳转目标去掉令牌但保留其他参数()
    {
        Assert.AreEqual("/", WebHostAccess.StripTokenQuery("/?t=abc"));
        Assert.AreEqual("/", WebHostAccess.StripTokenQuery("?t=abc"));
        Assert.AreEqual("/", WebHostAccess.StripTokenQuery(""));
        Assert.AreEqual("/index.html?a=1", WebHostAccess.StripTokenQuery("/index.html?t=abc&a=1"));
        Assert.AreEqual("/session/list?page=2", WebHostAccess.StripTokenQuery("/session/list?page=2"));
        Assert.AreEqual("/session/list", WebHostAccess.StripTokenQuery("/session/list"));
    }

    [TestMethod]
    public void 入口地址拼上令牌参数()
    {
        Assert.AreEqual(
            "http://192.168.3.215:18080/?t=tok",
            WebHostAccess.BuildEntryUrl("http://192.168.3.215:18080", "tok"));
        Assert.AreEqual(
            "http://192.168.3.215:18080/?t=a%2Bb",
            WebHostAccess.BuildEntryUrl("http://192.168.3.215:18080/", "a+b"),
            "令牌里的特殊字符必须转义，否则 URL 会被截断");
    }

    [TestMethod]
    public void 局域网入口取局域网主机与令牌()
    {
        var url = WebHostAccess.BuildLanEntryUrl(18080, "tok");

        Assert.IsTrue(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"入口地址必须是绝对 URL：{url}");
        Assert.AreEqual("http", uri!.Scheme);
        Assert.AreEqual(18080, uri.Port);
        Assert.AreEqual(WebHostAccess.GetPreferredLanHost(), uri.Host, "入口主机必须与局域网地址一致");
        Assert.AreEqual("tok", uri.Query.TrimStart('?').Split('=')[1]);
    }

    [TestMethod]
    public void 每次生成的令牌都可直接进URL()
    {
        var first = WebHostAccess.CreateToken();
        var second = WebHostAccess.CreateToken();

        Assert.AreEqual(43, first.Length);
        Assert.IsFalse(first.Contains('+') || first.Contains('/') || first.Contains('='), "Base64Url 不应含 + / =");
        Assert.AreNotEqual(first, second, "每次生成的令牌必须不同");
    }

    [TestMethod]
    public void 只有Base64Url字符的令牌可用()
    {
        Assert.IsTrue(WebHostAccess.IsValidToken(WebHostAccess.CreateToken()));
        Assert.IsTrue(WebHostAccess.IsValidToken("abc-123_XYZ"));

        Assert.IsFalse(WebHostAccess.IsValidToken(null));
        Assert.IsFalse(WebHostAccess.IsValidToken(""));
        Assert.IsFalse(WebHostAccess.IsValidToken("abc def"), "空格会截断地址栏与 Set-Cookie");
        Assert.IsFalse(WebHostAccess.IsValidToken("abc;def"), "分号会截断 Set-Cookie");
        Assert.IsFalse(WebHostAccess.IsValidToken("abc\r\nX-Evil: 1"), "换行会注入多余的响应头");
    }

    [TestMethod]
    public void 虚拟网卡不作为局域网入口()
    {
        Assert.IsTrue(WebHostAccess.IsVirtualAdapter("本地连接", "TAP-Windows Adapter V9 for OpenVPN Connect"),
            "VPN 的 TAP 网卡会给出形似局域网的地址，实际别的设备连不上");
        Assert.IsTrue(WebHostAccess.IsVirtualAdapter("vEthernet (WSL (Hyper-V firewall))", "Hyper-V Virtual Ethernet Adapter"));
        Assert.IsTrue(WebHostAccess.IsVirtualAdapter("VMware Network Adapter VMnet8", "VMware Virtual Ethernet Adapter"));

        Assert.IsFalse(WebHostAccess.IsVirtualAdapter("WLAN", "MediaTek Wi-Fi 6 MT7921 Wireless LAN Card"));
        Assert.IsFalse(WebHostAccess.IsVirtualAdapter("以太网", "Realtek PCIe GbE Family Controller"));
    }

    [TestMethod]
    public void 入口地址优先取真实局域网段()
    {
        var host = WebHostAccess.GetPreferredLanHost();

        Assert.IsTrue(System.Net.IPAddress.TryParse(host, out var address), $"入口主机必须是 IPv4：{host}");
        if (!host.StartsWith("127.", StringComparison.Ordinal))
        {
            StringAssert.StartsWith(host, "192.168.", $"优先取 192.168 私网地址，实际：{host}");
        }
    }

    [TestMethod]
    public void 换发凭据的Cookie不带Domain且带HttpOnly()
    {
        var cookie = WebHostAccess.BuildAuthCookie("tok");

        StringAssert.StartsWith(cookie, $"{WebHostAccess.AuthCookieName}=tok;");
        StringAssert.Contains(cookie, "Path=/");
        StringAssert.Contains(cookie, "HttpOnly");
        Assert.IsFalse(cookie.Contains("Domain", StringComparison.OrdinalIgnoreCase), "带 Domain 会让 IP 访问的浏览器拒收");
    }

    /// <summary>可手动推进的时间源：会话令牌的轮换与绝对上限都依赖时钟，不能靠真实等待。</summary>
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>把时钟往前拨。</summary>
        /// <param name="span">时长。</param>
        public void Advance(TimeSpan span) => _now += span;
    }
}