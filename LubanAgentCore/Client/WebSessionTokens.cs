/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： WebSessionTokens
*描述：浏览器会话令牌的轮换、宽限期与绝对上限（纯状态机，便于单测）
*
*****************************************************************************/

namespace LubanAgentCore.Client;

/// <summary>浏览器会话令牌的时间参数。</summary>
/// <remarks>
/// 浏览器侧的令牌必须短命：入口地址可能被转发、被截图、留在他人浏览历史里，
/// 固定值等于把 Agent 的完整操作权限永久送出去。这里用「定期轮换 + 旧值短暂宽限 + 会话族绝对上限」三层约束，
/// 让活动页面自动续期，同时给泄漏的地址一个明确终点。
/// </remarks>
public sealed record WebSessionPolicy
{
    /// <summary>单个令牌的有效期；到期后的下一次请求换新。</summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>换新后旧令牌的宽限期；轮换瞬间还在路上的并发请求仍可放行。</summary>
    public TimeSpan Grace { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>整个会话族的绝对上限；到点后任何旧令牌都不再放行，必须重新获取入口地址。</summary>
    public TimeSpan MaxLifetime { get; init; } = TimeSpan.FromHours(8);

    /// <summary>默认参数（15 分钟 / 5 分钟 / 8 小时）。</summary>
    public static WebSessionPolicy Default { get; } = new();

    /// <summary>
    /// 按分钟/小时构造并校验。
    /// </summary>
    /// <param name="lifetimeMinutes">单个令牌的有效期（分钟），必须大于 0。</param>
    /// <param name="graceMinutes">旧令牌宽限期（分钟），不得小于 0。</param>
    /// <param name="maxHours">会话族绝对上限（小时），必须大于 0。</param>
    /// <returns>时间参数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">参数越界（配置写错时直接暴露，不做静默兜底）。</exception>
    public static WebSessionPolicy Create(double lifetimeMinutes, double graceMinutes, double maxHours)
    {
        if (lifetimeMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetimeMinutes), "会话令牌有效期必须大于 0 分钟");
        }

        if (graceMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(graceMinutes), "会话令牌宽限期不能为负数");
        }

        if (maxHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHours), "会话令牌绝对上限必须大于 0 小时");
        }

        return new WebSessionPolicy
        {
            Lifetime = TimeSpan.FromMinutes(lifetimeMinutes),
            Grace = TimeSpan.FromMinutes(graceMinutes),
            MaxLifetime = TimeSpan.FromHours(maxHours)
        };
    }
}

/// <summary>会话令牌的判定结果。</summary>
public enum WebSessionState
{
    /// <summary>不是本进程签发过的会话令牌。</summary>
    Invalid,

    /// <summary>有效；需要时由调用方把 <see cref="WebSessionCheck.Token"/> 回写 Cookie。</summary>
    Accepted,

    /// <summary>会话族已到绝对上限；必须重新获取入口地址。</summary>
    Expired
}

/// <summary>一次会话令牌判定的结果。</summary>
/// <param name="State">判定结果。</param>
/// <param name="Token">应当回写给浏览器的当前令牌；仅 <see cref="WebSessionState.Accepted"/> 时非空。</param>
public readonly record struct WebSessionCheck(WebSessionState State, string? Token)
{
    /// <summary>不是本进程签发的令牌。</summary>
    public static WebSessionCheck Invalid => new(WebSessionState.Invalid, null);

    /// <summary>会话族已到绝对上限。</summary>
    public static WebSessionCheck Expired => new(WebSessionState.Expired, null);
}

/// <summary>
/// 浏览器会话令牌的轮换状态机。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="WebHostAccess"/> 里的固定令牌分工：固定令牌只走 <c>Authorization: Bearer</c>，
/// 给 CLI / Codex / 插件用，进程内不变；此类只管浏览器，令牌随 <see cref="WebSessionPolicy.Lifetime"/> 轮换。
/// 浏览器在地址栏 <c>?t=</c> 或 <c>luban_auth</c> Cookie 里带的都是这里的令牌。
/// </para>
/// <para>
/// 轮换发生在「当前令牌已到期后的第一次判定」：此时把旧值降级为宽限令牌、
/// 生成新值并返回给调用方写回 Cookie。因此持续访问的页面不会掉线，也不会因为并发请求同时到期而被判为无效。
/// </para>
/// <para>
/// 绝对上限（<see cref="WebSessionPolicy.MaxLifetime"/>）从会话族创建/重开时算起，到点后连当前令牌也拒绝，
/// 只能由宿主侧持固定令牌的接口（<c>LubanAgentWebHost token</c>）重开会话族。
/// </para>
/// </remarks>
public sealed class WebSessionTokens
{
    private readonly object _gate = new();
    private readonly WebSessionPolicy _policy;
    private readonly TimeProvider _time;

    private string _current;
    private DateTimeOffset _currentIssuedAt;
    private string? _previous;
    private DateTimeOffset _previousExpiresAt;
    private DateTimeOffset _startedAt;

    /// <summary>创建一个会话族。</summary>
    /// <param name="policy">时间参数；为空取默认值。</param>
    /// <param name="initialToken">首个令牌；为空则随机生成。</param>
    /// <param name="time">时间源（单测注入）；为空取系统时间。</param>
    public WebSessionTokens(WebSessionPolicy? policy = null, string? initialToken = null, TimeProvider? time = null)
    {
        _policy = policy ?? WebSessionPolicy.Default;
        _time = time ?? TimeProvider.System;
        _current = string.IsNullOrEmpty(initialToken) ? WebHostAccess.CreateToken() : initialToken;

        var now = _time.GetUtcNow();
        _currentIssuedAt = now;
        _startedAt = now;
    }

    /// <summary>当前时间参数。</summary>
    public WebSessionPolicy Policy => _policy;

    /// <summary>当前令牌；轮换期间会变化，请求处理中不要提前缓存。</summary>
    public string Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>会话族的绝对到期时刻；此后必须重新获取入口地址。</summary>
    public DateTimeOffset ExpiresAt
    {
        get
        {
            lock (_gate)
            {
                return _startedAt + _policy.MaxLifetime;
            }
        }
    }

    /// <summary>当前令牌的到期时刻；到期后的第一次请求会换新。</summary>
    public DateTimeOffset RotateAt
    {
        get
        {
            lock (_gate)
            {
                return _currentIssuedAt + _policy.Lifetime;
            }
        }
    }

    /// <summary>
    /// 判定浏览器带来的令牌，并按需触发轮换。
    /// </summary>
    /// <param name="presented">地址栏或 Cookie 里的令牌。</param>
    /// <returns>判定结果；<see cref="WebSessionState.Accepted"/> 时带回当前令牌。</returns>
    public WebSessionCheck Check(string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return WebSessionCheck.Invalid;
        }

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now - _startedAt >= _policy.MaxLifetime)
            {
                return WebSessionCheck.Expired;
            }

            if (WebHostAccess.FixedEquals(presented, _current))
            {
                if (now - _currentIssuedAt >= _policy.Lifetime)
                {
                    Rotate(now);
                }

                return new WebSessionCheck(WebSessionState.Accepted, _current);
            }

            // 轮换瞬间的并发请求还带着旧值：宽限期内照常放行，并把它滑到当前令牌
            if (_previous is not null
                && now < _previousExpiresAt
                && WebHostAccess.FixedEquals(presented, _previous))
            {
                return new WebSessionCheck(WebSessionState.Accepted, _current);
            }

            return WebSessionCheck.Invalid;
        }
    }

    /// <summary>
    /// 只判定、不轮换（供动作内的第二道复核使用）。
    /// </summary>
    /// <param name="presented">Cookie 里的令牌。</param>
    /// <returns>仍在有效期内返回 true。</returns>
    public bool IsAccepted(string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now - _startedAt >= _policy.MaxLifetime)
            {
                return false;
            }

            if (WebHostAccess.FixedEquals(presented, _current))
            {
                return true;
            }

            return _previous is not null
                   && now < _previousExpiresAt
                   && WebHostAccess.FixedEquals(presented, _previous);
        }
    }

    /// <summary>
    /// 取一个可以直接放进入口地址的令牌。
    /// </summary>
    /// <remarks>
    /// 会话族已过绝对上限时自动重开（调用方必须已用固定令牌完成鉴权），保证打印出的入口一定可用；
    /// 未到期时返回当前令牌，不额外轮换——刚打印完地址就换新只会让别的设备手里的地址提前失效。
    /// </remarks>
    /// <returns>入口地址用的令牌。</returns>
    public string TakeEntryToken()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now - _startedAt < _policy.MaxLifetime)
            {
                return _current;
            }

            return Restart(now);
        }
    }

    /// <summary>
    /// 重开会话族：换新令牌、重置绝对上限。
    /// </summary>
    /// <remarks>宿主重启或会话族到期后由持固定令牌的运维入口调用；已下发的旧令牌全部立即失效。</remarks>
    /// <returns>新令牌。</returns>
    public string Restart()
    {
        lock (_gate)
        {
            return Restart(_time.GetUtcNow());
        }
    }

    private string Restart(DateTimeOffset now)
    {
        _current = WebHostAccess.CreateToken();
        _currentIssuedAt = now;
        _startedAt = now;
        _previous = null;
        _previousExpiresAt = default;
        return _current;
    }

    private void Rotate(DateTimeOffset now)
    {
        _previous = _current;
        _previousExpiresAt = now + _policy.Grace;
        _current = WebHostAccess.CreateToken();
        _currentIssuedAt = now;
    }
}