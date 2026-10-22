/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： YunxiaoResponseParser
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/22
*描述：云效响应体解析器（codeup / flow 两类接口共用）
*
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效响应体解析器：<b>裸体反序列化</b> + <b>业务错误识别</b>（<c>CodeupClient</c> / <c>FlowClient</c> 共用）。
/// </summary>
/// <remarks>
/// <para>
/// <b>真机口径（2026-10-22 取证）</b>：<c>/oapi/v1</c> 下两类接口的<b>成功响应都不套 <c>{ "result": ... }</c> 外壳</b>——
/// 列表接口返回<b>裸数组</b>（如 <c>GET /flow/.../pipelines</c>）、详情接口返回<b>裸对象</b>（如 <c>GET /pipelines/{id}</c>）、
/// 创建 / 更新 / 删除返回<b>裸标量</b>（创建＝裸整数 id，更新 / 删除＝裸 <c>true</c>）。
/// 只有<b>失败</b>时才出现包裹体（<c>HTTP 4xx</c>）：
/// <c>{"errorCode":"InvalidPipeline.createOrUpdateError","errorMessage":"1209300 yaml校验失败…","requestId":"…"}</c>，
/// 其中 <c>errorCode</c> 是<b>字符串</b>（不是数字）。
/// </para>
/// <para>
/// <b>为什么单列一个类</b>：① 两类客户端共用同一口径，避免两处各写一份；② 解析规则是「真机契约」，
/// 由 <c>LubanAgent.Tests</c> 的 <c>YunxiaoResponseParserUnitTest</c> 直接锁住（客户端方法依赖 HTTP，无法单测）。
/// </para>
/// <para>
/// <b>历史坑</b>：按 <c>JsonSerializer.Deserialize&lt;YunxiaoApiResponse&lt;T&gt;&gt;</c> 拆 <c>result</c> 外壳是错的
/// ——真机裸整数 id / 裸数组会被拆成 <c>null</c>，表现为「云效明明建成功，插件却报响应解析失败」。
/// </para>
/// </remarks>
public static class YunxiaoResponseParser
{
    /// <summary>
    /// 与客户端同口径的 JSON 选项：<c>JsonSerializerDefaults.Web</c> —— camelCase 命名策略 +
    /// 属性名忽略大小写 + <b><c>AllowReadingFromString</c></b>（.NET 的 Web 默认，字符串形式的数字也吃得下）。
    /// </summary>
    /// <remarks>
    /// <b>注意</b>：数字 id 仍一律用整型承载（<c>long?</c>）、字符串 id 用 <c>string?</c>——
    /// <c>AllowReadingFromString</c> 只允许「字符串 → 数字」，<b>不</b>允许「数字 → 字符串」，
    /// 故把 id 声明成 <c>string?</c> 接真机数字 id 依旧会抛 <see cref="JsonException"/>。
    /// </remarks>
    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 反序列化<b>单个对象</b>响应（裸对象）。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="json">JSON 文本。</param>
    /// <param name="path">请求路径（仅用于错误文案）。</param>
    /// <returns>反序列化结果；空体时为 <c>default</c>。</returns>
    /// <exception cref="YunxiaoApiException">JSON 无法解析（不静默返回 <c>null</c>，否则会把解析失败伪装成「数据不存在」）。</exception>
    public static T? DeserializeObject<T>(string? json, string path)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new YunxiaoApiException(0, $"云效响应解析失败（{path}）：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 反序列化<b>数组</b>响应（云效列表接口一律返回裸数组）。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="json">JSON 文本。</param>
    /// <returns>列表；空体或根节点不是数组时为 <c>null</c>。</returns>
    /// <exception cref="YunxiaoApiException">JSON 无法解析。</exception>
    public static List<T>? DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<T>>(json, JsonOpts)
                : null;
        }
        catch (JsonException ex)
        {
            throw new YunxiaoApiException(0, $"云效列表响应解析失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 解析<b>裸标量 id</b> 响应（云效「创建」接口成功时返回裸整数 id）。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <returns>id 字符串（不变文化的十进制）；不可解析时为 <c>null</c>。</returns>
    /// <remarks>
    /// 三种形态都收：裸整数（真机口径）、裸字符串、以及含 <c>id</c> 字段的对象（个别接口会包一层）。
    /// </remarks>
    public static string? ParseId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new YunxiaoApiException(0, $"云效响应解析失败（期望 id）：{ex.Message}", ex);
        }

        using (document)
        {
            return ParseIdElement(document.RootElement);
        }
    }

    /// <summary>
    /// 把 <see cref="JsonElement"/> 归一化为 id 字符串（数字 / 字符串 / 含 <c>id</c> 的对象）。
    /// </summary>
    /// <param name="element">JSON 节点。</param>
    /// <returns>id 字符串；不可解析时为 <c>null</c>。</returns>
    private static string? ParseIdElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetInt64(out var value)
                    ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null;

            case JsonValueKind.String:
                var text = element.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();

            case JsonValueKind.Object when element.TryGetProperty("id", out var id):
                return ParseIdElement(id);

            default:
                return null;
        }
    }

    /// <summary>
    /// 识别「携带业务错误码」的响应体（HTTP 200 也可能出现，故成功分支也要过一遍）。
    /// </summary>
    /// <remarks>
    /// 云效<b>成功</b>响应没有 <c>errorCode</c> 字段，出现该字段即视为错误；
    /// 错误码可能是字符串（如 <c>InvalidPipeline.createOrUpdateError</c>）也可能是数字，
    /// 故按字符串读后再尝试转数字，转不出时用响应体里的 <c>code</c>（HTTP 语义状态码）兜底，再兜 <c>400</c>。
    /// </remarks>
    /// <param name="json">响应体。</param>
    /// <returns>错误码与信息；非错误时为 <c>null</c>。</returns>
    public static (int Code, string Message)? ParseBusinessError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        // 裸数组不可能带 errorCode（列表接口的最小可能分支，先短路避免无谓解析）
        if (json.TrimStart().StartsWith('['))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (!root.TryGetProperty("errorCode", out var errorCodeElement))
                return null;

            var rawCode = errorCodeElement.ValueKind switch
            {
                JsonValueKind.String => errorCodeElement.GetString(),
                JsonValueKind.Number => errorCodeElement.GetRawText(),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(rawCode)
                || rawCode == "0"
                || string.Equals(rawCode, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var code = int.TryParse(rawCode, out var parsed) ? parsed : 0;
            if (code == 0 && root.TryGetProperty("code", out var httpCodeElement)
                && httpCodeElement.ValueKind == JsonValueKind.Number)
            {
                code = httpCodeElement.GetInt32();
            }

            var message = ExtractErrorMessage(json) ?? "云效返回业务错误";
            return (code == 0 ? 400 : code, message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 从错误响应体中取可读信息（<c>errorMessage</c> → <c>errorDescription</c> → <c>message</c>）。
    /// </summary>
    /// <param name="json">响应体。</param>
    /// <returns>错误信息；取不到时为 <c>null</c>（调用方回退到原始文本 / 兜底文案）。</returns>
    public static string? ExtractErrorMessage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var name in new[] { "errorMessage", "errorDescription", "message" })
            {
                if (document.RootElement.TryGetProperty(name, out var element)
                    && element.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(element.GetString()))
                {
                    return element.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误体：交给调用方回退到原始文本
        }

        return null;
    }
}