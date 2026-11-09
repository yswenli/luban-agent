/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol
*文件名： AgentApiJson
*描述：API 传输口径的 JSON 序列化约定与请求体扁平包装
*
*****************************************************************************/

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentCore.Protocol;

/// <summary>
/// API 传输口径：camelCase、字段大小写不敏感、不缩进、不转义中文。
/// </summary>
/// <remarks>
/// <para>
/// 请求体必须用单字段扁平包装（<c>{"payload":"&lt;内层 JSON&gt;"}</c>），
/// 原因见 <see cref="WrapPayload{T}"/>：宿主只按 <c>Dictionary&lt;string,string&gt;</c> 反解 JSON 体，
/// 嵌套对象会被判为错误请求。
/// </para>
/// </remarks>
public static class AgentApiJson
{
    /// <summary>请求体扁平包装字段名（与控制器参数名一致，大小写敏感）。</summary>
    public const string PayloadField = "payload";

    /// <summary>统一序列化选项。</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>序列化。</summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="value">值。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>反序列化；空文本返回 default。</summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="json">JSON 文本。</param>
    /// <returns>反序列化结果。</returns>
    public static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>
    /// 把值序列化为 JSON，再包成只有 <c>payload</c> 一个字符串字段的请求体。
    /// </summary>
    /// <remarks>
    /// 宿主的 JSON 体解析只支持 <c>Dictionary&lt;string,string&gt;</c> / <c>List&lt;object&gt;</c> 两种形态，
    /// 嵌套对象会让请求在进入 action 之前就被判 400；因此复杂入参统一压成单字段字符串。
    /// </remarks>
    /// <typeparam name="T">入参类型。</typeparam>
    /// <param name="value">入参。</param>
    /// <returns>可直接作为请求体的 JSON 文本。</returns>
    public static string WrapPayload<T>(T value)
        => Serialize(new Dictionary<string, string> { [PayloadField] = Serialize(value) });

    /// <summary>取出 <see cref="PayloadField"/> 的内层 JSON 并反序列化。</summary>
    /// <typeparam name="T">入参类型。</typeparam>
    /// <param name="payload">控制器收到的内层 JSON 文本。</param>
    /// <returns>反序列化结果；空文本返回 default。</returns>
    public static T? UnwrapPayload<T>(string? payload) => Deserialize<T>(payload);
}