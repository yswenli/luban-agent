/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： TextMeasure
*版本号： V1.0.0.0
*唯一标识：按显示列宽度量/切分工具
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/17
*描述：按终端显示列宽处理文本（CJK 字符占 2 列），供 Block 布局使用
*
*****************************************************************************/
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 按终端显示列宽度量/切分文本。CJK 字符占 2 列，ASCII 占 1 列。
/// </summary>
internal static class TextMeasure
{
    /// <summary>制表符展开宽度（列）。</summary>
    private const int TabWidth = 4;

    /// <summary>
    /// 计算文本的显示列宽。控制字符（宽度为负，如 \n、\r、\t）按 0 列计，避免记账出现负值。
    /// </summary>
    public static int MeasureColumns(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var columns = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            columns += GetCellWidth(rune);
        }
        return columns;
    }

    /// <summary>
    /// 统一换行符后按逻辑行拆分文本。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <returns>逻辑行数组；空文本返回单个空行。</returns>
    public static string[] SplitLines(string? text)
        => string.IsNullOrEmpty(text)
            ? [string.Empty]
            : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// 先按逻辑行（\n）拆分，再对每行按显示列宽折行，返回最终显示行文本集合。
    /// 制表符展开为空格以保留缩进；控制字符按 0 列计。
    /// 供 Block 的 Layout 与 Render 共用，保证账面行数与实际渲染行数一致。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <param name="maxWidth">单行最大显示列宽。</param>
    /// <returns>折行后的显示行文本列表。</returns>
    public static List<string> WrapTextByLines(string? text, int maxWidth)
    {
        var result = new List<string>();
        if (maxWidth <= 0) maxWidth = 1;

        foreach (var rawLine in SplitLines(text))
        {
            var line = rawLine.Replace("\t", new string(' ', TabWidth));
            if (line.Length == 0)
            {
                result.Add(string.Empty);
                continue;
            }

            var offset = 0;
            while (offset < line.Length)
            {
                var take = TakeByColumns(line, offset, maxWidth);
                result.Add(line.Substring(offset, take));
                offset += take;
            }
        }

        return result;
    }

    /// <summary>
    /// 按显示列宽切分单行 segments，保留样式映射，尽量在空格/词边界换行。
    /// </summary>
    public static List<RenderLine> WrapByColumns(List<TextSegment> segments, int maxWidth)
    {
        var result = new List<RenderLine>();
        if (maxWidth <= 0 || segments.Count == 0)
        {
            return result;
        }

        var totalText = new StringBuilder();
        var styleMap = new List<(int Start, int End, TextSegment Segment)>();
        var pos = 0;

        foreach (var seg in segments)
        {
            styleMap.Add((pos, pos + seg.Text.Length, seg));
            totalText.Append(seg.Text);
            pos += seg.Text.Length;
        }

        var fullText = totalText.ToString();
        if (fullText.Length == 0)
        {
            result.Add(RenderLine.Blank);
            return result;
        }

        var offset = 0;
        while (offset < fullText.Length)
        {
            var take = TakeByColumns(fullText, offset, maxWidth);

            if (offset + take < fullText.Length)
            {
                var wrapPos = fullText.LastIndexOf(' ', offset + take - 1, take);
                if (wrapPos > offset)
                {
                    take = wrapPos - offset + 1;
                }
            }

            var lineSegments = SliceByColumns(styleMap, offset, take);
            result.Add(new RenderLine(lineSegments));
            offset += take;
        }

        return result;
    }

    /// <summary>
    /// 按列宽截断（兜底），CJK 不切半。
    /// </summary>
    public static string TruncateByColumns(string text, int maxWidth)
    {
        if (maxWidth <= 0) return string.Empty;
        if (string.IsNullOrEmpty(text)) return text;

        var take = TakeByColumns(text, 0, maxWidth);
        return text[..take];
    }

    private static int TakeByColumns(string text, int offset, int maxWidth)
    {
        var columns = 0;
        var chars = 0;
        var runeIndex = offset;

        while (runeIndex < text.Length)
        {
            var rune = Rune.GetRuneAt(text, runeIndex);
            var runeWidth = GetCellWidth(rune);

            if (columns + runeWidth > maxWidth)
            {
                break;
            }

            columns += runeWidth;
            chars += rune.Utf16SequenceLength;
            runeIndex += rune.Utf16SequenceLength;
        }

        if (chars == 0 && runeIndex < text.Length)
        {
            // 宽度不足以容纳单个字符（如 maxWidth=1 遇 CJK）时仍前进一个字符，避免调用方死循环
            chars = Rune.GetRuneAt(text, runeIndex).Utf16SequenceLength;
        }

        return chars;
    }

    /// <summary>
    /// 单字符显示列宽；控制字符（负值）统一按 0 列计。
    /// </summary>
    private static int GetCellWidth(Rune rune)
    {
        var width = rune.GetColumns();
        return width < 0 ? 0 : width;
    }

    private static List<TextSegment> SliceByColumns(
        List<(int Start, int End, TextSegment Segment)> styleMap,
        int offset,
        int take)
    {
        var result = new List<TextSegment>();

        foreach (var (start, end, seg) in styleMap)
        {
            var segStart = Math.Max(start, offset);
            var segEnd = Math.Min(end, offset + take);

            if (segStart < segEnd)
            {
                var slice = seg.Text[(segStart - start)..(segEnd - start)];
                result.Add(new TextSegment(slice, seg.Fg, seg.Bg, seg.Style));
            }
        }

        return result;
    }
}