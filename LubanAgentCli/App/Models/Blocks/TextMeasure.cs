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
    /// <summary>
    /// 计算文本的显示列宽。
    /// </summary>
    public static int MeasureColumns(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var columns = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            columns += rune.GetColumns();
        }
        return columns;
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
            var runeWidth = rune.GetColumns();

            if (columns + runeWidth > maxWidth)
            {
                break;
            }

            columns += runeWidth;
            chars += rune.Utf16SequenceLength;
            runeIndex += rune.Utf16SequenceLength;
        }

        return chars;
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