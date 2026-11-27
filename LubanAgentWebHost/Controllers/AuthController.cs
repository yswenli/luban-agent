/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： AuthController
*描述：登录 / 注册 / 登出 / 当前用户 / 邀请码接口
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 认证接口。
/// </summary>
/// <remarks>
/// 登录、注册、登出与状态查询在门禁里是公开路径（拿不到令牌就没人能登录）；
/// 其余动作按常规守卫校验凭据，用户与邀请码管理另要求管理员。
/// </remarks>
public sealed class AuthController : Controller
{
    /// <summary>登录页公开状态。</summary>
    /// <returns>初始化状态、注册开关与当前用户。</returns>
    [HttpGet]
    public Task<ActionResult> Status() => WebAuthService.StatusAsync();

    /// <summary>登录。</summary>
    /// <param name="payload">包装后的 <see cref="LoginRequest"/> JSON。</param>
    /// <returns>用户与到期时刻。</returns>
    [HttpPost]
    public Task<ActionResult> Login(string payload) => WebAuthService.LoginAsync(payload);

    /// <summary>凭邀请码注册。</summary>
    /// <param name="payload">包装后的 <see cref="RegisterRequest"/> JSON。</param>
    /// <returns>用户与到期时刻。</returns>
    [HttpPost]
    public Task<ActionResult> Register(string payload) => WebAuthService.RegisterAsync(payload);

    /// <summary>登出。</summary>
    /// <returns>成功壳。</returns>
    [HttpPost]
    public Task<ActionResult> Logout() => WebAuthService.LogoutAsync();

    /// <summary>当前登录用户。</summary>
    /// <returns>用户信息。</returns>
    [HttpGet]
    public Task<ActionResult> Me() => WebAuthService.MeAsync();

    /// <summary>邀请码列表（仅管理员）。</summary>
    /// <returns>邀请码列表。</returns>
    [HttpGet]
    public Task<ActionResult> Invites() => WebAuthService.InvitesAsync();

    /// <summary>创建邀请码（仅管理员）。</summary>
    /// <param name="payload">包装后的 <see cref="InviteCreateRequest"/> JSON。</param>
    /// <returns>新建的邀请码。</returns>
    [HttpPost]
    public Task<ActionResult> CreateInvite(string payload) => WebAuthService.CreateInviteAsync(payload);

    /// <summary>作废邀请码（仅管理员）。</summary>
    /// <param name="payload">包装后的 <see cref="InviteCodeRequest"/> JSON。</param>
    /// <returns>作废结果。</returns>
    [HttpPost]
    public Task<ActionResult> RevokeInvite(string payload) => WebAuthService.RevokeInviteAsync(payload);

    /// <summary>删除邀请码（仅管理员，任意状态）。</summary>
    /// <param name="payload">包装后的 <see cref="InviteCodeRequest"/> JSON。</param>
    /// <returns>删除结果。</returns>
    [HttpPost]
    public Task<ActionResult> DeleteInvite(string payload) => WebAuthService.DeleteInviteAsync(payload);

    /// <summary>修改本人密码。</summary>
    /// <param name="payload">包装后的 <see cref="ChangePasswordRequest"/> JSON。</param>
    /// <returns>成功壳。</returns>
    [HttpPost]
    public Task<ActionResult> ChangePassword(string payload) => WebAuthService.ChangePasswordAsync(payload);

    /// <summary>修改本人显示名。</summary>
    /// <param name="payload">包装后的 <see cref="UpdateProfileRequest"/> JSON。</param>
    /// <returns>更新后的用户信息。</returns>
    [HttpPost]
    public Task<ActionResult> UpdateProfile(string payload) => WebAuthService.UpdateProfileAsync(payload);

    /// <summary>用户列表（仅管理员）。</summary>
    /// <returns>全部未删除用户。</returns>
    [HttpGet]
    public Task<ActionResult> Users() => WebAuthService.UsersAsync();

    /// <summary>重置指定用户密码（仅管理员）。</summary>
    /// <param name="payload">包装后的 <see cref="ResetPasswordRequest"/> JSON。</param>
    /// <returns>被重置的用户与一次性新密码。</returns>
    [HttpPost]
    public Task<ActionResult> ResetPassword(string payload) => WebAuthService.ResetPasswordAsync(payload);
}