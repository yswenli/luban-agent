/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Security
*文件名： UserAuthService
*描述：Web 宿主用户注册、登录与 JWT 签发（内置管理员不可删除）
*
*****************************************************************************/

namespace LubanAgentCore.Security;

/// <summary>内置管理员初始化结果。</summary>
/// <param name="Created">本次是否新建了管理员。</param>
/// <param name="InitialPassword">新建时生成的初始密码；配置里显式给了密码或已存在时为 <c>null</c>。</param>
public sealed record AuthBootstrapResult(bool Created, string? InitialPassword);

/// <summary>
/// Web 宿主用户认证：内置管理员、邀请码注册、登录与令牌签发。
/// </summary>
/// <remarks>
/// 只负责用户与令牌本身；请求级鉴权（Cookie / <c>?t=</c> 的放行）由门禁调用
/// <see cref="TryValidateToken"/> 与 <see cref="ValidateAsync"/> 完成。
/// </remarks>
public sealed class UserAuthService
{
    /// <summary>内置管理员用户标识（不可改名、不可删除）。</summary>
    public const string BuiltInAdminUserId = "yswenli";

    /// <summary>内置管理员的显示名。</summary>
    public const string BuiltInAdminDisplayName = "管理员";

    /// <summary>密码最小长度。</summary>
    public const int MinPasswordLength = 8;

    /// <summary>显示名最大长度（列宽 128，这里收紧，避免顶栏与列表被超长文本撑破）。</summary>
    public const int MaxDisplayNameLength = 64;

    private const string UserNamePattern = "^[A-Za-z][A-Za-z0-9._-]{2,31}$";
    private const string InviteAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int InviteCodeLength = 8;

    private readonly JwtTokenService _tokens;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _maxLifetime;
    private readonly AgentUserRepository _users = new();
    private readonly InviteCodeRepository _invites = new();

    /// <summary>创建认证服务。</summary>
    /// <param name="tokens">JWT 签发器。</param>
    /// <param name="lifetime">单个令牌有效期。</param>
    /// <param name="maxLifetime">整个会话的绝对上限；<see cref="TimeSpan.Zero"/> 表示不设上限。</param>
    /// <param name="time">时间源（单测注入）。</param>
    public UserAuthService(JwtTokenService tokens, TimeSpan lifetime, TimeSpan maxLifetime, TimeProvider? time = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "令牌有效期必须大于 0");
        }

        if (maxLifetime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLifetime), "会话绝对上限不能为负数");
        }

        _lifetime = lifetime;
        _maxLifetime = maxLifetime;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>单个令牌有效期。</summary>
    public TimeSpan TokenLifetime => _lifetime;

    /// <summary>当前时间（UTC）。</summary>
    public DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>
    /// 确保内置管理员存在；<paramref name="configuredPassword"/> 为空时生成随机初始密码。
    /// </summary>
    /// <param name="configuredPassword">配置中显式指定的管理员密码。</param>
    /// <returns>初始化结果；新建且未配置密码时带回初始密码。</returns>
    /// <exception cref="AuthException">已有同标识账号既不是管理员也不是内置账号（数据被手工改动）。</exception>
    public async Task<AuthBootstrapResult> EnsureBuiltInAdminAsync(string? configuredPassword)
    {
        var existing = await _users.GetByUserIdAsync(BuiltInAdminUserId).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!existing.IsBuiltIn || !existing.IsAdmin)
            {
                await _users.UpdateAsync(
                    u => new DbAgentUser { IsBuiltIn = true, IsAdmin = true, UpdateTime = DateTime.Now },
                    u => u.Id == existing.Id).ConfigureAwait(false);
            }

            return new AuthBootstrapResult(false, null);
        }

        var generated = string.IsNullOrWhiteSpace(configuredPassword);
        var password = generated ? CreateRandomPassword() : configuredPassword!.Trim();
        if (password.Length < MinPasswordLength)
        {
            throw new AuthException(400, $"管理员密码至少 {MinPasswordLength} 位");
        }

        await _users.InsertAsync(new DbAgentUser
        {
            UserId = BuiltInAdminUserId,
            UserName = BuiltInAdminUserId,
            DisplayName = BuiltInAdminDisplayName,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = true,
            IsBuiltIn = true,
            TokenVersion = 1,
            CreateTime = DateTime.Now
        }).ConfigureAwait(false);

        return new AuthBootstrapResult(true, generated ? password : null);
    }

    /// <summary>
    /// 登录并签发令牌。
    /// </summary>
    /// <param name="userName">登录名。</param>
    /// <param name="password">明文密码。</param>
    /// <returns>用户、令牌与到期时刻。</returns>
    /// <exception cref="AuthException">用户名或密码错误、账号被禁用。</exception>
    public async Task<(DbAgentUser User, string Token, DateTimeOffset ExpiresAt)> LoginAsync(string? userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            throw new AuthException(400, "用户名和密码不能为空");
        }

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var user = await _users.GetByUserNameAsync(userName).ConfigureAwait(false);

        // 未知用户名同样回通用 401：不落失败计数，否则等于给出用户名字典探测通道
        if (user is null)
        {
            throw new AuthException(401, LoginLockoutPolicy.InvalidCredentialsMessage);
        }

        if (LoginLockoutPolicy.IsLocked(user.LockedUntil, nowUtc))
        {
            throw new AuthException(429, LoginLockoutPolicy.BuildLockedMessage(user.LockedUntil, nowUtc));
        }

        // 上次锁定已到期：从零重新计数，避免「到期后再错一次又锁 30 分钟」
        if (user.LockedUntil is not null || user.FailedLoginCount >= LoginLockoutPolicy.MaxFailedAttempts)
        {
            await _users.ClearLoginFailureAsync(user.UserId).ConfigureAwait(false);
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            // 计数用单条 UPDATE 自增拿到真实值，再按规则决定是否落锁定
            var failed = await _users.RegisterLoginFailureAsync(user.UserId).ConfigureAwait(false);
            var lockedUntil = LoginLockoutPolicy.NextLockedUntil(failed, user.LockedUntil, nowUtc);
            if (lockedUntil != user.LockedUntil)
            {
                await _users.SetLoginLockAsync(user.UserId, failed, lockedUntil).ConfigureAwait(false);
            }

            if (LoginLockoutPolicy.IsLocked(lockedUntil, nowUtc))
            {
                throw new AuthException(429, LoginLockoutPolicy.BuildLockedMessage(lockedUntil, nowUtc));
            }

            throw new AuthException(401, LoginLockoutPolicy.InvalidCredentialsMessage);
        }

        if (user.Disabled)
        {
            throw new AuthException(403, "账号已被禁用，请联系管理员");
        }

        // 历史数据修复：早期账号可能把密码误存成显示名，登录时能拿到明文，一比即可判定并回退登录名
        if (!string.IsNullOrWhiteSpace(user.DisplayName)
            && string.Equals(user.DisplayName.Trim(), password, StringComparison.Ordinal))
        {
            await _users.UpdateDisplayNameAsync(user.UserId, user.UserName).ConfigureAwait(false);
            user.DisplayName = user.UserName;
        }

        await _users.TouchLoginAsync(user.UserId, _time.GetUtcNow().UtcDateTime).ConfigureAwait(false);
        user.LastLoginAt = _time.GetUtcNow().UtcDateTime;

        var claims = BuildClaims(user, _time.GetUtcNow());
        return (user, _tokens.Create(claims), claims.ExpiresAt);
    }

    /// <summary>
    /// 用邀请码注册并签发令牌。
    /// </summary>
    /// <param name="request">注册入参。</param>
    /// <returns>用户、令牌与到期时刻。</returns>
    /// <exception cref="AuthException">入参不合法、邀请码无效或用户名已存在。</exception>
    public async Task<(DbAgentUser User, string Token, DateTimeOffset ExpiresAt)> RegisterAsync(RegisterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userName = NormalizeUserName(request.UserName);
        if (!Regex.IsMatch(userName, UserNamePattern, RegexOptions.CultureInvariant))
        {
            throw new AuthException(400, "用户名需以字母开头，3–32 位，只含字母、数字、点、下划线与连字符");
        }

        if (string.Equals(userName, BuiltInAdminUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthException(409, "该用户名不可用");
        }

        var password = RequirePassword(request.Password);

        // 显示名误填成密码（密码管理器自动填充、手滑粘贴等）时直接拒绝：
        // 落库后它会在账号页与顶栏明文展示口令。
        var requestedDisplayName = (request.DisplayName ?? string.Empty).Trim();
        if (requestedDisplayName.Length > 0
            && string.Equals(requestedDisplayName, password, StringComparison.Ordinal))
        {
            throw new AuthException(400, "显示名不能与密码相同");
        }

        var displayName = SanitizeDisplayName(requestedDisplayName, userName);

        // 语言只是展示偏好，非法值不值得让注册失败：按未设置处理
        var language = SupportedLanguages.TryNormalize(request.Language, out var normalizedLanguage)
            ? normalizedLanguage
            : null;

        var code = (request.InviteCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            throw new AuthException(400, "请填写邀请码");
        }

        var invite = await _invites.GetByCodeAsync(code).ConfigureAwait(false);
        if (invite is null)
        {
            throw new AuthException(400, "邀请码无效");
        }

        if (invite.UsedBy is not null)
        {
            throw new AuthException(409, "邀请码已被使用");
        }

        if (invite.ExpiresAt <= _time.GetUtcNow().UtcDateTime)
        {
            throw new AuthException(400, "邀请码已过期");
        }

        var userId = userName.ToLowerInvariant();
        if (await _users.GetAnyByUserIdAsync(userId).ConfigureAwait(false) is not null)
        {
            throw new AuthException(409, "用户名已存在");
        }

        var user = new DbAgentUser
        {
            UserId = userId,
            UserName = userName,
            DisplayName = displayName,
            Language = language,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = false,
            IsBuiltIn = false,
            TokenVersion = 1,
            LastLoginAt = _time.GetUtcNow().UtcDateTime,
            CreateTime = DateTime.Now
        };

        try
        {
            await _users.InsertAsync(user).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 并发注册同一用户名：唯一约束在落库时才会失败，回查确认后按 409 返回，避免冒成 500
            if (await _users.GetAnyByUserIdAsync(userId).ConfigureAwait(false) is not null)
            {
                throw new AuthException(409, "用户名已存在");
            }

            throw;
        }

        // 先建用户再原子占用邀请码：并发下只有一个能抢到，抢不到的账号回滚（软删）
        var claimed = await _invites.TryClaimAsync(code, userId, _time.GetUtcNow().UtcDateTime).ConfigureAwait(false);
        if (!claimed)
        {
            await _users.LogicDeleteAsync(u => u.Id == user.Id).ConfigureAwait(false);
            throw new AuthException(409, "邀请码已被使用");
        }

        var claims = BuildClaims(user, _time.GetUtcNow());
        return (user, _tokens.Create(claims), claims.ExpiresAt);
    }

    /// <summary>
    /// 校验令牌签名与有效期（不查库）。
    /// </summary>
    /// <param name="token">JWT。</param>
    /// <param name="claims">校验通过时的声明。</param>
    /// <param name="ignoreExpiry">是否忽略到期时间。</param>
    /// <returns>签名正确且未过期返回 true。</returns>
    public bool TryValidateToken(string? token, out WebTokenClaims? claims, bool ignoreExpiry = false)
        => _tokens.TryValidate(token, out claims, ignoreExpiry);

    /// <summary>
    /// 续签：保留身份与会话起点，只把到期时间往后推。
    /// </summary>
    /// <param name="claims">原声明。</param>
    /// <returns>新 JWT。</returns>
    public string RenewToken(WebTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var now = _time.GetUtcNow();
        var expires = now + _lifetime;
        if (_maxLifetime > TimeSpan.Zero)
        {
            var limit = claims.SessionStartedAt + _maxLifetime;
            if (expires > limit)
            {
                expires = limit;
            }
        }

        return _tokens.Create(claims with { IssuedAt = now, ExpiresAt = expires });
    }

    /// <summary>
    /// 按声明复核用户仍然有效：未删除、未禁用、令牌版本一致。
    /// </summary>
    /// <param name="claims">令牌声明。</param>
    /// <returns>用户；无效时为 <c>null</c>。</returns>
    public async Task<DbAgentUser?> ValidateAsync(WebTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        if (_maxLifetime > TimeSpan.Zero
            && _time.GetUtcNow() - claims.SessionStartedAt >= _maxLifetime)
        {
            return null;
        }

        var user = await _users.GetByUserIdAsync(claims.UserId).ConfigureAwait(false);
        if (user is null || user.Disabled || user.TokenVersion != claims.Version)
        {
            return null;
        }

        return user;
    }

    /// <summary>按用户标识取用户（未删除）。</summary>
    /// <param name="userId">用户标识。</param>
    /// <returns>用户；不存在时为 <c>null</c>。</returns>
    public Task<DbAgentUser?> GetUserAsync(string userId)
        => _users.GetByUserIdAsync(userId);

    /// <summary>
    /// 为指定用户签发一个入口令牌（供宿主运维入口与二维码使用）。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <returns>令牌与到期时刻。</returns>
    /// <exception cref="AuthException">用户不存在。</exception>
    public async Task<(string Token, DateTimeOffset ExpiresAt)> CreateEntryTokenAsync(string userId)
    {
        var user = await _users.GetByUserIdAsync(userId).ConfigureAwait(false)
                   ?? throw new AuthException(404, "宿主身份对应用户不存在");
        if (user.Disabled)
        {
            throw new AuthException(403, "宿主身份对应账号已被禁用");
        }

        var claims = BuildClaims(user, _time.GetUtcNow());
        return (_tokens.Create(claims), claims.ExpiresAt);
    }

    /// <summary>
    /// 确保宿主身份对应用户存在（内置管理员已存在时跳过）。
    /// </summary>
    /// <remarks>
    /// <c>--user</c> 指定非默认身份时，数据目录仍按该标识隔离；此账号的密码不对外发放，
    /// 只用于让宿主身份在用户表里有记录、入口令牌能签发。
    /// </remarks>
    /// <param name="userId">宿主身份。</param>
    /// <returns>确保完成的任务。</returns>
    public async Task EnsureHostIdentityAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)
            || string.Equals(userId, BuiltInAdminUserId, StringComparison.Ordinal))
        {
            return;
        }

        if (await _users.GetByUserIdAsync(userId).ConfigureAwait(false) is not null)
        {
            return;
        }

        await _users.InsertAsync(new DbAgentUser
        {
            UserId = userId,
            UserName = userId,
            DisplayName = userId,
            PasswordHash = PasswordHasher.Hash(CreateRandomPassword()),
            IsAdmin = false,
            IsBuiltIn = false,
            TokenVersion = 1,
            CreateTime = DateTime.Now
        }).ConfigureAwait(false);
    }

    /// <summary>创建邀请码。</summary>
    /// <param name="createdBy">创建者用户标识。</param>
    /// <param name="validDays">有效天数；缺省 7，范围 1–30。</param>
    /// <returns>邀请码信息。</returns>
    public async Task<InviteCodeDto> CreateInviteAsync(string createdBy, int? validDays)
    {
        var days = validDays is >= 1 and <= 30 ? validDays.Value : 7;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = CreateInviteCode();
            if (await _invites.GetByCodeAsync(code).ConfigureAwait(false) is not null)
            {
                continue;
            }

            var entity = new DbInviteCode
            {
                Code = code,
                CreatedBy = createdBy,
                ExpiresAt = _time.GetUtcNow().AddDays(days).UtcDateTime,
                CreateTime = DateTime.Now
            };

            await _invites.InsertAsync(entity).ConfigureAwait(false);
            return ToDto(entity);
        }

        throw new AuthException(500, "邀请码生成失败，请重试");
    }

    /// <summary>列出邀请码（新到旧）。</summary>
    /// <returns>邀请码列表。</returns>
    public async Task<List<InviteCodeDto>> ListInvitesAsync()
    {
        var list = await _invites.ListAsync().ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    /// <summary>作废邀请码（仅限未使用）。</summary>
    /// <param name="code">邀请码。</param>
    /// <returns>作废成功返回 true。</returns>
    public Task<bool> RevokeInviteAsync(string code)
        => _invites.LogicDeleteAsync(c => c.Code == code && c.UsedBy == null);

    /// <summary>删除邀请码（任意状态，软删除后不再出现在列表里）。</summary>
    /// <param name="code">邀请码。</param>
    /// <returns>删除成功返回 true。</returns>
    public Task<bool> DeleteInviteAsync(string code)
        => _invites.LogicDeleteAsync(c => c.Code == code);

    /// <summary>
    /// 修改本人密码：先校验旧密码，成功后递增令牌版本。
    /// </summary>
    /// <remarks>
    /// 令牌版本递增会让所有已签发的旧 JWT 在下次动作校验时失效，因此改密等于把其他设备踢下线；
    /// 当前浏览器需要由调用方换发新 Cookie 才能继续用。
    /// </remarks>
    /// <param name="userId">用户标识。</param>
    /// <param name="oldPassword">当前密码。</param>
    /// <param name="newPassword">新密码。</param>
    /// <returns>令牌版本已递增的用户实体。</returns>
    /// <exception cref="AuthException">用户不存在、旧密码不正确或新密码不合规。</exception>
    public async Task<DbAgentUser> ChangePasswordAsync(string userId, string? oldPassword, string? newPassword)
    {
        var user = await _users.GetByUserIdAsync(userId).ConfigureAwait(false)
                   ?? throw new AuthException(404, "用户不存在");

        if (!PasswordHasher.Verify(oldPassword ?? string.Empty, user.PasswordHash))
        {
            throw new AuthException(400, "当前密码不正确");
        }

        var password = RequirePassword(newPassword);
        await UpdatePasswordAsync(user, password).ConfigureAwait(false);
        return user;
    }

    /// <summary>
    /// 管理员重置他人密码：生成随机新密码并返回一次，旧密码与旧令牌同时失效。
    /// </summary>
    /// <remarks>
    /// 内置管理员的密码默认不能由他人重置：它是最上层账号，被他人重置会绕过「谁掌握宿主」的边界。
    /// 仅在宿主本机离线恢复命令里传 <paramref name="allowBuiltIn"/>=true 才放行。
    /// </remarks>
    /// <param name="targetUserId">被重置的用户标识。</param>
    /// <param name="allowBuiltIn">是否允许重置内置管理员（仅离线恢复使用）。</param>
    /// <returns>新密码明文（仅此一次返回，不落库）。</returns>
    /// <exception cref="AuthException">用户不存在，或目标是内置管理员且未显式放行。</exception>
    public async Task<string> ResetPasswordAsync(string targetUserId, bool allowBuiltIn = false)
    {
        var user = await _users.GetByUserIdAsync(targetUserId).ConfigureAwait(false)
                   ?? throw new AuthException(404, "用户不存在");

        if (user.IsBuiltIn && !allowBuiltIn)
        {
            throw new AuthException(400, "内置管理员的密码不能由他人重置，请由该账号自行修改");
        }

        var password = CreateRandomPassword();
        await UpdatePasswordAsync(user, password).ConfigureAwait(false);
        return password;
    }

    /// <summary>
    /// 管理员解锁：清零失败计数并清除锁定到期时间。
    /// </summary>
    /// <param name="targetUserId">目标用户标识。</param>
    /// <returns>原本处于锁定或计数状态并已清除时返回 true。</returns>
    /// <exception cref="AuthException">用户不存在。</exception>
    public async Task<bool> UnlockAsync(string targetUserId)
    {
        var user = await _users.GetByUserIdAsync(targetUserId).ConfigureAwait(false)
                   ?? throw new AuthException(404, "用户不存在");

        if (user.FailedLoginCount == 0 && user.LockedUntil is null)
        {
            return false;
        }

        await _users.ClearLoginFailureAsync(user.UserId).ConfigureAwait(false);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        return true;
    }

    /// <summary>列出全部未删除用户（管理员视图）。</summary>
    /// <returns>用户列表（按创建时间正序）。</returns>
    public async Task<List<AuthUserDto>> ListUsersAsync()
    {
        var list = await _users.ListAsync().ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    /// <summary>
    /// 修改本人显示名；登录名与用户标识不变。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="displayName">新显示名；空白时回退登录名。</param>
    /// <returns>更新后的用户信息。</returns>
    /// <exception cref="AuthException">用户不存在、显示名超长或与密码相同。</exception>
    public async Task<AuthUserDto> UpdateDisplayNameAsync(string userId, string? displayName)
    {
        var user = await _users.GetByUserIdAsync(userId).ConfigureAwait(false)
                   ?? throw new AuthException(404, "用户不存在");

        var provided = (displayName ?? string.Empty).Trim();
        if (provided.Length > 0 && PasswordHasher.Verify(provided, user.PasswordHash))
        {
            throw new AuthException(400, "显示名不能与密码相同");
        }

        var normalized = SanitizeDisplayName(provided, user.UserName);
        if (!string.Equals(user.DisplayName, normalized, StringComparison.Ordinal))
        {
            await _users.UpdateDisplayNameAsync(user.UserId, normalized).ConfigureAwait(false);
            user.DisplayName = normalized;
        }

        return ToDto(user);
    }

    /// <summary>
    /// 修改本人界面语言。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="language">界面语言代码；必须在 <see cref="SupportedLanguages"/> 白名单内。</param>
    /// <returns>更新后的用户信息。</returns>
    /// <exception cref="AuthException">用户不存在或语言代码不受支持。</exception>
    public async Task<AuthUserDto> UpdateLanguageAsync(string userId, string? language)
    {
        if (!SupportedLanguages.TryNormalize(language, out var normalized))
        {
            throw new AuthException(400, "界面语言不受支持");
        }

        var user = await _users.GetByUserIdAsync(userId).ConfigureAwait(false)
                   ?? throw new AuthException(404, "用户不存在");

        if (!string.Equals(user.Language, normalized, StringComparison.Ordinal))
        {
            await _users.UpdateLanguageAsync(user.UserId, normalized).ConfigureAwait(false);
            user.Language = normalized;
        }

        return ToDto(user);
    }

    /// <summary>把用户实体转成不含密码的传输模型。</summary>
    /// <param name="user">用户实体。</param>
    /// <returns>用户信息。</returns>
    public static AuthUserDto ToDto(DbAgentUser user)
        => new(
            user.UserId,
            user.UserName,
            user.DisplayName,
            user.IsAdmin,
            user.IsBuiltIn,
            user.LastLoginAt,
            user.FailedLoginCount,
            user.LockedUntil,
            user.Language);

    private WebTokenClaims BuildClaims(DbAgentUser user, DateTimeOffset now)
    {
        var expires = now + _lifetime;
        if (_maxLifetime > TimeSpan.Zero && expires > now + _maxLifetime)
        {
            expires = now + _maxLifetime;
        }

        return new WebTokenClaims(
            user.UserId,
            user.UserName,
            user.IsAdmin,
            user.TokenVersion,
            now,
            expires,
            now);
    }

    private InviteCodeDto ToDto(DbInviteCode code)
        => ToDto(code, _time.GetUtcNow().UtcDateTime);

    /// <summary>把邀请码实体转成传输模型，过期判定使用调用方给出的时间。</summary>
    /// <param name="code">邀请码实体。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <returns>邀请码信息。</returns>
    internal static InviteCodeDto ToDto(DbInviteCode code, DateTime nowUtc)
        => new(
            code.Code,
            code.CreatedBy,
            code.ExpiresAt,
            code.UsedBy,
            code.UsedAt,
            code.ExpiresAt <= nowUtc);

    private static string NormalizeUserName(string? userName)
        => (userName ?? string.Empty).Trim();

    /// <summary>
    /// 收敛显示名：去首尾空白，空白时回退登录名。
    /// </summary>
    /// <param name="value">用户输入。</param>
    /// <param name="userName">登录名（空白时的回退值）。</param>
    /// <returns>可直接落库的显示名。</returns>
    /// <exception cref="AuthException">超过 <see cref="MaxDisplayNameLength"/> 个字符。</exception>
    internal static string SanitizeDisplayName(string? value, string userName)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return userName;
        }

        if (trimmed.Length > MaxDisplayNameLength)
        {
            throw new AuthException(400, $"显示名最多 {MaxDisplayNameLength} 个字符");
        }

        return trimmed;
    }

    /// <summary>校验密码长度；不合规直接抛 <see cref="AuthException"/>。</summary>
    /// <param name="password">明文密码。</param>
    /// <returns>原样的密码。</returns>
    private static string RequirePassword(string? password)
    {
        var value = password ?? string.Empty;
        if (value.Length < MinPasswordLength)
        {
            throw new AuthException(400, $"密码至少 {MinPasswordLength} 位");
        }

        return value;
    }

    /// <summary>换掉密码摘要并递增令牌版本，使旧令牌立即失效。</summary>
    /// <param name="user">目标用户。</param>
    /// <param name="password">新密码明文。</param>
    /// <returns>更新任务。</returns>
    private async Task UpdatePasswordAsync(DbAgentUser user, string password)
    {
        await _users.UpdateAsync(
            u => new DbAgentUser
            {
                PasswordHash = PasswordHasher.Hash(password),
                TokenVersion = user.TokenVersion + 1,
                FailedLoginCount = 0,
                LockedUntil = null,
                UpdateTime = DateTime.Now
            },
            u => u.Id == user.Id).ConfigureAwait(false);

        // 保持内存实体与库一致：调用方随后要按新版本签发令牌
        user.TokenVersion += 1;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        // 新密码恰好等于显示名时，显示名会明文泄露口令：回退登录名（显示与「未设置」等价）
        if (!string.IsNullOrWhiteSpace(user.DisplayName)
            && string.Equals(user.DisplayName.Trim(), password, StringComparison.Ordinal))
        {
            await _users.UpdateDisplayNameAsync(user.UserId, user.UserName).ConfigureAwait(false);
            user.DisplayName = user.UserName;
        }
    }

    private static string CreateInviteCode()
    {
        Span<char> buffer = stackalloc char[InviteCodeLength];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = InviteAlphabet[RandomNumberGenerator.GetInt32(InviteAlphabet.Length)];
        }

        return new string(buffer);
    }

    private static string CreateRandomPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        Span<char> buffer = stackalloc char[16];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(buffer);
    }
}