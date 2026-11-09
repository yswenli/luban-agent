/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol
*文件名： ApiEnvelope
*描述：统一响应壳（成功/失败共用），客户端据 Success + Code 判定
*
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentCore.Protocol;

/// <summary>
/// 统一响应壳。
/// </summary>
/// <param name="Success">是否成功（HTTP 状态码仍表达同一语义，两者一致）。</param>
/// <param name="Code">业务/HTTP 状态码。</param>
/// <param name="Message">失败原因（成功为 null）。</param>
/// <param name="Data">载荷（成功时可能有，失败为 null）。</param>
public sealed record ApiEnvelope(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("data")] JsonElement? Data = null)
{
    /// <summary>构造成功响应。</summary>
    /// <param name="data">载荷对象；null 表示无载荷。</param>
    /// <returns>响应壳。</returns>
    public static ApiEnvelope Ok(object? data = null)
        => new(true, 200, null, data is null
            ? null
            : JsonSerializer.SerializeToElement(data, data.GetType(), AgentApiJson.Options));

    /// <summary>构造失败响应。</summary>
    /// <param name="code">状态码。</param>
    /// <param name="message">失败原因。</param>
    /// <returns>响应壳。</returns>
    public static ApiEnvelope Fail(int code, string message) => new(false, code, message);

    /// <summary>把 <see cref="Data"/> 还原为强类型载荷。</summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <returns>载荷；无载荷时返回 default。</returns>
    public T? GetData<T>() => Data is { } element ? element.Deserialize<T>(AgentApiJson.Options) : default;
}