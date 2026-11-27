/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebAuthService
*描述：Web 端登录 / 注册 / 登出与邀请码接口
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 认证接口业务。
/// </summary>
/// <remarks>
/// 登录成功后把 JWT 写进 HttpOnly Cookie（<c>luban_auth</c>），前端拿不到也不落 localStorage；
/// 之后的同源请求由门禁从 Cookie 解出用户身份。
/// </remarks>
public static class WebAuthService
{
    /// <summary>
    /// 登录页公开状态：是否已初始化管理员、是否允许注册、当前是否已登录。
    /// </summary>
    /// <returns>状态。</returns>
    public static Task<ActionResult> StatusAsync() => WebApi.RunAsync(async () =>
    {
        var runtime = WebApi.Runtime;
        var admin = await runtime.Auth.GetUserAsync(UserAuthService.BuiltInAdminUserId).ConfigureAwait(false);

        AuthUserDto? user = null;
        if (WebApi.CurrentClaims is { } claims
            && await runtime.Auth.ValidateAsync(claims).ConfigureAwait(false) is { } current)
        {
            user = UserAuthService.ToDto(current);
        }
        else if (WebApi.IsBearerRequest
                 && await runtime.Auth.GetUserAsync(runtime.UserId).ConfigureAwait(false) is { } host)
        {
            user = UserAuthService.ToDto(host);
        }

        return WebApi.OkJson(new AuthStatusDto(admin is not null, true, user));
    });

    /// <summary>
    /// 登录并下发登录 Cookie。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="LoginRequest"/> JSON。</param>
    /// <returns>用户与到期时刻。</returns>
    public static Task<ActionResult> LoginAsync(string payload) => WebApi.RunAsync(async () =>
    {
        if (!WebApi.TryReadPayload<LoginRequest>(payload, out var request))
        {
            return WebApi.FailJson(400, "请求体不合法");
        }

        try
        {
            var (user, token, expiresAt) = await WebApi.Runtime.Auth
                .LoginAsync(request!.UserName, request.Password)
                .ConfigureAwait(false);

            SetAuthCookie(token);
            return WebApi.OkJson(new AuthSessionDto(UserAuthService.ToDto(user), expiresAt));
        }
        catch (AuthException ex)
        {
            return WebApi.FailJson(ex.Code, ex.Message);
        }
    });

    /// <summary>
    /// 凭邀请码注册并下发登录 Cookie。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="RegisterRequest"/> JSON。</param>
    /// <returns>用户与到期时刻。</returns>
    public static Task<ActionResult> RegisterAsync(string payload) => WebApi.RunAsync(async () =>
    {
        if (!WebApi.TryReadPayload<RegisterRequest>(payload, out var request))
        {
            return WebApi.FailJson(400, "请求体不合法");
        }

        try
        {
            var (user, token, expiresAt) = await WebApi.Runtime.Auth
                .RegisterAsync(request!)
                .ConfigureAwait(false);

            SetAuthCookie(token);
            return WebApi.OkJson(new AuthSessionDto(UserAuthService.ToDto(user), expiresAt));
        }
        catch (AuthException ex)
        {
            return WebApi.FailJson(ex.Code, ex.Message);
        }
    });

    /// <summary>
    /// 登出：清掉登录 Cookie（令牌是无状态的，只能靠客户端不再携带）。
    /// </summary>
    /// <returns>成功壳。</returns>
    public static Task<ActionResult> LogoutAsync() => WebApi.RunAsync(() =>
    {
        WebApi.Response.Headers["Set-Cookie"] = WebHostAccess.BuildClearAuthCookie(IsSecureRequest());
        return Task.FromResult(WebApi.OkJson());
    });

    /// <summary>
    /// 当前登录用户。
    /// </summary>
    /// <returns>用户信息。</returns>
    public static Task<ActionResult> MeAsync() => WebApi.RunGuardedAsync(
        async () =>
        {
            if (WebApi.CurrentClaims is { } claims
                && await WebApi.Runtime.Auth.ValidateAsync(claims).ConfigureAwait(false) is { } user)
            {
                return WebApi.OkJson(UserAuthService.ToDto(user));
            }

            var host = await WebApi.Runtime.Auth.GetUserAsync(WebApi.UserId).ConfigureAwait(false);
            return host is null
                ? WebApi.FailJson(404, "当前身份没有对应用户")
                : WebApi.OkJson(UserAuthService.ToDto(host));
        },
        ensureUser: false);

    /// <summary>
    /// 邀请码列表（仅管理员）。
    /// </summary>
    /// <returns>邀请码列表。</returns>
    public static Task<ActionResult> InvitesAsync() => WebApi.RunGuardedAsync(
        async () =>
        {
            if (await EnsureAdminAsync().ConfigureAwait(false) is { } denied)
            {
                return denied;
            }

            var list = await WebApi.Runtime.Auth.ListInvitesAsync().ConfigureAwait(false);
            return WebApi.OkJson(list);
        },
        ensureUser: false);

    /// <summary>
    /// 创建邀请码（仅管理员）。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="InviteCreateRequest"/> JSON。</param>
    /// <returns>新建的邀请码。</returns>
    public static Task<ActionResult> CreateInviteAsync(string payload) => WebApi.RunGuardedAsync(
        async () =>
        {
            if (await EnsureAdminAsync().ConfigureAwait(false) is { } denied)
            {
                return denied;
            }

            var days = WebApi.TryReadPayload<InviteCreateRequest>(payload, out var request)
                ? request?.ValidDays
                : null;

            try
            {
                var invite = await WebApi.Runtime.Auth.CreateInviteAsync(WebApi.UserId, days).ConfigureAwait(false);
                return WebApi.OkJson(invite);
            }
            catch (AuthException ex)
            {
                return WebApi.FailJson(ex.Code, ex.Message);
            }
        },
        ensureUser: false);

    /// <summary>
    /// 作废邀请码（仅管理员，且只限未使用的）。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="InviteCodeRequest"/> JSON。</param>
    /// <returns>是否作废成功。</returns>
    public static Task<ActionResult> RevokeInviteAsync(string payload) => WebApi.RunGuardedAsync(
        async () =>
        {
            if (await EnsureAdminAsync().ConfigureAwait(false) is { } denied)
            {
                return denied;
            }

            if (!WebApi.TryReadPayload<InviteCodeRequest>(payload, out var request)
                || string.IsNullOrWhiteSpace(request?.Code))
            {
                return WebApi.FailJson(400, "邀请码不能为空");
            }

            var removed = await WebApi.Runtime.Auth
                .RevokeInviteAsync(request!.Code.Trim().ToUpperInvariant())
                .ConfigureAwait(false);
            return removed
                ? WebApi.OkJson()
                : WebApi.FailJson(404, "邀请码不存在或已被使用");
        },
        ensureUser: false);

    /// <summary>
    /// 删除邀请码（仅管理员，任意状态）。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="InviteCodeRequest"/> JSON。</param>
    /// <returns>是否删除成功。</returns>
    public static Task<ActionResult> DeleteInviteAsync(string payload) => WebApi.RunGuardedAsync(
        async () =>
        {
            if (await EnsureAdminAsync().ConfigureAwait(false) is { } denied)
            {
                return denied;
            }

            if (!WebApi.TryReadPayload<InviteCodeRequest>(payload, out var request)
                || string.IsNullOrWhiteSpace(request?.Code))
            {
                return WebApi.FailJson(400, "邀请码不能为空");
            }

            var removed = await WebApi.Runtime.Auth
                .DeleteInviteAsync(request!.Code.Trim().ToUpperInvariant())
                .ConfigureAwait(false);
            return removed
                ? WebApi.OkJson()
                : WebApi.FailJson(404, "邀请码不存在");
        },
        ensureUser: false);

    /// <summary>
    /// 修改本人密码；成功后给当前浏览器换发新 Cookie，其他设备上的旧令牌立即失效。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="ChangePasswordRequest"/> JSON。</param>
    /// <returns>成功壳。</returns>
    public static Task<ActionResult> ChangePasswordAsync(string payload) => WebApi.RunGuardedAsync(
        async () =>
        {
            if (!WebApi.TryReadPayload<ChangePasswordRequest>(payload, out var request) || request is null)
            {
                return WebApi.FailJson(400, "请求体不合法");
            }

            try
            {
                var user = await WebApi.Runtime.Auth
                    .ChangePasswordAsync(WebApi.UserId, request.OldPassword, request.NewPassword)
                    .ConfigureAwait(false);

                // 令牌版本已递增，旧 Cookie 从下一次动作起失效；Bearer 客户端不持有 Cookie，无需换发
                if (!WebApi.IsBearerRequest)
                {
                    var entry = await WebApi.Runtime.Auth.CreateEntryTokenAsync(user.UserId).ConfigureAwait(false);
                    SetAuthCookie(entry.Token);
                }

                return WebApi.OkJson();
            }
            catch (AuthException ex)
            {
                return WebApi.FailJson(ex.Code, ex.Message);
            }
        },
        ensureUser: false);

    /// <summary>
    /// 修改本人显示名；空值回退登录名，不影响登录名与用户标识。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="UpdateProfileRequest"/> JSON。</param>
    /// <returns>更新后的用户信息。</returns>
    public static Task<ActionResult> UpdateProfileAsync(string payload) => WebApi.RunGuardedAsync(
        async () =>
        {
            if (!WebApi.TryReadPayload<UpdateProfileRequest>(payload, out var request))
            {
                return WebApi.FailJson(400, "请求体不合法");
            }

            try
            {
                var user = await WebApi.Runtime.Auth
                    .UpdateDisplayNameAsync(WebApi.UserId, request!.DisplayName)
                    .ConfigureAwait(false);
                return WebApi.OkJson(user);
            }
            catch (AuthException ex)
            {
                return WebApi.FailJson(ex.Code, ex.Message);
            }
        },
        ensureUser: false);

    /// <summary>
    /// 用户列表（仅管理员）。
    /// </summary>
    /// <returns>全部未删除用户。</returns>
    public static Task<ActionResult> UsersAsync() => WebApi.RunGuardedAsync(
        async () =>
        {
            if (await EnsureAdminAsync().ConfigureAwait(false) is { } denied)
            {
                return denied;
            }

            var list = await WebApi.Runtime.Auth.ListUsersAsync().ConfigureAwait(false);
            return WebApi.OkJson(list);
        },
        ensureUser: false);

    /// <summary>
    /// 重置指定用户密码（仅管理员）；新密码只在本响应里返回一次。
    /// </summary>
    /// <param name="payload">包装后的 <see cref="ResetPasswordRequest"/> JSON。</param>
    /// <returns>被重置的用户与新密码。</returns>
    public static Task<ActionResult> ResetPasswordAsync(string payload) => WebApi.RunGuardedAsync(
        async () =>
        {
            if (await EnsureAdminAsync().ConfigureAwait(false) is { } denied)
            {
                return denied;
            }

            if (!WebApi.TryReadPayload<ResetPasswordRequest>(payload, out var request)
                || string.IsNullOrWhiteSpace(request?.UserId))
            {
                return WebApi.FailJson(400, "用户标识不能为空");
            }

            var userId = request!.UserId.Trim();

            // 自我重置会在响应的同时使当前会话失效；新密码只返回一次，响应丢失即锁死。
            // 本人改密走「修改密码」，那里校验旧密码并在同一响应里换发 Cookie。
            if (string.Equals(userId, WebApi.UserId, StringComparison.Ordinal))
            {
                return WebApi.FailJson(400, "不能重置自己的密码，请使用「修改密码」");
            }

            try
            {
                var password = await WebApi.Runtime.Auth.ResetPasswordAsync(userId).ConfigureAwait(false);
                return WebApi.OkJson(new ResetPasswordResult(userId, password));
            }
            catch (AuthException ex)
            {
                return WebApi.FailJson(ex.Code, ex.Message);
            }
        },
        ensureUser: false);

    /// <summary>把登录令牌写进 HttpOnly Cookie。</summary>
    /// <param name="token">JWT。</param>
    private static void SetAuthCookie(string token)
        => WebApi.Response.Headers["Set-Cookie"] = WebHostAccess.BuildAuthCookie(token, IsSecureRequest());

    /// <summary>Cookie 是否要带 <c>Secure</c>：跟请求实际协议走（反代终结 TLS 时看 X-Forwarded-Proto）。</summary>
    /// <returns>HTTPS 返回 true。</returns>
    private static bool IsSecureRequest()
        => string.Equals(
            WebRequestScheme.Resolve(WebApi.Request, WebApi.Runtime.Settings.UseHttps),
            WebRequestScheme.Https,
            StringComparison.Ordinal);

    /// <summary>邀请码与用户管理仅管理员可用；宿主固定令牌视为管理员。</summary>
    /// <returns>拒绝响应；有权限返回 null。</returns>
    private static async Task<ActionResult?> EnsureAdminAsync()
    {
        if (WebApi.IsBearerRequest)
        {
            return null;
        }

        if (WebApi.CurrentClaims is not { } claims
            || await WebApi.Runtime.Auth.ValidateAsync(claims).ConfigureAwait(false) is not { IsAdmin: true })
        {
            return WebApi.FailJson(403, "需要管理员权限");
        }

        return null;
    }
}