using LuBan.AIAgent.Attachments;
using LuBan.AIAgent.Sessions;
using LubanAgentCore.Configuration;
using LubanAgentCore.Models;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LubanAgentCore.Services;

/// <summary>
/// 附件编排服务：路径解析、处理、模型能力判定、ChatMessage 构建、元数据序列化。
/// </summary>
public sealed class AttachmentService
{
    /// <summary>每消息最大附件数。</summary>
    public const int MaxAttachmentsPerMessage = 5;

    private static readonly Regex FileRefPattern =
        new("(?<![A-Za-z0-9_.@-])@(?:\"([^\"]+)\"|([^\\s@]+))", RegexOptions.Compiled);

    private static readonly char[] TrailingPunctuation =
        { ',', ';', ':', '，', '。', '；', '：', '、', ')', ']', '}', '！', '？', '!', '?' };

    /// <summary>裸路径起始边界：前面不能是路径分隔符或标识符字符，避免命中 <c>abc.png</c> 这类文件名片段。</summary>
    private const string BareStartBoundary = @"(?<![A-Za-z0-9_@\.\\/])";

    /// <summary>裸图片路径扫描（未加 <c>@</c> 前缀）；扩展名取自 <see cref="MediaTypeUtil.ImageExtensions"/>。</summary>
    private static readonly Regex BareImagePattern = BuildBareImagePattern();

    /// <summary>构造裸图片路径正则：引号包裹（可含空格）优先，其次盘符/UNC 开头的绝对路径。</summary>
    private static Regex BuildBareImagePattern()
    {
        // 长扩展名优先，避免 .tif 抢先匹配 .tiff
        var exts = "(?:" + string.Join("|", MediaTypeUtil.ImageExtensions
            .OrderByDescending(e => e.Length)
            .Select(Regex.Escape)) + ")";

        var pattern = BareStartBoundary
            + "(?:"
            + @"""(?<quoted>[^""\r\n]+?" + exts + @")"""
            + "|"
            + @"(?<bare>(?:[A-Za-z]:[\\/]|\\\\[^\\/\s]+[\\/])[^\r\n\t""<>|]*?" + exts + @")(?![A-Za-z0-9])"
            + ")";

        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    private readonly IAttachmentProcessor _processor;
    private readonly IServiceProvider _services;
    private readonly IWorkspaceManager? _workspace;

    /// <summary>构造。</summary>
    /// <param name="processor">单文件处理器。</param>
    /// <param name="services">服务提供者，用于按用户解析模型能力（keyed）。</param>
    /// <param name="workspace">工作区管理器，用于相对路径解析（可为 null）。</param>
    public AttachmentService(IAttachmentProcessor processor, IServiceProvider services, IWorkspaceManager? workspace = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _workspace = workspace;
    }

    /// <summary>
    /// 解析输入文本中的 @引用并**从文本中移除**，返回已归一化的绝对路径列表。
    /// 支持：@path、@"path with spaces"、@./relative（基于工作区根）、@/absolute。
    /// 仅返回存在且类型受支持的引用；其余引用原样保留在文本中。
    /// </summary>
    /// <param name="input">原始输入。</param>
    /// <param name="remainingText">去除有效引用后的正文。</param>
    public IReadOnlyList<string> ParseFileReferences(string input, out string remainingText)
    {
        var paths = new List<string>();
        var removals = new List<(int Index, int Length)>();

        foreach (Match m in FileRefPattern.Matches(input))
        {
            var raw = NormalizeToken(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            var resolved = ResolvePath(raw);

            // 无效引用：保留原文，交由 UI 提交时校验提示
            if (resolved == null || !File.Exists(resolved) || !_processor.IsSupported(resolved))
                continue;

            if (!paths.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                paths.Add(resolved);
            removals.Add((m.Index, m.Length));
        }

        // 未命中有效引用时正文必须原样返回（保留缩进、代码块与原有空白）：
        // 宿主每轮都会调用本方法，无条件压缩空白会静默改写用户正文。
        remainingText = RemoveRanges(input, removals);
        return paths;
    }

    /// <summary>去掉引用片段首尾空白及常见尾随标点（如 "@a.txt," 中的逗号）。</summary>
    private static string NormalizeToken(string raw) => raw.Trim().TrimEnd(TrailingPunctuation);

    /// <summary>把引用路径解析为绝对路径；相对路径基于当前工作区根目录。</summary>
    /// <param name="path">引用中的路径。</param>
    /// <returns>绝对路径；无法解析时返回 null。</returns>
    public string? ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var p = path.Trim();
            if (Path.IsPathRooted(p))
                return Path.GetFullPath(p);

            var root = _workspace?.CurrentWorkspace?.RootPath;
            return string.IsNullOrEmpty(root)
                ? Path.GetFullPath(p)
                : Path.GetFullPath(Path.Combine(root, p));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>返回文本中无法解析为受支持文件的 @引用片段（供提交前校验提示）。</summary>
    /// <param name="input">原始输入文本。</param>
    /// <param name="service">用于解析路径的服务实例。</param>
    public static IReadOnlyList<string> FindUnresolvedReferences(string input, AttachmentService service)
    {
        var invalid = new List<string>();
        foreach (Match m in FileRefPattern.Matches(input))
        {
            var raw = NormalizeToken(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            var resolved = service.ResolvePath(raw);
            if (resolved == null || !File.Exists(resolved) || !service._processor.IsSupported(resolved))
                invalid.Add(raw);
        }
        return invalid;
    }

    /// <summary>
    /// 扫描正文中的裸图片路径（未加 <c>@</c> 前缀），例如 <c>D:\图片\a.png 看看这个图</c>。
    /// 仅返回可解析、文件存在、且属于受支持图片类型的命中项；重复路径只保留首个。
    /// </summary>
    /// <param name="input">原始输入文本。</param>
    public IReadOnlyList<BareImageMention> FindBareImageMentions(string input)
    {
        if (string.IsNullOrEmpty(input))
            return Array.Empty<BareImageMention>();

        var result = new List<BareImageMention>();
        var codeSpans = FindCodeSpans(input);
        foreach (Match m in BareImagePattern.Matches(input))
        {
            // 代码里的示例路径不是用户要附加的图片（如 var p = @"D:\imgs\logo.png";），
            // 命中后还会被 StripBareImageMentions 从代码里删掉，因此扫描阶段就要排除。
            if (IntersectsAny(m.Index, m.Length, codeSpans) || LooksLikeCodeContext(input, m.Index))
                continue;

            var raw = NormalizeToken(m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["bare"].Value);
            var resolved = ResolvePath(raw);
            if (resolved == null || !File.Exists(resolved)) continue;
            if (!MediaTypeUtil.IsImage(resolved) || !_processor.IsSupported(resolved)) continue;
            if (result.Any(r => string.Equals(r.Path, resolved, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new BareImageMention(resolved, raw, m.Index, m.Length));
        }
        return result;
    }

    /// <summary>从正文中移除指定的裸图片命中片段，只归并被移除点紧邻的空白（其余文字原样保留）。</summary>
    /// <param name="input">原始输入文本。</param>
    /// <param name="mentions">已成功附加的命中项。</param>
    public static string StripBareImageMentions(string input, IReadOnlyList<BareImageMention> mentions)
    {
        if (string.IsNullOrEmpty(input) || mentions.Count == 0)
            return input;

        var removals = mentions
            .Where(m => m.Length > 0 && m.Index >= 0)
            .Select(m => (m.Index, m.Length))
            .ToList();
        return RemoveRanges(input, removals);
    }

    /// <summary>
    /// 按区间移除文本片段，只归并被移除点紧邻的空格/制表符：
    /// 两侧都还有正文时留一个空格，否则不留；其余位置的空白与缩进完全不动。
    /// </summary>
    private static string RemoveRanges(string input, IReadOnlyList<(int Index, int Length)> removals)
    {
        if (removals.Count == 0)
            return input;

        var builder = new StringBuilder(input.Length);
        var cursor = 0;

        foreach (var (index, length) in removals.OrderBy(r => r.Index))
        {
            var end = index + length;
            // 重叠或越界的区间直接跳过，避免误删相邻正文
            if (index < cursor || end > input.Length)
                continue;

            // 吸收片段两侧紧邻的空白，用于「删掉整个引用而不是留下两个空格」
            var before = index;
            while (before > cursor && input[before - 1] is ' ' or '\t') before--;
            var after = end;
            while (after < input.Length && input[after] is ' ' or '\t') after++;

            builder.Append(input, cursor, before - cursor);
            // 只在「两侧都还有正文且不在换行边界」时补一个空格，避免行首/行尾多出空白
            var lineBreakBefore = before > 0 && input[before - 1] is '\n' or '\r';
            var lineBreakAfter = after < input.Length && input[after] is '\n' or '\r';
            if (before > cursor && after < input.Length && !lineBreakBefore && !lineBreakAfter)
                builder.Append(' ');
            cursor = after;
        }

        builder.Append(input, cursor, input.Length - cursor);
        return builder.ToString();
    }

    /// <summary>标记围栏代码块（``` / ~~~）与行内反引号代码的区间。</summary>
    private static List<(int Start, int End)> FindCodeSpans(string text)
    {
        var spans = new List<(int Start, int End)>();
        var fenceStart = -1;
        var fenceMarker = '\0';
        var lineStart = 0;

        while (lineStart < text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;

            if (fenceStart < 0)
            {
                if (TryReadFence(text, lineStart, lineEnd, closingOnly: false, out var marker))
                {
                    fenceStart = lineStart;
                    fenceMarker = marker;
                }
                else
                {
                    AddInlineCodeSpans(text, lineStart, lineEnd, spans);
                }
            }
            else if (TryReadFence(text, lineStart, lineEnd, closingOnly: true, out var closing)
                     && closing == fenceMarker)
            {
                spans.Add((fenceStart, lineEnd));
                fenceStart = -1;
            }

            if (lineEnd >= text.Length) break;
            lineStart = lineEnd + 1;
        }

        // 未闭合的围栏：其后全部按代码处理，宁可少附加也不误删
        if (fenceStart >= 0)
            spans.Add((fenceStart, text.Length));

        return spans;
    }

    /// <summary>识别一行开头的围栏标记；闭合围栏要求标记后只有空白。</summary>
    private static bool TryReadFence(
        string text, int lineStart, int lineEnd, bool closingOnly, out char marker)
    {
        marker = '\0';
        var i = lineStart;
        while (i < lineEnd && (text[i] is ' ' or '\t')) i++;
        if (i >= lineEnd || (text[i] != '`' && text[i] != '~')) return false;

        var ch = text[i];
        var count = 0;
        while (i < lineEnd && text[i] == ch)
        {
            count++;
            i++;
        }
        if (count < 3) return false;

        if (closingOnly)
        {
            while (i < lineEnd && (text[i] is ' ' or '\t')) i++;
            if (i != lineEnd) return false;
        }

        marker = ch;
        return true;
    }

    /// <summary>把一行内的反引号代码片段记为受保护区。</summary>
    private static void AddInlineCodeSpans(
        string text, int lineStart, int lineEnd, List<(int Start, int End)> spans)
    {
        var i = lineStart;
        while (i < lineEnd)
        {
            if (text[i] != '`')
            {
                i++;
                continue;
            }

            var open = i;
            var ticks = 0;
            while (i < lineEnd && text[i] == '`')
            {
                ticks++;
                i++;
            }

            var close = FindBacktickRun(text, i, lineEnd, ticks);
            if (close < 0) break; // 反引号未成对：本行剩余部分按普通文本处理

            var closeEnd = close + ticks;
            spans.Add((open, closeEnd));
            i = closeEnd;
        }
    }

    /// <summary>从 <paramref name="start"/> 起查找长度恰为 <paramref name="ticks"/> 的反引号串。</summary>
    private static int FindBacktickRun(string text, int start, int lineEnd, int ticks)
    {
        var i = start;
        while (i < lineEnd)
        {
            if (text[i] != '`')
            {
                i++;
                continue;
            }

            var runStart = i;
            var count = 0;
            while (i < lineEnd && text[i] == '`')
            {
                count++;
                i++;
            }
            if (count == ticks) return runStart;
        }
        return -1;
    }

    /// <summary>片段是否落在受保护区（代码块/行内代码）内。</summary>
    private static bool IntersectsAny(int index, int length, List<(int Start, int End)> spans)
        => spans.Any(s => index < s.End && index + length > s.Start);

    /// <summary>
    /// 命中位置所在行的前缀是否像代码：<c>= D:\a.png</c>、<c>"D:\a.png"</c>、<c>@"..."</c> 等。
    /// 只收紧这一种情形，避免把「路径 + 问题」的正常用法排除掉。
    /// </summary>
    private static bool LooksLikeCodeContext(string text, int index)
    {
        var lineStart = index <= 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
        var prefix = text.AsSpan(lineStart, index - lineStart).TrimEnd();
        if (prefix.Length == 0) return false;

        var last = prefix[^1];
        if (last is '=' or '(' or '[' or '{' or ',')
            return true;

        if (last is '"' or '\'')
        {
            // @"D:\a.png" / $"..." / var p = "D:\a.png"
            if (prefix.Length >= 2 && (prefix[^2] is '@' or '$')) return true;
            return prefix.Contains('=') || prefix.Contains('(');
        }

        return false;
    }

    /// <summary>路径是否属于受支持的附件类型。</summary>
    public bool IsSupported(string filePath) => _processor.IsSupported(filePath);

    /// <summary>受支持的扩展名（小写，含点）。供文件选择器过滤使用。</summary>
    public IReadOnlyList<string> SupportedExtensions => _processor.SupportedExtensions;

    /// <summary>处理附件路径集合。</summary>
    public async Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(
        IReadOnlyList<string> filePaths, bool generateThumbnails = false, CancellationToken ct = default)
    {
        if (filePaths.Count > MaxAttachmentsPerMessage)
            throw new InvalidOperationException($"最多附加 {MaxAttachmentsPerMessage} 个文件");

        var result = new List<ProcessedAttachment>(filePaths.Count);
        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(await _processor.ProcessAsync(path, generateThumbnails, ct).ConfigureAwait(false));
        }
        return result;
    }

    /// <summary>模型是否可接受这些附件（仅有图片时校验 vision）。</summary>
    public bool CanAttach(string modelId, IReadOnlyList<ProcessedAttachment> attachments)
    {
        var hasImage = attachments.Any(a => a.Info.Kind == AttachmentKind.Image);
        return !hasImage || _services.GetModelCapabilityService().SupportsVision(modelId);
    }

    /// <summary>不支持时的提示。</summary>
    public string GetUnsupportedMessage(string modelId, IReadOnlyList<ProcessedAttachment> attachments)
        => $"当前模型 {modelId} 不支持图片输入，请切换到 vision 模型或移除图片附件";

    /// <summary>
    /// 构建含附件内容的用户消息。
    /// 直接委托 framework 的 <see cref="AttachmentMessageBuilder"/>，确保与 CLI/Codex 通路格式一致。
    /// </summary>
    public ChatMessage BuildUserMessage(string text, IReadOnlyList<ProcessedAttachment> attachments)
        => AttachmentMessageBuilder.Build(text, attachments);

    /// <summary>转 UI 展示模型。</summary>
    public static IReadOnlyList<AttachmentDisplayInfo> ToDisplayInfos(IReadOnlyList<ProcessedAttachment> attachments)
        => attachments.Select(a => new AttachmentDisplayInfo(
            a.Info.FileName, a.Info.MediaType, a.Info.FileSize,
            a.ThumbnailPath,
            a.Info.Kind == AttachmentKind.Image,
            a.PixelSize,
            a.Info.SourcePath)).ToList();

    /// <summary>历史附件 JSON（<see cref="AttachmentRecord"/> 数组）→ 显示信息。</summary>
    public static IReadOnlyList<AttachmentDisplayInfo> DisplayInfosFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<AttachmentDisplayInfo>();
        try
        {
            var records = JsonSerializer.Deserialize<List<AttachmentRecord>>(json);
            if (records is null)
                return Array.Empty<AttachmentDisplayInfo>();
            return records.Select(r => new AttachmentDisplayInfo(
                r.FileName, r.MediaType, r.FileSize, null,
                string.Equals(r.Kind, "Image", StringComparison.OrdinalIgnoreCase),
                null,
                string.IsNullOrEmpty(r.SourcePath) ? null : r.SourcePath)).ToList();
        }
        catch
        {
            return Array.Empty<AttachmentDisplayInfo>();
        }
    }
}