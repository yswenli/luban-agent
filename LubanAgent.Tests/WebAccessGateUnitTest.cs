/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebAccessGateUnitTest
*描述：访问门禁的公开路径判定（登录页与认证接口必须免令牌）
*
*****************************************************************************/

using LubanAgentWebHost.Hosting;

namespace LubanAgent.Tests;

/// <summary>
/// 公开路径判定：放错会把登录页挡在门外，收太宽则把整个 Agent 暴露出去。
/// </summary>
[TestClass]
public class WebAccessGateUnitTest
{
    [TestMethod]
    [DataRow("/", "GET")]
    [DataRow("/index.html", "GET")]
    [DataRow("/runtime-config.js", "GET")]
    [DataRow("/assets/index-abc.js", "GET")]
    [DataRow("/auth/status", "GET")]
    [DataRow("/auth/login", "POST")]
    [DataRow("/auth/register", "POST")]
    [DataRow("/auth/logout", "POST")]
    [DataRow("/system/health", "GET")]
    public void IsPublic_AllowsLoginShellAndAuthEndpoints(string url, string method)
        => Assert.IsTrue(WebAccessGate.IsPublic(url, method));

    [TestMethod]
    [DataRow("/system/bootstrap", "GET")]
    [DataRow("/session/list", "GET")]
    [DataRow("/workspace/create", "POST")]
    [DataRow("/auth/me", "GET")]
    [DataRow("/auth/invites", "GET")]
    public void IsPublic_RequiresCredentialForBusinessEndpoints(string url, string method)
        => Assert.IsFalse(WebAccessGate.IsPublic(url, method));

    [TestMethod]
    public void IsPublic_StaticShellOnlyAllowsReadMethods()
    {
        // 静态壳只放读：任意 POST 打到 SPA 根不应被当成公开页面
        Assert.IsFalse(WebAccessGate.IsPublic("/", "POST"));
        Assert.IsFalse(WebAccessGate.IsPublic("/index.html", "PUT"));
        Assert.IsTrue(WebAccessGate.IsPublic("/", "HEAD"));
    }

    [TestMethod]
    public void IsPublic_IgnoresQueryStringAndTrailingSlash()
    {
        Assert.IsTrue(WebAccessGate.IsPublic("/auth/status?x=1", "GET"));
        Assert.IsTrue(WebAccessGate.IsPublic("/auth/status/", "GET"));
        Assert.IsTrue(WebAccessGate.IsPublic("/assets/app.css?v=2", "GET"));
    }

    [TestMethod]
    public void IsPublic_BlankOrUnknownPath_IsNotPublic()
    {
        Assert.IsFalse(WebAccessGate.IsPublic(null, "GET"));
        Assert.IsFalse(WebAccessGate.IsPublic("   ", "GET"));
        Assert.IsFalse(WebAccessGate.IsPublic("/unknown", "GET"));
    }

    [TestMethod]
    public void NormalizePath_CollapsesTrailingSlashButKeepsRoot()
    {
        // 空白输入视为「没有路径」，由调用方按非公开处理
        Assert.AreEqual(string.Empty, WebAccessGate.NormalizePath(null));
        Assert.AreEqual("/", WebAccessGate.NormalizePath("/"));
        Assert.AreEqual("/", WebAccessGate.NormalizePath("/?t=abc"));
        Assert.AreEqual("/session/list", WebAccessGate.NormalizePath("/session/list/?t=abc"));
    }
}