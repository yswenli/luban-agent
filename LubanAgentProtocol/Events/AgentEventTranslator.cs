/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Protocol.Events
*文件名： AgentEventTranslator
*描述：SSE 帧 → 框架 AgentStreamEvent 的还原（客户端宿主使用）
*
*****************************************************************************/

using System.Text.Json;
using LuBan.AIAgent.Hosting;

namespace LubanAgent.Protocol.Events;

/// <summary>
/// 把服务端下发的 SSE 帧还原成框架事件对象。
/// </summary>
/// <remarks>
/// <para>
/// 只做帧 → 事件方向的还原：服务端写帧走框架的 <c>AgentEventProtocol.ToFrame</c>，
/// 客户端读帧用本类，两者的事件名与载荷字段必须成对维护。
/// </para>
/// <para>
/// <c>session</c> / <c>done</c> / <c>plan.item</c> / <c>tool.denied</c> 不产生
/// <see cref="AgentStreamEvent"/>，由客户端宿主另行处理，本类返回 false。
/// </para>
/// </remarks>
public static class AgentEventTranslator
{
    /// <summary>尝试把帧还原为宿主事件。</summary>
    /// <param name="frame">SSE 帧。</param>
    /// <param name="evt">还原出的事件。</param>
    /// <returns>是否有对应的事件类型。</returns>
    public static bool TryTranslate(
        AgentSseFrame frame,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AgentStreamEvent? evt)
    {
        evt = null;
        using var document = ParseOrNull(frame.Data);
        if (document is null)
        {
            return false;
        }

        var root = document.RootElement;
        switch (frame.Name)
        {
            case AgentSseEventNames.ThinkDelta:
                evt = new ThinkingDeltaEvent(ReadString(root, "delta") ?? "")
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            case AgentSseEventNames.TextDelta:
                evt = new TextDeltaEvent(ReadString(root, "delta") ?? "")
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            case AgentSseEventNames.ToolStarted:
                evt = new ToolCallStartedEvent(
                    ReadString(root, "name") ?? "",
                    ReadString(root, "callId") ?? "",
                    ReadArguments(root, "arguments"))
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            case AgentSseEventNames.ToolCompleted:
                evt = new ToolCallCompletedEvent(
                    ReadString(root, "callId") ?? "",
                    ReadString(root, "resultSummary"))
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            case AgentSseEventNames.ToolFailed:
                evt = new ToolCallFailedEvent(
                    ReadString(root, "callId") ?? "",
                    ReadString(root, "error") ?? "")
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            case AgentSseEventNames.ToolConfirm:
                evt = new ToolConfirmationRequestedEvent(
                    ReadString(root, "confirmationId") ?? "",
                    ReadString(root, "toolName") ?? "",
                    ReadArguments(root, "arguments"),
                    ReadInt32(root, "timeoutSeconds"))
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            case AgentSseEventNames.AttachmentAdded:
                evt = new AttachmentAddedEvent(
                    ReadString(root, "fileName") ?? "",
                    ReadString(root, "mediaType") ?? "",
                    ReadInt64(root, "fileSize"),
                    ReadString(root, "source"))
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            case AgentSseEventNames.AttachmentSkipped:
                evt = new AttachmentSkippedEvent(
                    ReadString(root, "source") ?? "",
                    ReadString(root, "reason") ?? "")
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            case AgentSseEventNames.Error:
                evt = new ErrorEvent(ReadString(root, "message") ?? "")
                {
                    LlmRound = ReadInt32(root, "llmRound")
                };
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 读取 <c>plan.item</c> / <c>tool.denied</c> 的载荷（两者结构一致：
    /// <c>{ toolName, arguments }</c>）。
    /// </summary>
    /// <param name="frame">SSE 帧。</param>
    /// <param name="toolName">工具名。</param>
    /// <param name="arguments">工具参数。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryReadToolInvocation(
        AgentSseFrame frame,
        out string toolName,
        out IReadOnlyDictionary<string, object?> arguments)
    {
        toolName = "";
        arguments = new Dictionary<string, object?>();

        using var document = ParseOrNull(frame.Data);
        if (document is null)
        {
            return false;
        }

        toolName = ReadString(document.RootElement, "toolName") ?? "";
        arguments = ReadArguments(document.RootElement, "arguments");
        return toolName.Length > 0;
    }

    /// <summary>把 JSON 元素递归转换为 CLR 值（字典 / 数组 / 基本类型）。</summary>
    /// <param name="element">JSON 元素。</param>
    /// <returns>CLR 值。</returns>
    public static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ToClrValue(p.Value), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(ToClrValue).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        _ => null
    };

    /// <summary>读取参数字典；缺失或类型不符返回空字典。</summary>
    /// <param name="root">载荷根元素。</param>
    /// <param name="propertyName">属性名。</param>
    /// <returns>参数字典。</returns>
    public static IReadOnlyDictionary<string, object?> ReadArguments(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arguments) || arguments.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            result[property.Name] = ToClrValue(property.Value);
        }

        return result;
    }

    private static JsonDocument? ParseOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt32(JsonElement root, string propertyName)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var result)
            ? result
            : 0;

    private static long ReadInt64(JsonElement root, string propertyName)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt64(out var result)
            ? result
            : 0L;
}