/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AccountSecurityUnitTest
*描述：密码摘要与浏览器会话 JWT 的签发 / 校验
*
*****************************************************************************/

using LubanAgentCore.Security;

namespace LubanAgent.Tests;

/// <summary>
/// 用户口令与浏览器令牌。
/// </summary>
[TestClass]
public class AccountSecurityUnitTest
{
    /// <summary>可手动推进的时间源，用来验证到期语义而不真的等待。</summary>
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static WebTokenClaims Claims(DateTimeOffset now, TimeSpan lifetime)
        => new("alice", "Alice", IsAdmin: false, Version: 3, now, now + lifetime, now);

    [TestMethod]
    public void PasswordHash_RoundTrips_AndRejectsWrongPassword()
    {
        var stored = PasswordHasher.Hash("correct horse battery");

        StringAssert.StartsWith(stored, "pbkdf2-sha256$");
        Assert.IsTrue(PasswordHasher.Verify("correct horse battery", stored));
        Assert.IsFalse(PasswordHasher.Verify("wrong password", stored));
    }

    [TestMethod]
    public void PasswordHash_UsesFreshSalt_SoSamePasswordDiffers()
    {
        var first = PasswordHasher.Hash("same-password");
        var second = PasswordHasher.Hash("same-password");

        Assert.AreNotEqual(first, second);
        Assert.IsTrue(PasswordHasher.Verify("same-password", first));
        Assert.IsTrue(PasswordHasher.Verify("same-password", second));
    }

    [TestMethod]
    public void PasswordVerify_InvalidStoredValue_ReturnsFalse()
    {
        // 登录接口不能因为库里格式坏掉就 500
        Assert.IsFalse(PasswordHasher.Verify("any", null));
        Assert.IsFalse(PasswordHasher.Verify("any", ""));
        Assert.IsFalse(PasswordHasher.Verify("any", "plain-text"));
        Assert.IsFalse(PasswordHasher.Verify("any", "pbkdf2-sha256$abc$@@@$###"));
    }

    [TestMethod]
    public void PasswordHash_BlankPassword_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PasswordHasher.Hash("   "));
    }

    [TestMethod]
    public void DisplayName_Blank_FallsBackToUserName()
    {
        Assert.AreEqual("alice", UserAuthService.SanitizeDisplayName(null, "alice"));
        Assert.AreEqual("alice", UserAuthService.SanitizeDisplayName("   ", "alice"));
    }

    [TestMethod]
    public void DisplayName_IsTrimmed()
    {
        Assert.AreEqual("爱丽丝", UserAuthService.SanitizeDisplayName("  爱丽丝  ", "alice"));
    }

    [TestMethod]
    public void DisplayName_TooLong_Throws()
    {
        var tooLong = new string('a', UserAuthService.MaxDisplayNameLength + 1);
        var ex = Assert.ThrowsExactly<AuthException>(() => UserAuthService.SanitizeDisplayName(tooLong, "alice"));
        Assert.AreEqual(400, ex.Code);
    }

    [TestMethod]
    public void Jwt_RoundTripsClaims()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var tokens = new JwtTokenService(new byte[48], new ManualTimeProvider(now));

        var token = tokens.Create(Claims(now, TimeSpan.FromMinutes(15)));

        Assert.IsTrue(tokens.TryValidate(token, out var claims));
        Assert.IsNotNull(claims);
        Assert.AreEqual("alice", claims!.UserId);
        Assert.AreEqual("Alice", claims.UserName);
        Assert.IsFalse(claims.IsAdmin);
        Assert.AreEqual(3, claims.Version);
    }

    [TestMethod]
    public void Jwt_Expired_RejectedUnlessExpiryIgnored()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var time = new ManualTimeProvider(now);
        var tokens = new JwtTokenService(new byte[48], time);
        var token = tokens.Create(Claims(now, TimeSpan.FromMinutes(1)));

        time.Now = now.AddMinutes(2);

        Assert.IsFalse(tokens.TryValidate(token, out _));
        Assert.IsTrue(tokens.TryValidate(token, out var claims, ignoreExpiry: true));
        Assert.AreEqual("alice", claims!.UserId);
    }

    [TestMethod]
    public void Jwt_TamperedPayload_Rejected()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var tokens = new JwtTokenService(new byte[48], new ManualTimeProvider(now));
        var parts = tokens.Create(Claims(now, TimeSpan.FromMinutes(15))).Split('.');

        // 换掉载荷但不重新签名：签名对不上
        var forged = $"{parts[0]}.{parts[1][..^2]}AA.{parts[2]}";
        Assert.IsFalse(tokens.TryValidate(forged, out _));
    }

    [TestMethod]
    public void Jwt_SignedWithDifferentKey_Rejected()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var issuer = new JwtTokenService(Enumerable.Repeat((byte)1, 48).ToArray(), new ManualTimeProvider(now));
        var verifier = new JwtTokenService(Enumerable.Repeat((byte)2, 48).ToArray(), new ManualTimeProvider(now));

        Assert.IsFalse(verifier.TryValidate(issuer.Create(Claims(now, TimeSpan.FromMinutes(15))), out _));
    }

    [TestMethod]
    public void Jwt_Renew_KeepsSessionStartAndExtendsExpiry()
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var time = new ManualTimeProvider(start);
        var tokens = new JwtTokenService(new byte[48], time);
        var original = Claims(start, TimeSpan.FromMinutes(15));

        time.Now = start.AddMinutes(10);
        var renewed = tokens.Renew(original, TimeSpan.FromMinutes(15));

        Assert.IsTrue(tokens.TryValidate(renewed, out var claims));
        Assert.AreEqual(original.SessionStartedAt, claims!.SessionStartedAt);
        Assert.AreEqual(time.Now, claims.IssuedAt);
        Assert.AreEqual(time.Now.AddMinutes(15), claims.ExpiresAt);
    }

    [TestMethod]
    public void Jwt_KeyShorterThan32Bytes_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new JwtTokenService(new byte[31]));
    }
}