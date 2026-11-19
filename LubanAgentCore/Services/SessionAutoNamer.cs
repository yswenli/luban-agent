/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services
*文件名： SessionAutoNamer
*描述：会话自动命名：把一段对话内容概括成不超过 15 字的主题
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 会话自动命名：把用户首条消息概括成一句话主题。
/// </summary>
public interface ISessionAutoNamer
{
    /// <summary>
    /// 生成会话主题。
    /// </summary>
    /// <param name="sessionId">会话标识；用于取该会话当前选中的模型。</param>
    /// <param name="text">待概括的原文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>不超过 <see cref="SessionAutoNamer.MaxTitleChars"/> 字的主题；无模型或调用失败时返回 null。</returns>
    Task<string?> AutoSessionNameAsync(string? sessionId, string text, CancellationToken ct = default);
}

/// <summary>
/// 会话自动命名实现：一次性非流式调用，不装配工具、关闭思考，与提示词改写（<c>/agent/optimize</c>）同口径。
/// </summary>
public sealed class SessionAutoNamer(IAgentModelSelection selection, IProviderRouter router) : ISessionAutoNamer
{
    /// <summary>主题字数上限（按 Unicode 字符计，中文一字算一，英文一个字母算一）。</summary>
    public const int MaxTitleChars = 15;

    /// <summary>输入截断长度：与提示词改写的输入上限一致，超出直接截断而不是拒绝。</summary>
    private const int MaxInputChars = 4000;

    /// <summary>整体超时：命名只影响标题，卡住就放弃，不占着后台线程无限等。</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>可剥离的输出前缀（模型偶尔会加“标题：”这类修饰）。</summary>
    private static readonly string[] OutputPrefixes = ["会话标题", "会话名称", "标题", "主题", "名称"];

    /// <summary>需要从首尾剥离的引号与括号。</summary>
    private const string WrapChars = "\"'“”‘’「」『』《》〈〉()（）[]【】";

    /// <summary>需要从末尾剥离的标点。</summary>
    private const string TrailingPunctuation = "。．.！!？?，,、；;：:…~～";

    /// <summary>
    /// 命名指令：只输出主题，不解释、不加装饰。
    /// </summary>
    /// <remarks>
    /// 固定写在服务端而不是由调用方传入：标题形态直接体现产品行为，放开给终端等于让三端口径各自漂移。
    /// </remarks>
    public const string Instruction = """
        你是会话命名助手。用户会发来一段对话内容，你的唯一任务是为这段内容起一个简短的主题名称。

        必须遵守：
        1. 只输出主题名称本身：不要加引号、书名号、序号、前缀（例如“标题：”）或任何解释。
        2. 不超过 15 个字：中文按字计，英文按字母计。
        3. 抓住这段内容的主要意图或对象，不要照抄整句，不要臆造用户没有提到的具体信息。
        4. 不要以句号、问号、感叹号等标点结尾。
        5. 使用与用户输入相同的语言。
        """;

    /// <inheritdoc />
    public async Task<string?> AutoSessionNameAsync(string? sessionId, string text, CancellationToken ct = default)
    {
        var prompt = NormalizeInput(text);
        if (prompt.Length == 0) return null;

        string model;
        try
        {
            model = await selection.ResolveQualifiedModelNameAsync(sessionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"会话自动命名：解析模型失败。sessionId={sessionId}, error={ex.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(model)) return null;

        IChatClient client;
        try
        {
            client = router.CreateChatClient(model);
        }
        catch (Exception ex)
        {
            // 供应商被删/改名属于配置问题，不是服务故障：静默放弃，保留默认标题
            Logger.Warn($"会话自动命名：模型不可用。model={model}, error={ex.Message}");
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            var response = await client
                .GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, Instruction),
                        new ChatMessage(ChatRole.User, prompt),
                    ],
                    new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } },
                    timeout.Token)
                .ConfigureAwait(false);

            return Sanitize(response?.Text);
        }
        catch (Exception ex)
        {
            Logger.Warn($"会话自动命名：调用失败。sessionId={sessionId}, model={model}, error={ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 压缩输入：去首尾空白、把连续空白（含换行）压成单空格，超长截断。
    /// </summary>
    public static string NormalizeInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var compact = Regex.Replace(text.Trim(), @"\s+", " ");
        return compact.Length <= MaxInputChars ? compact : compact[..MaxInputChars];
    }

    /// <summary>
    /// 清洗模型输出：去装饰前缀、包裹引号与末尾标点，压空白后截到 15 字；空结果返回 null。
    /// </summary>
    public static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = Regex.Replace(raw.Trim(), @"\s+", " ");

        // 前缀可能叠加（“标题：「…””），循环剥离；只在存在分隔符时剥，避免吃掉正文开头
        for (var guard = 0; guard < 3; guard++)
        {
            var stripped = false;
            foreach (var prefix in OutputPrefixes)
            {
                if (!text.StartsWith(prefix, StringComparison.Ordinal)) continue;

                var rest = text[prefix.Length..].TrimStart();
                if (rest.Length == 0) return null;
                if (rest[0] is not ('：' or ':' or ' ' or '·' or '-')) continue;

                text = rest.TrimStart('：', ':', ' ', '·', '-', '—').Trim();
                stripped = true;
                break;
            }

            if (!stripped) break;
        }

        text = StripWrapChars(text);

        var end = text.Length;
        while (end > 0 && (TrailingPunctuation.Contains(text[end - 1]) || char.IsWhiteSpace(text[end - 1])))
        {
            end--;
        }

        text = text[..end].Trim();
        text = StripWrapChars(text);
        if (text.Length == 0) return null;

        return Truncate(text, MaxTitleChars);
    }

    /// <summary>从首尾交替剥离引号/括号，直到两端都不再是装饰字符。</summary>
    private static string StripWrapChars(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && (WrapChars.Contains(text[start]) || char.IsWhiteSpace(text[start])))
        {
            start++;
        }

        while (end > start && (WrapChars.Contains(text[end - 1]) || char.IsWhiteSpace(text[end - 1])))
        {
            end--;
        }

        return text[start..end];
    }

    /// <summary>按 Unicode 字符截断，避免把代理对（emoji 等）切成半个字符。</summary>
    private static string Truncate(string text, int maxChars)
    {
        var builder = new StringBuilder(Math.Min(text.Length, maxChars * 2));
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count++ >= maxChars) break;
            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}