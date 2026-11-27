/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Security
*文件名： JwtTokenService
*描述：Web 浏览器会话令牌（HS256 JWT）的签发与校验
*
*****************************************************************************/

namespace LubanAgentCore.Security;

/// <summary>一次浏览器会话的令牌声明。</summary>
/// <param name="UserId">用户标识。</param>
/// <param name="UserName">登录名。</param>
/// <param name="IsAdmin">是否管理员。</param>
/// <param name="Version">用户令牌版本；改密或禁用时递增。</param>
/// <param name="IssuedAt">签发时间。</param>
/// <param name="ExpiresAt">到期时间。</param>
/// <param name="SessionStartedAt">会话起始时间（绝对上限从此刻算起）。</param>
public sealed record WebTokenClaims(
    string UserId,
    string UserName,
    bool IsAdmin,
    int Version,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset SessionStartedAt);

/// <summary>
/// Web 浏览器会话令牌：HS256 签名的 JWT，无服务端会话表。
/// </summary>
/// <remarks>
/// <para>
/// 只承载身份与有效期：签名密钥独立于用户口令，落盘在用户主目录，不进源码与日志。
/// 「15 分钟到期 + 活动页面续期」由调用方按 <see cref="WebTokenClaims.ExpiresAt"/> 重新签发实现；
/// 旧令牌在自身到期前仍然有效，等价于轮换后的宽限期。
/// </para>
/// <para>
/// 不校验用户是否仍存在/禁用——那是数据库职责，由动作守卫（每请求一次）复核，
/// 静态资源不再多查一次库。
/// </para>
/// </remarks>
public sealed class JwtTokenService
{
    /// <summary>JWT 固定头（只支持 HS256）。</summary>
    private static readonly string EncodedHeader = Base64UrlEncode(
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        }));

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    /// <summary>创建签发器。</summary>
    /// <param name="key">HS256 密钥；至少 32 字节。</param>
    /// <param name="time">时间源（单测注入）；为空取系统时间。</param>
    public JwtTokenService(byte[] key, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new ArgumentException("JWT 密钥至少需要 32 字节", nameof(key));
        }

        _key = key;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>从文件读取密钥；文件不存在时生成并写入。</summary>
    /// <param name="path">密钥文件路径。</param>
    /// <param name="time">时间源（单测注入）。</param>
    /// <returns>签发器。</returns>
    public static JwtTokenService LoadOrCreate(string path, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] key;
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim();
            key = Convert.FromBase64String(text);
            if (key.Length < 32)
            {
                throw new InvalidOperationException($"JWT 密钥文件内容不合法（至少 32 字节）：{path}");
            }
        }
        else
        {
            key = RandomNumberGenerator.GetBytes(48);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, Convert.ToBase64String(key), new UTF8Encoding(false));
        }

        return new JwtTokenService(key, time);
    }

    /// <summary>签发令牌。</summary>
    /// <param name="claims">声明。</param>
    /// <returns>可直接进 URL 与 Cookie 的 JWT。</returns>
    public string Create(WebTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["sub"] = claims.UserId,
            ["name"] = claims.UserName,
            ["adm"] = claims.IsAdmin,
            ["ver"] = claims.Version,
            ["iat"] = claims.IssuedAt.ToUnixTimeSeconds(),
            ["exp"] = claims.ExpiresAt.ToUnixTimeSeconds(),
            ["sst"] = claims.SessionStartedAt.ToUnixTimeSeconds()
        });

        var body = $"{EncodedHeader}.{Base64UrlEncode(payload)}";
        return $"{body}.{Base64UrlEncode(Sign(body))}";
    }

    /// <summary>
    /// 校验证书签名与有效期。
    /// </summary>
    /// <param name="token">JWT。</param>
    /// <param name="claims">校验通过时的声明。</param>
    /// <param name="ignoreExpiry">是否忽略到期时间（仅用于把「过期」与「伪造」区分开）。</param>
    /// <returns>签名正确且未过期返回 true。</returns>
    public bool TryValidate(string? token, out WebTokenClaims? claims, bool ignoreExpiry = false)
    {
        claims = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Trim().Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], EncodedHeader, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Sign($"{parts[0]}.{parts[1]}");
        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            var userId = ReadString(root, "sub");
            var userName = ReadString(root, "name");
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(userName))
            {
                return false;
            }

            if (!root.TryGetProperty("exp", out var expElement)
                || !expElement.TryGetInt64(out var exp)
                || !root.TryGetProperty("iat", out var iatElement)
                || !iatElement.TryGetInt64(out var iat)
                || !root.TryGetProperty("sst", out var sstElement)
                || !sstElement.TryGetInt64(out var sst)
                || !root.TryGetProperty("ver", out var verElement)
                || !verElement.TryGetInt32(out var version))
            {
                return false;
            }

            var now = _time.GetUtcNow();
            if (!ignoreExpiry && now.ToUnixTimeSeconds() >= exp)
            {
                return false;
            }

            // 会话起始时间不能来自未来（给 60 秒时钟偏差余量）
            if (sst > now.AddMinutes(1).ToUnixTimeSeconds())
            {
                return false;
            }

            claims = new WebTokenClaims(
                userId,
                userName,
                root.TryGetProperty("adm", out var adm) && adm.ValueKind == JsonValueKind.True,
                version,
                DateTimeOffset.FromUnixTimeSeconds(iat),
                DateTimeOffset.FromUnixTimeSeconds(exp),
                DateTimeOffset.FromUnixTimeSeconds(sst));
            return true;
        }
    }

    /// <summary>
    /// 续签：保留会话起点与身份，只延后到期时间。
    /// </summary>
    /// <param name="claims">原声明。</param>
    /// <param name="lifetime">本次令牌有效期。</param>
    /// <returns>新 JWT。</returns>
    public string Renew(WebTokenClaims claims, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var now = _time.GetUtcNow();
        return Create(claims with { IssuedAt = now, ExpiresAt = now + lifetime });
    }

    /// <summary>当前时间。</summary>
    public DateTimeOffset Now => _time.GetUtcNow();

    private byte[] Sign(string body) => HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body));

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var value = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
    }
}