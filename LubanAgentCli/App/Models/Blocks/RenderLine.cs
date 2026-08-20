/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： RenderLine
*版本号： V1.0.0.0
*唯一标识：渲染行与文本片段模型
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Block 自绘输出的行级模型——一行由一个或多个带色文本片段组成
*
*****************************************************************************/
using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除），此处显式指向 Terminal.Gui 类型
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 渲染行。一行由一个或多个带色文本片段组成，供 View 层逐段绘制。
/// </summary>
/// <param name="Segments">行内文本片段列表。</param>
public sealed record RenderLine(List<TextSegment> Segments)
{
    /// <summary>空行（无任何片段）。</summary>
    public static RenderLine Blank => new([]);

    /// <summary>单文本行（同一颜色/样式的一段文本）。</summary>
    /// <param name="text">文本内容。</param>
    /// <param name="fg">前景色。</param>
    /// <param name="style">文本样式。</param>
    /// <returns>包含一个 TextSegment 的渲染行。</returns>
    public static RenderLine Single(string text, Color fg, TextStyle style = TextStyle.None)
        => new([new TextSegment(text, fg, Bg: null, Style: style)]);

    /// <summary>单文本行，指定前景色和背景色。</summary>
    /// <param name="text">文本内容。</param>
    /// <param name="fg">前景色。</param>
    /// <param name="bg">背景色。</param>
    /// <param name="style">文本样式。</param>
    /// <returns>包含一个 TextSegment 的渲染行。</returns>
    public static RenderLine Single(string text, Color fg, Color bg, TextStyle style = TextStyle.None)
        => new([new TextSegment(text, fg, bg, style)]);
}

/// <summary>
/// 文本片段。渲染行的最小着色单元，包含一段连续文本及其前景色/背景色/样式。
/// </summary>
/// <param name="Text">文本内容。</param>
/// <param name="Fg">前景色（24-bit RGBA）。</param>
/// <param name="Bg">背景色（24-bit RGBA）。默认使用主题背景色。</param>
/// <param name="Style">文本样式（粗体/斜体/下划线等）。</param>
public sealed record TextSegment(string Text, Color Fg, Color? Bg = null, TextStyle Style = TextStyle.None);
