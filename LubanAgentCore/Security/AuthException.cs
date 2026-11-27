/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Security
*文件名： AuthException
*描述：注册 / 登录业务失败（携带可回传给调用方的状态码）
*
*****************************************************************************/

namespace LubanAgentCore.Security;

/// <summary>
/// 认证业务失败。
/// </summary>
/// <remarks>
/// 与参数校验区分：<see cref="Code"/> 直接作为 HTTP 状态码回传，
/// 文案已面向用户，不含本机路径与库内细节。
/// </remarks>
public sealed class AuthException : Exception
{
    /// <summary>创建认证异常。</summary>
    /// <param name="code">HTTP 状态码。</param>
    /// <param name="message">面向用户的文案。</param>
    public AuthException(int code, string message)
        : base(message)
        => Code = code;

    /// <summary>HTTP 状态码（400 / 401 / 403 / 409）。</summary>
    public int Code { get; }
}