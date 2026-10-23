/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Utils
*文件名： SessionToolCallHistory
*版本号： V1.0.0.0
*唯一标识：会话历史里的工具调用回放
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/23
*描述：把会话库落库的 tool_calls JSON 还原成可供 UI 回放的工具调用记录。
*     落库形态见框架 SessionChatHistoryProvider.SerializeToolCalls：
*     [{"name":"工具名","callId":"调用ID","arguments":"<参数 JSON 字符串>"}]
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// 会话历史里的一条工具调用记录。
/// </summary>
/// <param name="Name">工具名。</param>
/// <param name="CallId">调用 ID（可能为空）。</param>
/// <param name="Arguments">已解析的参数（不可解析时为空表）。</param>
public sealed record HistoryToolCall(
    string Name,
    string? CallId,
    IReadOnlyDictionary<string, object?> Arguments);

/// <summary>
/// 会话历史工具调用的解析器：CLI 与 GUI 共用的历史回放口径。
/// </summary>
/// <remarks>
/// 解析必须「不抛异常、不丢整条消息」：库里可能混有历史版本写下的异常形态，
/// 单条解析失败只跳过该条，不影响同一条消息里的其它工具调用。
/// </remarks>
public static class SessionToolCallHistory
{
    /// <summary>空参数表（复用，避免每次解析都新建）。</summary>
    private static readonly IReadOnlyDictionary<string, object?> EmptyArguments
        = new Dictionary<string, object?>();

    /// <summary>落库字段名为小写 camelCase，反序列化时忽略大小写。</summary>
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 把落库的 <c>tool_calls</c> JSON 还原成工具调用记录列表。
    /// </summary>
    /// <param name="json">落库的 tool_calls JSON；为空或不可解析时返回空列表（不抛异常）。</param>
    /// <returns>工具调用记录（保持落库顺序）。</returns>
    public static IReadOnlyList<HistoryToolCall> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<HistoryToolCall>();

        List<ToolCallRecord>? records;
        try
        {
            records = System.Text.Json.JsonSerializer.Deserialize<List<ToolCallRecord>>(json, JsonOptions);
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<HistoryToolCall>();
        }

        if (records is null || records.Count == 0)
            return Array.Empty<HistoryToolCall>();

        var calls = new List<HistoryToolCall>(records.Count);
        foreach (var record in records)
        {
            // 工具名是回放的唯一必需字段；缺失则无法渲染成卡片，直接跳过
            if (string.IsNullOrWhiteSpace(record.Name))
                continue;

            calls.Add(new HistoryToolCall(record.Name!, record.CallId, ParseArguments(record.Arguments)));
        }

        return calls;
    }

    /// <summary>
    /// 把工具参数 JSON 转成 UI 绑定用的字典。
    /// </summary>
    /// <remarks>
    /// 字符串取原值（避免 JSON 引号污染摘要），数字/对象/数组取原始文本，
    /// 布尔与 null 取对应值——与流式回合里 <c>ToolCallStartedEvent.Arguments</c> 的形态对齐。
    /// </remarks>
    /// <param name="json">参数 JSON 字符串。</param>
    /// <returns>参数字典；为空或不可解析时返回空表。</returns>
    public static IReadOnlyDictionary<string, object?> ParseArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return EmptyArguments;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return EmptyArguments;

            var args = new Dictionary<string, object?>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                args[property.Name] = property.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => property.Value.GetString(),
                    System.Text.Json.JsonValueKind.Number => property.Value.GetRawText(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }

            return args;
        }
        catch (System.Text.Json.JsonException)
        {
            return EmptyArguments;
        }
    }

    /// <summary>
    /// 落库的工具调用形态（与框架 <c>SerializeToolCalls</c> 的匿名对象字段一致）。
    /// </summary>
    private sealed class ToolCallRecord
    {
        /// <summary>工具名。</summary>
        public string? Name { get; set; }

        /// <summary>调用 ID。</summary>
        public string? CallId { get; set; }

        /// <summary>参数 JSON 字符串。</summary>
        public string? Arguments { get; set; }
    }
}