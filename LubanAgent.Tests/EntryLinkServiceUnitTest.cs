/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： EntryLinkServiceUnitTest
*描述：入口短码表的生成、解析、到期与容量淘汰
*
*****************************************************************************/

using LubanAgentCore.Client;
using LubanAgentWebHost.Hosting;

namespace LubanAgent.Tests;

/// <summary>
/// 入口短码表。
/// </summary>
[TestClass]
public class EntryLinkServiceUnitTest
{
    /// <summary>可手动推进的时间源，用来验证到期语义而不真的等待。</summary>
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestMethod]
    public void 短码可换回令牌且长度固定()
    {
        var service = new EntryLinkService();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var code = service.Register("token-1", expiresAt);

        Assert.AreEqual(EntryLinkService.CodeLength, code.Length);
        Assert.IsTrue(WebHostAccess.IsValidToken(code), "短码要能直接放进 URL 路径，只允许 Base64Url 字符");
        Assert.IsTrue(service.TryResolve(code, out var token));
        Assert.AreEqual("token-1", token);
    }

    [TestMethod]
    public void 每次登记得到不同的短码()
    {
        var service = new EntryLinkService();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var first = service.Register("token-1", expiresAt);
        var second = service.Register("token-1", expiresAt);

        Assert.AreNotEqual(first, second);
        Assert.IsTrue(service.TryResolve(first, out _));
        Assert.IsTrue(service.TryResolve(second, out _));
    }

    [TestMethod]
    public void 过期短码不再放行()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new EntryLinkService(clock);
        var code = service.Register("token-1", clock.Now.AddMinutes(1));

        Assert.IsTrue(service.TryResolve(code, out _), "到期前应能解析");

        clock.Now = clock.Now.AddMinutes(2);
        Assert.IsFalse(service.TryResolve(code, out var token));
        Assert.IsNull(token);
    }

    [TestMethod]
    public void 未知或空短码不放行()
    {
        var service = new EntryLinkService();
        service.Register("token-1", DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.IsFalse(service.TryResolve(null, out _));
        Assert.IsFalse(service.TryResolve("", out _));
        Assert.IsFalse(service.TryResolve("abcdefghij", out _));
    }

    [TestMethod]
    public void 超过容量时先淘汰最早到期的短码()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new EntryLinkService(clock);

        var first = service.Register("token-0", clock.Now.AddMinutes(1));
        for (var i = 1; i < EntryLinkService.MaxLinks; i++)
        {
            service.Register($"token-{i}", clock.Now.AddMinutes(1 + i));
        }

        Assert.IsTrue(service.TryResolve(first, out _), "未超上限时不淘汰");

        var extra = service.Register("token-extra", clock.Now.AddMinutes(200));

        Assert.IsFalse(service.TryResolve(first, out _), "超上限后应淘汰最早到期的一条");
        Assert.IsTrue(service.TryResolve(extra, out _));
    }
}