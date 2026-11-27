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
    [TestMethod]
    public void 健康检查路径判定()
    {
        Assert.IsTrue(WebHostAccess.IsHealthPath("/system/health"));
        Assert.IsTrue(WebHostAccess.IsHealthPath("/system/health/"));
        Assert.IsTrue(WebHostAccess.IsHealthPath("/SYSTEM/HEALTH"));
        Assert.IsFalse(WebHostAccess.IsHealthPath("/session/list"));
        Assert.IsFalse(WebHostAccess.IsHealthPath(""));
    }

    [TestMethod]
    public void Bearer只认宿主固定令牌()
    {
        Assert.IsTrue(WebHostAccess.IsBearerAuthorized("Bearer tok", "tok"));
        Assert.IsTrue(WebHostAccess.IsBearerAuthorized("bearer  tok  ", "tok"));
        Assert.IsFalse(WebHostAccess.IsBearerAuthorized("Bearer other", "tok"));
        Assert.IsFalse(WebHostAccess.IsBearerAuthorized("Bearer tok", ""));
        Assert.IsFalse(WebHostAccess.IsBearerAuthorized(null, "tok"));
    }

    [TestMethod]
    public void 命令行用host_token钉住宿主令牌且会话上限默认一天()
    {
        var configuration = new ConfigurationBuilder().Build();

        // --token 已让给「打印入口」命令，钉住固定令牌改走 --host-token
        var pinned = LubanAgentWebHost.Hosting.WebHostSettings.Create(configuration, ["--host-token", "abc_123"]);
        Assert.AreEqual("abc_123", pinned.Token);
        Assert.AreEqual(24d, pinned.SessionMaxHours, "会话绝对上限默认 24 小时");

        var byConfig = LubanAgentWebHost.Hosting.WebHostSettings.Create(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LubanAgentWeb:Token"] = "from_config",
                ["LubanAgentWeb:SessionMaxHours"] = "2"
            }).Build(),
            []);
        Assert.AreEqual("from_config", byConfig.Token);
        Assert.AreEqual(2d, byConfig.SessionMaxHours);

        // --token 不再兼作固定令牌：它只触发打印入口
        Assert.IsNull(LubanAgentWebHost.Hosting.WebHostSettings.Create(configuration, ["--token"]).Token);
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
        Assert.AreEqual("https", uri!.Scheme, "宿主默认启用 HTTPS，局域网入口必须印成 https");
        Assert.AreEqual(18080, uri.Port);
        Assert.AreEqual(WebHostAccess.GetPreferredLanHost(), uri.Host, "入口主机必须与局域网地址一致");
        Assert.AreEqual("tok", uri.Query.TrimStart('?').Split('=')[1]);
    }

    [TestMethod]
    public void 入口协议跟随宿主的HTTPS开关()
    {
        Assert.AreEqual(
            "http",
            new Uri(WebHostAccess.BuildLanEntryUrl(18080, "tok", https: false)).Scheme,
            "--http 启动时局域网入口不能印成 https，否则手机打开就是连接失败");
        Assert.AreEqual("https", new Uri(WebHostAccess.BuildLanEntryUrl(18080, "tok", https: true)).Scheme);

        Assert.AreEqual("http://127.0.0.1:18080/?t=tok", WebHostAccess.BuildLocalEntryUrl(18080, "tok", https: false));
        Assert.AreEqual("https://127.0.0.1:18080/?t=tok", WebHostAccess.BuildLocalEntryUrl(18080, "tok", https: true));
    }

    [TestMethod]
    public void 短码入口地址只含路径短码()
    {
        Assert.AreEqual(
            "https://192.168.3.215:18080/e/abcdefghij",
            WebHostAccess.BuildCodeEntryUrl("https://192.168.3.215:18080/", "abcdefghij"));
        Assert.AreEqual(
            "http://127.0.0.1:18080/e/abcdefghij",
            WebHostAccess.BuildLocalCodeEntryUrl(18080, "abcdefghij", https: false));

        var lan = WebHostAccess.BuildLanCodeEntryUrl(18080, "abcdefghij");
        Assert.AreEqual("https", new Uri(lan).Scheme);
        Assert.AreEqual(WebHostAccess.GetPreferredLanHost(), new Uri(lan).Host);
        Assert.AreEqual(WebHostAccess.EntryCodePathPrefix + "abcdefghij", new Uri(lan).AbsolutePath);
        Assert.IsFalse(lan.Contains('?'), "短码入口不应再带查询串里的令牌");
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
        Assert.IsFalse(cookie.Contains("Secure"), "HTTP 下不能带 Secure，否则浏览器根本不会回传这个 Cookie");

        StringAssert.Contains(
            WebHostAccess.BuildAuthCookie("tok", secure: true),
            "; Secure",
            "HTTPS 下必须带 Secure，避免凭据被明文连接带走");
    }
}