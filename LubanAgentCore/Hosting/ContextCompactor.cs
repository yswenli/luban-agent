/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： ContextCompactor
*描述：按 token 预算压缩会话上下文（摘要 + 归档），供起跑前守卫与超长兜底共用
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 把活跃会话消息摘要化并归档，腾出模型上下文预算。
/// </summary>
/// <remarks>
/// <para>
/// 与框架自动压缩（<c>SessionChatHistoryProvider</c>）口径一致：先写 <c>role=summary</c> 摘要、
/// 再把被摘要的消息标记为已压缩，反序执行会在写摘要失败时永久裁掉历史。
/// </para>
/// <para>
/// 区别在于触发条件：这里按 token 预算选保留窗口，而不是按消息条数阈值，
/// 以覆盖「条数不多但单条极长」的情况。
/// </para>
/// </remarks>
public sealed class ContextCompactor
{
    /// <summary>仅用于驱动摘要器的哨兵消息：摘要器保留末条不摘要，垫一条让全部真实消息都进摘要。</summary>
    private const string SummarizeSentinel = "（请总结以上对话，保留关键事实、结论与未完成事项）";

    /// <summary>预算未知时摘要输入的上限估算：按常见最小窗口（8k）的一半留出余量。</summary>
    private const int DefaultSummarizeCap = 4000;

    private readonly ISessionManager _sessions;
    private readonly IChatClient _chatClient;
    private readonly CompactionOptions _compaction;

    /// <summary>构造。</summary>
    /// <param name="sessions">会话管理器（会话作用域）。</param>
    /// <param name="chatClient">聊天客户端，用于生成摘要。</param>
    /// <param name="compaction">压缩策略，提供保留 token 数与摘要输入比例。</param>
    public ContextCompactor(
        ISessionManager sessions,
        IChatClient chatClient,
        CompactionOptions compaction)
    {
        _sessions = sessions;
        _chatClient = chatClient;
        _compaction = compaction;
    }

    /// <summary>
    /// 估算当前上下文 token：活跃消息正文 + 附件占位 + 本轮输入。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="incoming">本轮输入文本（可为 null）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>估算 token 数。</returns>
    public async Task<int> EstimateTokensAsync(string sessionId, string? incoming, CancellationToken ct = default)
    {
        var active = await _sessions.GetActiveMessagesAsync(sessionId).ConfigureAwait(false);
        return active.Sum(EstimateMessageTokens) + EstimateTokens(incoming);
    }

    /// <summary>
    /// 压缩会话上下文；没有可归档的消息时返回 <c>null</c>。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="budget">模型上下文触发阈值；未知时为 <c>null</c>，此时仅按保留 token 与摘要收敛。</param>
    /// <param name="incoming">本轮输入文本（计入压缩前/后的 token 估算）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>压缩结果；无消息可归档或摘要为空时返回 <c>null</c>。</returns>
    public async Task<ContextCompactionResult?> CompactAsync(
        string sessionId,
        int? budget,
        string? incoming,
        CancellationToken ct = default)
    {
        var active = (await _sessions.GetActiveMessagesAsync(sessionId).ConfigureAwait(false)).ToList();
        var summaries = active.Where(m => m.Role == "summary").OrderByDescending(m => m.Id).ToList();
        var messages = active.Where(m => m.Role != "summary").ToList();

        var before = active.Sum(EstimateMessageTokens) + EstimateTokens(incoming);

        // 保留窗口按 token 从尾部累加：条数与真实长度无关，长会话会被条数撑爆、短会话又留不够。
        // 上限再压到可用预算的一半，给系统提示、工具定义与模型回复留出空间；
        // 阈值未知（模型窗口未登记）时按「压缩前的一半」收敛，保证仍然实际减半。
        var keepTokens = _compaction.KeepTokens;
        keepTokens = Math.Min(keepTokens, Math.Max(1, (budget is > 0 ? budget.Value : before) / 2));
        keepTokens = Math.Max(1, keepTokens);

        var keepFrom = messages.Count;
        var keptTokens = 0;
        while (keepFrom > 0)
        {
            var tokens = EstimateMessageTokens(messages[keepFrom - 1]);
            var newest = keepFrom == messages.Count;
            var overLimit = keptTokens + tokens > keepTokens;

            // 多消息时至少保留最新一条：没有接续点，下一轮只能对着一堆摘要说「继续」。
            // 单条会话不存在接续问题，硬留等于不压缩，仍按上限判断。
            if (overLimit && !(newest && messages.Count > 1))
            {
                break;
            }

            keepFrom--;
            keptTokens += tokens;
        }

        var archived = messages.Take(keepFrom).ToList();
        var kept = messages.Skip(keepFrom).ToList();
        if (archived.Count == 0)
        {
            return null;
        }

        // 摘要调用本身也受同一个模型窗口约束：喂进全部归档消息，超出上限时丢最老的，
        // 并对单条超长正文截断。否则压缩必然失败，超长兜底就永远救不回来。
        var summarizeCap = budget is > 0
            ? Math.Max(200, (int)((long)budget.Value * Math.Clamp(_compaction.SummarizePercent, 10, 100) / 100))
            : Math.Min(Math.Max(200, before / 2), DefaultSummarizeCap);
        var history = new List<ChatMessage>();
        var summarizeTokens = 0;
        for (var i = archived.Count - 1; i >= 0; i--)
        {
            var message = archived[i];
            var text = TruncateForSummary(message.Content, Math.Max(200, summarizeCap / 2));
            var tokens = EstimateTokens(text);
            if (history.Count > 0 && summarizeTokens + tokens > summarizeCap)
            {
                break;
            }

            history.Insert(0, new ChatMessage(MapRole(message.Role), text));
            summarizeTokens += tokens;
        }

        history.Add(new ChatMessage(ChatRole.User, SummarizeSentinel));

#pragma warning disable MEAI001 // SummarizingChatReducer 属实验性 API，框架自动压缩同样使用
        var reducer = new SummarizingChatReducer(_chatClient, 1, 0);
        var reduced = (await reducer.ReduceAsync(history, ct).ConfigureAwait(false)).ToList();
#pragma warning restore MEAI001

        // 约定：targetCount=1 时输出恰为「摘要 + 末尾哨兵」两条，且摘要固定为 assistant 角色。
        // 条数或角色不符说明摘要器没有真正归约（例如历史里混入 system 消息时会被单独前置保留），
        // 此时首条不是摘要，直接放弃压缩，不能用一条正文冒充摘要写库。
        if (reduced.Count != 2 || reduced[0].Role != ChatRole.Assistant)
        {
            return null;
        }

        var summaryText = reduced[0].Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(summaryText))
        {
            return null;
        }

        // 先写摘要、后归档：反序执行时归档成功而写摘要失败，历史会被永久裁掉且无摘要兜底
        await _sessions.AddMessageAsync(sessionId, "summary", summaryText, EstimateTokens(summaryText)).ConfigureAwait(false);

        var compactedIds = archived.Select(m => m.Id)
            .Concat(summaries.Select(s => s.Id))
            .ToList();
        await _sessions.MarkMessagesCompactedAsync(sessionId, compactedIds).ConfigureAwait(false);

        var after = keptTokens + EstimateTokens(summaryText) + EstimateTokens(incoming);
        return new ContextCompactionResult(archived.Count, kept.Count, before, after, budget);
    }

    /// <summary>
    /// 估算文本 token：中日韩字符按 1 字 1 token，其余字符按 4 字符 1 token。
    /// </summary>
    /// <param name="text">文本。</param>
    /// <returns>估算 token 数。</returns>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var cjk = 0;
        var other = 0;
        foreach (var ch in text)
        {
            if (IsCjk(ch))
            {
                cjk++;
            }
            else
            {
                other++;
            }
        }

        return cjk + (other + 3) / 4;
    }

    /// <summary>估算单条消息的 token：正文 + 附件占位开销。</summary>
    /// <remarks>
    /// 只算真正进入模型上下文的部分：历史回放只带正文与附件，
    /// 思考、工具调用与分段存库供展示和排查，不回放、不占上下文。
    /// </remarks>
    /// <param name="message">会话消息。</param>
    /// <returns>估算 token 数。</returns>
    public static int EstimateMessageTokens(SessionMessage message)
    {
        if (message is null)
        {
            return 0;
        }

        var tokens = EstimateTokens(message.Content);
        if (!string.IsNullOrWhiteSpace(message.Attachments))
        {
            // 附件正文不在库里，只按一条占位文本估算，避免整体估成 0
            tokens += 32;
        }

        return tokens;
    }

    private static bool IsCjk(char ch)
        => ch is >= '\u3000' and <= '\u9fff'
            or >= '\uac00' and <= '\ud7af'
            or >= '\uff00' and <= '\uffef';

    /// <summary>
    /// 把单条正文截到摘要输入可承受的长度。
    /// </summary>
    /// <remarks>
    /// 按字符数而非 token 截断：CJK 约 1 字 1 token，是同等字符数下最坏情况，
    /// 因此 <paramref name="maxChars"/> 取 token 上限即为安全上界。
    /// </remarks>
    private static string TruncateForSummary(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text ?? string.Empty;
        }

        return text[..maxChars] + "…（正文过长，摘要输入已截断）";
    }

    private static ChatRole MapRole(string role) => role switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "system" => ChatRole.System,
        _ => ChatRole.Assistant
    };
}