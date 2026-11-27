/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： AuthDtos
*描述：注册 / 登录 / 邀请码接口的传输模型
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>登录入参。</summary>
public sealed record LoginRequest
{
    /// <summary>登录名。</summary>
    public string UserName { get; init; } = "";

    /// <summary>密码。</summary>
    public string Password { get; init; } = "";
}

/// <summary>注册入参（需要邀请码）。</summary>
public sealed record RegisterRequest
{
    /// <summary>登录名。</summary>
    public string UserName { get; init; } = "";

    /// <summary>密码（至少 8 位）。</summary>
    public string Password { get; init; } = "";

    /// <summary>显示名；空白时取登录名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>邀请码。</summary>
    public string InviteCode { get; init; } = "";
}

/// <summary>用户信息（不含密码摘要）。</summary>
/// <param name="UserId">用户标识。</param>
/// <param name="UserName">登录名。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="IsAdmin">是否管理员。</param>
/// <param name="IsBuiltIn">是否内置账号。</param>
/// <param name="LastLoginAt">最后登录时间。</param>
public sealed record AuthUserDto(
    string UserId,
    string UserName,
    string? DisplayName,
    bool IsAdmin,
    bool IsBuiltIn,
    DateTime? LastLoginAt);

/// <summary>登录 / 注册成功后的响应。</summary>
/// <param name="User">用户信息。</param>
/// <param name="ExpiresAt">令牌到期时刻。</param>
public sealed record AuthSessionDto(AuthUserDto User, DateTimeOffset ExpiresAt);

/// <summary>邀请码信息。</summary>
/// <param name="Code">邀请码。</param>
/// <param name="CreatedBy">创建者。</param>
/// <param name="ExpiresAt">过期时间。</param>
/// <param name="UsedBy">使用者；为空表示未使用。</param>
/// <param name="UsedAt">使用时间。</param>
/// <param name="Expired">是否已过期。</param>
public sealed record InviteCodeDto(
    string Code,
    string? CreatedBy,
    DateTime ExpiresAt,
    string? UsedBy,
    DateTime? UsedAt,
    bool Expired);

/// <summary>创建邀请码入参。</summary>
public sealed record InviteCreateRequest
{
    /// <summary>有效天数；缺省 7 天，范围 1–30。</summary>
    public int? ValidDays { get; init; }
}

/// <summary>仅带邀请码的请求。</summary>
public sealed record InviteCodeRequest
{
    /// <summary>邀请码。</summary>
    public string Code { get; init; } = "";
}

/// <summary>修改本人资料入参。</summary>
public sealed record UpdateProfileRequest
{
    /// <summary>显示名；空白时回退登录名。</summary>
    public string? DisplayName { get; init; }
}

/// <summary>修改本人密码入参。</summary>
public sealed record ChangePasswordRequest
{
    /// <summary>当前密码。</summary>
    public string OldPassword { get; init; } = "";

    /// <summary>新密码（至少 8 位）。</summary>
    public string NewPassword { get; init; } = "";
}

/// <summary>管理员重置密码入参。</summary>
public sealed record ResetPasswordRequest
{
    /// <summary>被重置的用户标识。</summary>
    public string UserId { get; init; } = "";
}

/// <summary>管理员重置密码结果。</summary>
/// <param name="UserId">被重置的用户标识。</param>
/// <param name="Password">随机生成的新密码；只在本次响应里返回一次。</param>
public sealed record ResetPasswordResult(string UserId, string Password);

/// <summary>登录页需要的公开状态。</summary>
/// <param name="Initialized">是否已经初始化出管理员账号。</param>
/// <param name="AllowRegistration">是否启用邀请码注册。</param>
/// <param name="User">当前登录用户；未登录（或令牌已失效）为 null。</param>
public sealed record AuthStatusDto(bool Initialized, bool AllowRegistration, AuthUserDto? User);