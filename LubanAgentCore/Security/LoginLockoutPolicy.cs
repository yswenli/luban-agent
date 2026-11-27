/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Security
*文件名： LoginLockoutPolicy
*描述：登录失败计数与锁定规则（纯函数，便于单测）
*
*****************************************************************************/

namespace LubanAgentCore.Security;

/// <summary>
/// 登录失败锁定规则：连续失败达到阈值后锁定固定时长。
/// </summary>
/// <remarks>
/// 只对已存在账号计数；未知用户名不落任何状态，避免把锁定状态变成用户名字典探测通道。
/// 锁定到期后从零重新计数，避免「到期后再错一次又锁 30 分钟」。
/// </remarks>
public static class LoginLockoutPolicy
{
    /// <summary>触发锁定的连续失败次数。</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>每次触发的锁定时长。</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    /// <summary>凭据错误时的统一文案（不区分用户名不存在与密码错误）。</summary>
    public const string InvalidCredentialsMessage = "用户名或密码错误";

    /// <summary>账号是否处于锁定期内。</summary>
    /// <param name="lockedUntil">锁定到期时间（UTC）；为空表示未锁定。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <returns>仍在锁定期内返回 true。</returns>
    public static bool IsLocked(DateTime? lockedUntil, DateTime nowUtc)
        => lockedUntil is { } until && until > nowUtc;

    /// <summary>锁定剩余时长；未锁定返回 <see cref="TimeSpan.Zero"/>。</summary>
    /// <param name="lockedUntil">锁定到期时间（UTC）。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <returns>剩余时长。</returns>
    public static TimeSpan RemainingLockout(DateTime? lockedUntil, DateTime nowUtc)
        => IsLocked(lockedUntil, nowUtc) ? lockedUntil!.Value - nowUtc : TimeSpan.Zero;

    /// <summary>锁定剩余分钟数（向上取整，未锁定时为 0）。</summary>
    /// <param name="lockedUntil">锁定到期时间（UTC）。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <returns>剩余分钟数。</returns>
    public static int RemainingMinutes(DateTime? lockedUntil, DateTime nowUtc)
    {
        var remaining = RemainingLockout(lockedUntil, nowUtc);
        return remaining <= TimeSpan.Zero ? 0 : Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
    }

    /// <summary>本次失败后应写入的锁定到期时间。</summary>
    /// <param name="failedCount">本次失败后的失败次数。</param>
    /// <param name="lockedUntil">当前锁定到期时间（UTC）。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <returns>新的锁定到期时间；未触发锁定或锁定已到期时为 <c>null</c>。</returns>
    public static DateTime? NextLockedUntil(int failedCount, DateTime? lockedUntil, DateTime nowUtc)
    {
        if (IsLocked(lockedUntil, nowUtc))
        {
            return lockedUntil;
        }

        // 只在「本次失败刚好达到阈值」时落锁：计数由登录流程在锁定到期后清零，重计不会一错又锁
        return failedCount == MaxFailedAttempts ? nowUtc + LockoutDuration : null;
    }

    /// <summary>锁定期的提示文案，含剩余分钟数。</summary>
    /// <param name="lockedUntil">锁定到期时间（UTC）。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <returns>可直接展示给用户的文案。</returns>
    public static string BuildLockedMessage(DateTime? lockedUntil, DateTime nowUtc)
    {
        var minutes = Math.Max(1, RemainingMinutes(lockedUntil, nowUtc));
        return $"密码错误次数过多，账号已锁定，请在 {minutes} 分钟后重试，或联系管理员解锁";
    }
}