/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： EntryLinkService
*描述：入口短码表：把二维码里的短链映射回带会话令牌的地址
*
*****************************************************************************/

using System.Security.Cryptography;

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 入口短码表（进程内，不落盘）。
/// </summary>
/// <remarks>
/// <para>
/// 二维码只编码 <c>/e/{code}</c>，矩阵内容从约 280 字符降到 40 字符；短码在宿主进程内映射回
/// <c>/?t=&lt;令牌&gt;</c>，浏览器随后仍由门禁换成 HttpOnly Cookie。
/// </para>
/// <para>
/// 短码与会话令牌同时到期：令牌本来就在 15 分钟内过期，短码活得更久只会留下一个打开即 401 的链接。
/// 表只保留最近 <see cref="MaxLinks"/> 条，超出时先清过期项、再按到期时刻淘汰最早的，
/// 避免长期运行的宿主无限增长。
/// </para>
/// </remarks>
public sealed class EntryLinkService
{
    /// <summary>短码长度：7 字节随机数的 Base64Url 文本（56 bit 空间）。</summary>
    public const int CodeLength = 10;

    /// <summary>同时保留的短码上限。</summary>
    public const int MaxLinks = 128;

    private readonly Dictionary<string, (string Token, DateTimeOffset ExpiresAt)> _links = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;

    /// <summary>创建短码表。</summary>
    /// <param name="clock">时间源；为空时取系统时钟（测试可注入手动时钟）。</param>
    public EntryLinkService(TimeProvider? clock = null)
        => _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// 登记一条令牌并返回短码。
    /// </summary>
    /// <param name="token">浏览器会话令牌。</param>
    /// <param name="expiresAt">令牌到期时刻；短码同时失效。</param>
    /// <returns>可直接放进 URL 路径的短码。</returns>
    public string Register(string token, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            RemoveExpired(now);

            while (_links.Count >= MaxLinks)
            {
                var oldest = _links.MinBy(static pair => pair.Value.ExpiresAt).Key;
                _links.Remove(oldest);
            }

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var code = NewCode();
                if (_links.TryAdd(code, (token, expiresAt)))
                {
                    return code;
                }
            }

            throw new InvalidOperationException("入口短码生成失败：连续碰撞");
        }
    }

    /// <summary>
    /// 用短码换回令牌；成功后短码立即作废（一次性）。
    /// </summary>
    /// <remarks>
    /// 换回的地址里带令牌，若短码可重复解析，令牌就会长期留在浏览器历史/扫码记录里，
    /// 拿到历史的人可以反复打开。因此解析成功即移除，刷新页面由换到的 Cookie 继续。
    /// </remarks>
    /// <param name="code">路径里的短码。</param>
    /// <param name="token">换回的令牌；失败时为 null。</param>
    /// <returns>短码存在且未过期时返回 true。</returns>
    public bool TryResolve(string? code, out string? token)
    {
        token = null;
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_links.TryGetValue(code, out var link))
            {
                return false;
            }

            if (link.ExpiresAt <= _clock.GetUtcNow())
            {
                _links.Remove(code);
                return false;
            }

            _links.Remove(code);
            token = link.Token;
            return true;
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        var expired = _links
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(static pair => pair.Key)
            .ToArray();
        foreach (var code in expired)
        {
            _links.Remove(code);
        }
    }

    /// <summary>生成一条短码：7 字节随机数恰好编码成 10 个 Base64Url 字符。</summary>
    private static string NewCode()
    {
        Span<byte> bytes = stackalloc byte[7];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}