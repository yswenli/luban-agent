/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Client
*文件名： AgentServerException
*描述：服务端返回失败壳时抛出的异常（携带业务状态码与文案）
*
*****************************************************************************/

namespace LubanAgent.Client;

/// <summary>
/// 服务端拒绝或失败。
/// </summary>
/// <remarks>
/// <see cref="Code"/> 与服务端写入响应壳的状态码一致（400 / 401 / 403 / 404 / 502 / 503 …），
/// 客户端据此区分「会话不存在」「未授权」「宿主未启用」等语义，避免只拿字符串猜。
/// </remarks>
public sealed class AgentServerException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="code">业务/HTTP 状态码。</param>
    /// <param name="message">失败原因。</param>
    public AgentServerException(int code, string message)
        : base(message)
        => Code = code;

    /// <summary>创建异常。</summary>
    /// <param name="code">业务/HTTP 状态码。</param>
    /// <param name="message">失败原因。</param>
    /// <param name="inner">内部异常。</param>
    public AgentServerException(int code, string message, Exception inner)
        : base(message, inner)
        => Code = code;

    /// <summary>业务/HTTP 状态码。</summary>
    public int Code { get; }

    /// <summary>是否为「资源不存在」。</summary>
    public bool IsNotFound => Code == 404;
}