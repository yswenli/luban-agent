/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Security
*文件名： PasswordHasher
*描述：PBKDF2 密码摘要（不保存明文，验证走固定时间比较）
*
*****************************************************************************/

namespace LubanAgentCore.Security;

/// <summary>
/// 密码摘要与校验。
/// </summary>
/// <remarks>
/// 存储格式：<c>pbkdf2-sha256$迭代次数$盐(Base64)$摘要(Base64)</c>。
/// 迭代次数写进结果里，后续调参不影响旧口令校验。
/// </remarks>
public static class PasswordHasher
{
    /// <summary>当前使用的迭代次数（OWASP 对 PBKDF2-SHA256 的建议下限）。</summary>
    public const int DefaultIterations = 210_000;

    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    /// <summary>生成密码摘要。</summary>
    /// <param name="password">明文密码。</param>
    /// <returns>可直接落库的摘要串。</returns>
    /// <exception cref="ArgumentException">密码为空。</exception>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            KeyBytes);

        return $"{Prefix}${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    /// <summary>校验密码。</summary>
    /// <param name="password">明文密码。</param>
    /// <param name="stored">库里的摘要串。</param>
    /// <returns>匹配返回 true；摘要为空或格式不合法返回 false（不抛异常，避免登录接口 500）。</returns>
    public static bool Verify(string? password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations < 1000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}