/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebRequestSchemeUnitTest
*描述：请求协议判定：转发头优先、缺失时按宿主是否 TLS
*
*****************************************************************************/

using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

/// <summary>
/// 请求实际协议的判定：链接基地址与 Cookie Secure 共用这一处结论。
/// </summary>
[TestClass]
public class WebRequestSchemeUnitTest
{
    [TestMethod]
    public void Pick_WithoutForwardedProto_FollowsHostTls()
    {
        Assert.AreEqual("https", WebRequestScheme.Pick(null, hostUsesHttps: true));
        Assert.AreEqual("http", WebRequestScheme.Pick(null, hostUsesHttps: false));
    }

    [TestMethod]
    public void Pick_BlankForwardedProto_FollowsHostTls()
    {
        Assert.AreEqual("https", WebRequestScheme.Pick("   ", hostUsesHttps: true));
        Assert.AreEqual("http", WebRequestScheme.Pick("", hostUsesHttps: false));
    }

    [TestMethod]
    public void Pick_ForwardedProto_WinsOverHostTls()
    {
        // 反代终结 TLS、宿主自己讲明文：用户侧仍是 https
        Assert.AreEqual("https", WebRequestScheme.Pick("https", hostUsesHttps: false));
        // 反代降级到明文：宿主即使开着 TLS，用户侧也是 http
        Assert.AreEqual("http", WebRequestScheme.Pick("http", hostUsesHttps: true));
    }

    [TestMethod]
    public void Pick_ForwardedProto_IsCaseInsensitiveAndTrimmed()
    {
        Assert.AreEqual("https", WebRequestScheme.Pick(" HTTPS ", hostUsesHttps: false));
    }

    [TestMethod]
    public void Pick_MultipleForwardedValues_UseFirst()
    {
        // 代理链会追加多段，第一段才是用户与最近一跳之间的协议
        Assert.AreEqual("https", WebRequestScheme.Pick("https, http", hostUsesHttps: false));
    }

    [TestMethod]
    public void Pick_UnknownForwardedProto_FollowsHostTls()
    {
        // 拼错或非协议名不能把链接与 Cookie 引到错误协议
        Assert.AreEqual("https", WebRequestScheme.Pick("ftp", hostUsesHttps: true));
        Assert.AreEqual("http", WebRequestScheme.Pick("ftp", hostUsesHttps: false));
    }

    [TestMethod]
    public void Resolve_WithoutRequest_FallsBackToHostBaseAddress()
    {
        var https = WebRequestAddress.Resolve(null, 18080, hostUsesHttps: true);
        var http = WebRequestAddress.Resolve(null, 18080, hostUsesHttps: false);

        StringAssert.StartsWith(https, "https://");
        StringAssert.EndsWith(https, ":18080");
        StringAssert.StartsWith(http, "http://");
        StringAssert.EndsWith(http, ":18080");
    }
}