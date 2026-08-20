/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： MarkdownLightRenderer
*版本号： V1.0.0.0
*唯一标识：轻量 Markdown 渲染器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：轻量 Markdown 渲染器，支持 headers/bold/italic/strikethrough/code/lists/tasklists/links，
*输出带色 TextSegment 列表，供 AssistantMessageBlock 自绘使用
*
*****************************************************************************/
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 轻量 Markdown 渲染器。将 Markdown 文本解析为带色 <see cref="TextSegment"/> 列表，
/// 支持 # headers、**bold**、*italic*、~~strikethrough~~、`inline code`、```code blocks```、
/// - 列表项、- [ ] 任务列表、[links](url)、自动 URL 链接、HTML 标签过滤。
/// 不依赖外部 Markdown 库。
/// </summary>
internal static partial class MarkdownLightRenderer
{
    /// <summary>代码块背景色 #2d2d2d。</summary>
    private static readonly Color CodeBlockBg = new(0x2D, 0x2D, 0x2D, 0xFF);

    /// <summary>行内代码前景色 #E06C75。</summary>
    private static readonly Color InlineCodeFg = new(0xE0, 0x6C, 0x75, 0xFF);

    /// <summary>代码块内文本前景色 #ABB2BF。</summary>
    private static readonly Color CodeBlockFg = new(0xAB, 0xB2, 0xBF, 0xFF);

    /// <summary>链接前景色 #61AFEF。</summary>
    private static readonly Color LinkFg = new(0x61, 0xAF, 0xEF, 0xFF);

    /// <summary>Header 前景色 #C678DD。</summary>
    private static readonly Color HeaderFg = new(0xC6, 0x78, 0xDD, 0xFF);

    /// <summary>列表标记前景色 #E5C07B。</summary>
    private static readonly Color ListBulletFg = new(0xE5, 0xC0, 0x7B, 0xFF);

    /// <summary>引用块前景色 #5C6370。</summary>
    private static readonly Color QuoteFg = new(0x5C, 0x63, 0x70, 0xFF);

    /// <summary>任务列表已完成前景色 #98C379。</summary>
    private static readonly Color TaskDoneFg = new(0x98, 0xC3, 0x79, 0xFF);

    /// <summary>任务列表未完成前景色 #E5C07B。</summary>
    private static readonly Color TaskTodoFg = new(0xE5, 0xC0, 0x7B, 0xFF);

    /// <summary>删除线颜色。</summary>
    private static readonly Color StrikethroughFg = new(0x5C, 0x63, 0x70, 0xFF);

    [GeneratedRegex(@"^ {0,3}(#{1,6})\s+(.*)$")]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^ {0,3}[-*+]\s+(\[([ xX])\]\s+)?(.*)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^ {0,3}>\s?(.*)$")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"```(\w*)")]
    private static partial Regex CodeBlockStartRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"(https?://[^\s<>\[\]""']+)")]
    private static partial Regex AutoUrlRegex();

    /// <summary>
    /// 预处理 Markdown 文本：移除/简化 HTML 标签。
    /// </summary>
    private static string PreprocessMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;

        // 处理常见的 HTML 标签和自定义组件标签
        // 自闭合标签如 <Badge type="tip" text="待修改" /> → 提取 text 属性或移除
        var result = HtmlTagRegex().Replace(markdown, match =>
        {
            var tag = match.Value;

            // 尝试提取常见的文本属性（如 text、label、title）
            var textMatch = Regex.Match(tag, @"(?:text|label|title|content)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (textMatch.Success)
            {
                return $"[{textMatch.Groups[1].Value}]";
            }

            // 换行标签
            if (tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("<br />", StringComparison.OrdinalIgnoreCase))
            {
                return "\n";
            }

            // 其他 HTML 标签直接移除
            return string.Empty;
        });

        return result;
    }

    /// <summary>
    /// 将 Markdown 文本解析为带样式的文本片段列表（未换行）。
    /// 调用方负责按宽度换行。
    /// </summary>
    /// <param name="markdown">Markdown 源文本。</param>
    /// <param name="defaultFg">默认前景色。</param>
    /// <returns>带样式的文本片段列表。</returns>
    public static List<TextSegment> ParseInline(string markdown, Color defaultFg)
    {
        var segments = new List<TextSegment>();
        if (string.IsNullOrEmpty(markdown))
        {
            return segments;
        }

        // 预处理：移除 HTML 标签
        markdown = PreprocessMarkdown(markdown);

        var sb = new StringBuilder();
        TextStyle currentStyle = TextStyle.None;
        Color currentFg = defaultFg;
        bool inCode = false;
        bool inBold = false;
        bool inItalic = false;
        bool inStrikethrough = false;
        int i = 0;

        void Flush()
        {
            if (sb.Length > 0)
            {
                segments.Add(new TextSegment(sb.ToString(), currentFg, Bg: null, Style: currentStyle));
                sb.Clear();
            }
        }

        while (i < markdown.Length)
        {
            char c = markdown[i];

            // 转义字符
            if (c == '\\' && i + 1 < markdown.Length && IsMarkdownSpecial(markdown[i + 1]))
            {
                Flush();
                segments.Add(new TextSegment(markdown[i + 1].ToString(), defaultFg));
                i += 2;
                continue;
            }

            // 行内代码 `code`
            if (c == '`' && !inBold && !inItalic && !inStrikethrough)
            {
                if (!inCode)
                {
                    Flush();
                    inCode = true;
                    currentFg = InlineCodeFg;
                    currentStyle = TextStyle.None;
                    i++;
                    continue;
                }
                else
                {
                    Flush();
                    inCode = false;
                    currentFg = defaultFg;
                    currentStyle = TextStyle.None;
                    i++;
                    continue;
                }
            }

            // 粗体 **text**
            if (c == '*' && i + 1 < markdown.Length && markdown[i + 1] == '*' && !inCode && !inStrikethrough)
            {
                // 检查是否是 ***bold+italic***
                if (inBold && i + 2 < markdown.Length && markdown[i + 2] == '*')
                {
                    // 暂时不处理这种复杂情况，按粗体处理
                }

                if (inBold)
                {
                    Flush();
                    inBold = false;
                    currentStyle = inItalic ? TextStyle.Italic : TextStyle.None;
                    if (inStrikethrough) currentStyle |= TextStyle.Strikethrough;
                    currentFg = inStrikethrough ? StrikethroughFg : defaultFg;
                }
                else
                {
                    Flush();
                    inBold = true;
                    currentStyle = TextStyle.Bold;
                    if (inItalic) currentStyle |= TextStyle.Italic;
                    if (inStrikethrough) currentStyle |= TextStyle.Strikethrough;
                }
                i += 2;
                continue;
            }

            // 斜体 *text*（单星号，且不在粗体内）
            if (c == '*' && !inBold && !inCode && !inStrikethrough)
            {
                // 避免与列表项的 * 混淆（行首的 * 后面跟空格是列表项）
                if (i == 0 || (i > 0 && markdown[i - 1] == ' ') || (i > 0 && markdown[i - 1] == '\n'))
                {
                    // 可能是列表项，继续检查后面是否有空格
                    if (i + 1 < markdown.Length && markdown[i + 1] == ' ')
                    {
                        sb.Append(c);
                        i++;
                        continue;
                    }
                }

                if (inItalic)
                {
                    Flush();
                    inItalic = false;
                    currentStyle = TextStyle.None;
                    if (inBold) currentStyle |= TextStyle.Bold;
                    if (inStrikethrough) currentStyle |= TextStyle.Strikethrough;
                    currentFg = inStrikethrough ? StrikethroughFg : defaultFg;
                }
                else
                {
                    Flush();
                    inItalic = true;
                    currentStyle = TextStyle.Italic;
                    if (inBold) currentStyle |= TextStyle.Bold;
                    if (inStrikethrough) currentStyle |= TextStyle.Strikethrough;
                }
                i++;
                continue;
            }

            // 删除线 ~~text~~
            if (c == '~' && i + 1 < markdown.Length && markdown[i + 1] == '~' && !inCode)
            {
                if (inStrikethrough)
                {
                    Flush();
                    inStrikethrough = false;
                    currentStyle = TextStyle.None;
                    if (inBold) currentStyle |= TextStyle.Bold;
                    if (inItalic) currentStyle |= TextStyle.Italic;
                    currentFg = defaultFg;
                }
                else
                {
                    Flush();
                    inStrikethrough = true;
                    currentStyle = TextStyle.Strikethrough;
                    if (inBold) currentStyle |= TextStyle.Bold;
                    if (inItalic) currentStyle |= TextStyle.Italic;
                    currentFg = StrikethroughFg;
                }
                i += 2;
                continue;
            }

            // 链接 [text](url)
            if (c == '[' && !inCode && !inBold && !inItalic && !inStrikethrough)
            {
                var closeBracket = markdown.IndexOf(']', i + 1);
                if (closeBracket > i + 1 && closeBracket + 1 < markdown.Length && markdown[closeBracket + 1] == '(')
                {
                    var closeParen = markdown.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 2)
                    {
                        Flush();
                        var linkText = markdown[(i + 1)..closeBracket];
                        // 递归解析链接文本中的样式
                        var linkSegments = ParseInline(linkText, LinkFg);
                        foreach (var seg in linkSegments)
                        {
                            segments.Add(new TextSegment(seg.Text, LinkFg, seg.Bg, seg.Style | TextStyle.Underline));
                        }
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            // 自动 URL 链接检测
            if (c == 'h' && i + 7 < markdown.Length && markdown[i..(i + 7)] == "http://" ||
                c == 'h' && i + 8 < markdown.Length && markdown[i..(i + 8)] == "https://")
            {
                var urlMatch = AutoUrlRegex().Match(markdown, i);
                if (urlMatch.Success && urlMatch.Index == i)
                {
                    Flush();
                    var url = urlMatch.Value;
                    segments.Add(new TextSegment(url, LinkFg, Bg: null, Style: TextStyle.Underline));
                    i += url.Length;
                    continue;
                }
            }

            sb.Append(c);
            i++;
        }

        Flush();
        return segments;
    }

    /// <summary>
    /// 将完整 Markdown 文本按行解析，返回每行的带样式片段列表。
    /// 自动处理 code block、header、list、quote 等块级元素。
    /// </summary>
    /// <param name="markdown">完整 Markdown 文本。</param>
    /// <param name="defaultFg">默认前景色。</param>
    /// <returns>每行对应的文本片段列表。</returns>
    public static List<List<TextSegment>> ParseLines(string markdown, Color defaultFg)
    {
        var result = new List<List<TextSegment>>();
        if (string.IsNullOrEmpty(markdown))
        {
            return result;
        }

        // 预处理
        markdown = PreprocessMarkdown(markdown);

        var lines = markdown.Split('\n');
        bool inCodeBlock = false;
        string codeBlockLang = string.Empty;
        var codeBlockLines = new List<string>();

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx].TrimEnd('\r');

            // Code block 开始
            if (!inCodeBlock && CodeBlockStartRegex().IsMatch(line))
            {
                inCodeBlock = true;
                var m = CodeBlockStartRegex().Match(line);
                codeBlockLang = m.Groups[1].Value;
                codeBlockLines.Clear();

                if (!string.IsNullOrEmpty(codeBlockLang))
                {
                    result.Add([new TextSegment($"  {codeBlockLang}", CodeBlockFg, CodeBlockBg, TextStyle.Italic)]);
                }
                continue;
            }

            // Code block 结束
            if (inCodeBlock && line.TrimStart().StartsWith("```"))
            {
                inCodeBlock = false;
                // 输出已收集的行
                foreach (var codeLine in codeBlockLines)
                {
                    result.Add([new TextSegment("  " + (string.IsNullOrEmpty(codeLine) ? " " : codeLine), CodeBlockFg, CodeBlockBg)]);
                }
                codeBlockLines.Clear();
                continue;
            }

            // Code block 内容
            if (inCodeBlock)
            {
                codeBlockLines.Add(line);
                continue;
            }

            // 表格分隔符行 |---|---| 跳过（简化处理，不做表格渲染）
            if (Regex.IsMatch(line.Trim(), @"^\|?[\s:|-]+\|?$"))
            {
                continue;
            }

            // 表格行 | cell1 | cell2 | → 简化为普通文本，用空格分隔
            if (line.TrimStart().StartsWith("|") && line.TrimEnd().EndsWith("|"))
            {
                var cells = line.Trim().Trim('|').Split('|');
                var simplified = "  " + string.Join(" | ", cells.Select(c => c.Trim()));
                result.Add(ParseInline(simplified, defaultFg));
                continue;
            }

            // Header: # text
            var headerMatch = HeaderRegex().Match(line);
            if (headerMatch.Success)
            {
                var level = headerMatch.Groups[1].Value.Length;
                var text = headerMatch.Groups[2].Value;
                var indent = new string(' ', Math.Max(0, level - 1));
                var prefix = indent + new string('#', level) + " ";
                var headerSegments = ParseInline(text, HeaderFg);
                var allSegments = new List<TextSegment> { new(prefix, HeaderFg, Bg: null, TextStyle.Bold) };
                allSegments.AddRange(headerSegments);
                result.Add(allSegments);
                continue;
            }

            // List: - text / * text / + text / - [ ] task / - [x] task
            var listMatch = ListRegex().Match(line);
            if (listMatch.Success)
            {
                var checkbox = listMatch.Groups[2].Value;
                var text = listMatch.Groups[3].Value;
                var bulletSegments = new List<TextSegment>();

                if (!string.IsNullOrEmpty(checkbox))
                {
                    // 任务列表
                    var isChecked = checkbox.ToLower() == "x";
                    var checkText = isChecked ? "  ✓ " : "  ○ ";
                    var checkFg = isChecked ? TaskDoneFg : TaskTodoFg;
                    var checkStyle = isChecked ? TextStyle.Strikethrough : TextStyle.None;
                    bulletSegments.Add(new TextSegment(checkText, checkFg, Bg: null, checkStyle));
                    var textSegments = ParseInline(text, isChecked ? StrikethroughFg : defaultFg);
                    if (isChecked)
                    {
                        // 已完成任务的文本添加删除线
                        foreach (var seg in textSegments)
                        {
                            bulletSegments.Add(new TextSegment(seg.Text, StrikethroughFg, seg.Bg, seg.Style | TextStyle.Strikethrough));
                        }
                    }
                    else
                    {
                        bulletSegments.AddRange(textSegments);
                    }
                }
                else
                {
                    // 普通列表
                    bulletSegments.Add(new TextSegment("  • ", ListBulletFg));
                    bulletSegments.AddRange(ParseInline(text, defaultFg));
                }
                result.Add(bulletSegments);
                continue;
            }

            // Quote: > text
            var quoteMatch = QuoteRegex().Match(line);
            if (quoteMatch.Success)
            {
                var text = quoteMatch.Groups[1].Value;
                var quoteSegments = new List<TextSegment> { new("  │ ", QuoteFg, Bg: null, TextStyle.Italic) };
                quoteSegments.AddRange(ParseInline(text, QuoteFg));
                result.Add(quoteSegments);
                continue;
            }

            // Horizontal rule: --- / *** / ___
            if (Regex.IsMatch(line.Trim(), @"^([-*_])\s*\1\s*\1[\s\1]*$"))
            {
                result.Add([new TextSegment("  ───────────────────────────────", QuoteFg)]);
                continue;
            }

            // 普通行：内联解析
            if (!string.IsNullOrEmpty(line))
            {
                result.Add(ParseInline(line, defaultFg));
            }
            else
            {
                result.Add([]);
            }
        }

        // 处理未关闭的 code block
        if (inCodeBlock)
        {
            foreach (var codeLine in codeBlockLines)
            {
                result.Add([new TextSegment("  " + (string.IsNullOrEmpty(codeLine) ? " " : codeLine), CodeBlockFg, CodeBlockBg)]);
            }
        }

        return result;
    }

    /// <summary>
    /// 将解析后的行列表按指定宽度换行，生成最终渲染行。
    /// </summary>
    /// <param name="parsedLines">ParseLines 的输出。</param>
    /// <param name="width">每行最大宽度。</param>
    /// <returns>换行后的 RenderLine 列表。</returns>
    public static List<RenderLine> WrapLines(List<List<TextSegment>> parsedLines, int width)
    {
        var result = new List<RenderLine>();
        if (width <= 0) return result;

        foreach (var lineSegments in parsedLines)
        {
            if (lineSegments.Count == 0)
            {
                result.Add(RenderLine.Blank);
                continue;
            }

            // 检查是否是 code block 行（有背景色）
            bool isCodeBlock = lineSegments.Any(s => s.Bg is not null);

            if (isCodeBlock)
            {
                // Code block 行不自动换行，按显示列宽截断
                var col = 0;
                var wrapped = new List<TextSegment>();
                foreach (var seg in lineSegments)
                {
                    if (col >= width) break;
                    var available = width - col;
                    var segColumns = TextMeasure.MeasureColumns(seg.Text);

                    if (segColumns <= available)
                    {
                        wrapped.Add(seg);
                        col += segColumns;
                    }
                    else
                    {
                        var truncated = TextMeasure.TruncateByColumns(seg.Text, available);
                        wrapped.Add(new TextSegment(truncated, seg.Fg, seg.Bg, seg.Style));
                        col = width;
                    }
                }
                result.Add(new RenderLine(wrapped));
            }
            else
            {
                // 普通行：按显示列宽换行
                var wrappedLines = TextMeasure.WrapByColumns(lineSegments, width);
                result.AddRange(wrappedLines);
            }
        }

        return result;
    }

    private static bool IsMarkdownSpecial(char c) =>
        c is '*' or '`' or '[' or ']' or '(' or ')' or '#' or '_' or '\\' or '-' or '~';
}
