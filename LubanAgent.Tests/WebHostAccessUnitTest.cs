/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebHostAccessUnitTest
*描述：宿主访问门禁的令牌判定、入口地址与跳转目标
*
*****************************************************************************/

using LubanAgentCore.Client;

namespace LubanAgent.Tests;

[TestClass]
public class WebHostAccessUnitTest
{
    [TestMethod]
    public void 健康检查不需要令牌()
    {
        Assert.AreEqual(WebEntryDecision.Allow, WebHostAccess.Decide("/system/health", null, null, null, "tok"));
        Assert.AreEqual(WebEntryDecision.Allow, WebHostAccess.Decide("/system/health/", null, null, null, "tok"));
        Assert.AreEqual(WebEntryDecision.Allow, WebHostAccess.Decide("/SYSTEM/HEALTH", null, null, null, "tok"));
    }

    [TestMethod]
    public void 没有凭据时一律拒绝()
    {
        Assert.AreEqual(WebEntryDecision.Unauthorized, WebHostAccess.Decide("/", null, null, null, "tok"));
        Assert.AreEqual(WebEntryDecision.Unauthorized, WebHostAccess.Decide("/session/list", null, null, null, "tok"));
        Assert.AreEqual(WebEntryDecision.Unauthorized, WebHostAccess.Decide("/assets/index.js", null, null, null, "tok"));
    }

    [TestMethod]
    public void 地址栏令牌正确时换发凭据并跳转()
    {
        Assert.AreEqual(WebEntryDecision.Redirect, WebHostAccess.Decide("/", "tok", null, null, "tok"));
        Assert.AreEqual(WebEntryDecision.Redirect, WebHostAccess.Decide("/session/list", "tok", null, null, "tok"));
    }

    [TestMethod]
    public void 地址栏令牌错误时不被旧凭据救回()
    {
        Assert.AreEqual(
            WebEntryDecision.Unauthorized,
            WebHostAccess.Decide("/", "bad", "tok", "Bearer tok", "tok"),
            "带错令牌应当直接拒绝，避免把错误链接当成已登录");
    }

    [TestMethod]
    public void Cookie与Bearer都能放行()
    {
        Assert.AreEqual(WebEntryDecision.Allow, WebHostAccess.Decide("/session/list", null, "tok", null, "tok"));
        Assert.AreEqual(WebEntryDecision.Allow, WebHostAccess.Decide("/session/list", null, null, "Bearer tok", "tok"));
        Assert.AreEqual(WebEntryDecision.Allow, WebHostAccess.Decide("/session/list", null, null, "bearer  tok  ", "tok"));
    }

    [TestMethod]
    public void 令牌未配置时连健康检查也不放行()
    {
        Assert.AreEqual(WebEntryDecision.Unauthorized, WebHostAccess.Decide("/session/list", null, "tok", "Bearer tok", null));
        Assert.AreEqual(WebEntryDecision.Unauthorized, WebHostAccess.Decide("/system/health", null, null, null, ""));
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
    public void 每次生成的令牌都可直接进URL()
    {
        var first = WebHostAccess.CreateToken();
        var second = WebHostAccess.CreateToken();

        Assert.AreEqual(43, first.Length);
        Assert.IsFalse(first.Contains('+') || first.Contains('/') || first.Contains('='), "Base64Url 不应含 + / =");
        Assert.AreNotEqual(first, second, "每次生成的令牌必须不同");
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
}