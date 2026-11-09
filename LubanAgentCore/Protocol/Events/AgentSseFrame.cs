/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Events
*文件名： AgentSseFrame
*描述：SSE 帧的文本编解码（event + data + 空行结束）
*
*****************************************************************************/

using LuBan.AIAgent.Protocol;

namespace LubanAgentCore.Protocol.Events;

/// <summary>
/// 一个 SSE 帧：事件名 + 单行 JSON 载荷。
/// </summary>
/// <remarks>
/// 线格式固定为 <c>event: {name}\ndata: {json}\n\n</c>；心跳为注释行 <c>: ping\n\n</c>，
/// 客户端读到注释行直接丢弃，用于保活并探测连接是否仍然可写。
/// </remarks>
/// <param name="Name">事件名，见 <see cref="AgentSseEventNames"/>。</param>
/// <param name="Data">载荷 JSON 文本。</param>
public sealed record AgentSseFrame(string Name, string Data)
{
    /// <summary>心跳注释行（不含结尾空行）。</summary>
    public const string HeartbeatComment = ": ping";

    /// <summary>心跳完整文本（含结尾空行）。</summary>
    public const string HeartbeatText = HeartbeatComment + "\n\n";

    /// <summary>用载荷对象构造帧；载荷按框架口径序列化（camelCase、不转义中文）。</summary>
    /// <param name="name">事件名。</param>
    /// <param name="payload">载荷对象。</param>
    /// <returns>帧。</returns>
    public static AgentSseFrame Create(string name, object payload)
        => new(name, AgentEventProtocol.SerializePayload(payload));

    /// <summary>会话首帧。</summary>
    /// <param name="sessionId">实际使用的会话标识。</param>
    /// <param name="created">是否为本轮新建。</param>
    /// <param name="runId">本轮标识；客户端取消时回传，避免迟到取消命中新回合。</param>
    /// <returns>帧。</returns>
    public static AgentSseFrame ForSession(string sessionId, bool created, string? runId = null)
        => Create(AgentSseEventNames.Session, new { sessionId, created, runId });

    /// <summary>结束帧。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>帧。</returns>
    public static AgentSseFrame ForDone(string sessionId)
        => Create(AgentSseEventNames.Done, new { sessionId });

    /// <summary>错误帧。</summary>
    /// <param name="message">错误文案。</param>
    /// <returns>帧。</returns>
    public static AgentSseFrame ForError(string message)
        => Create(AgentSseEventNames.Error, new { message });

    /// <summary>转成可写入响应流的文本（含结尾空行）。</summary>
    /// <returns>SSE 文本。</returns>
    public string ToText() => $"event: {Name}\ndata: {Data}\n\n";

    /// <summary>把 <see cref="Data"/> 反序列化为强类型载荷。</summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <returns>载荷；解析失败返回 default。</returns>
    public T? GetPayload<T>()
    {
        try
        {
            return AgentApiJson.Deserialize<T>(Data);
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }

    /// <summary>判断一行是否为心跳注释。</summary>
    /// <param name="line">行文本（不含换行）。</param>
    /// <returns>是否心跳。</returns>
    public static bool IsHeartbeat(string? line)
        => string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(':');

    /// <summary>
    /// 解析一个事件块（已按空行切分好的若干行）。
    /// </summary>
    /// <param name="block">事件块文本（可含换行）。</param>
    /// <param name="frame">解析结果。</param>
    /// <returns>是否解析出有效帧（纯心跳块返回 false）。</returns>
    public static bool TryParse(string? block, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AgentSseFrame? frame)
    {
        frame = null;
        if (string.IsNullOrWhiteSpace(block))
        {
            return false;
        }

        string? name = null;
        var dataLines = new List<string>();
        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (IsHeartbeat(line))
            {
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLines.Add(line[5..].TrimStart());
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        frame = new AgentSseFrame(name, string.Join("\n", dataLines));
        return true;
    }
}