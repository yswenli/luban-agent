/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Utils
*文件名： MarkdownToInlinesConverter
*版本号： V1.0.0.0
*唯一标识：Markdown 转 Inline 转换器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：将简单的 Markdown 语法转换为 Avalonia Inline 元素
*
*****************************************************************************/
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Text.RegularExpressions;

namespace LubanAgentCodex.Utils;

/// <summary>
/// 轻量级 Markdown 渲染器，支持基本语法
/// </summary>
public static class MarkdownRenderer
{
    /// <summary>
    /// 将 Markdown 文本解析为 Inline 集合
    /// </summary>
    public static InlineCollection Parse(string markdown, InlineCollection inlines)
    {
        inlines.Clear();
        
        if (string.IsNullOrEmpty(markdown))
            return inlines;

        var lines = markdown.Split('\n');
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            // 处理标题
            if (line.StartsWith("# "))
            {
                AddRun(inlines, line.Substring(2), fontSize: 20, fontWeight: FontWeight.Bold);
            }
            else if (line.StartsWith("## "))
            {
                AddRun(inlines, line.Substring(3), fontSize: 18, fontWeight: FontWeight.Bold);
            }
            else if (line.StartsWith("### "))
            {
                AddRun(inlines, line.Substring(4), fontSize: 16, fontWeight: FontWeight.Bold);
            }
            // 处理无序列表
            else if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                var indent = line.Length - line.TrimStart().Length;
                var bullet = new string(' ', indent) + "• ";
                AddRun(inlines, bullet);
                ParseInline(inlines, line.TrimStart().Substring(2));
            }
            // 处理有序列表
            else if (Regex.IsMatch(line.TrimStart(), @"^\d+\. "))
            {
                var match = Regex.Match(line.TrimStart(), @"^(\d+\. )");
                AddRun(inlines, match.Value);
                ParseInline(inlines, line.TrimStart().Substring(match.Length));
            }
            // 处理代码块
            else if (line.TrimStart().StartsWith("```"))
            {
                // 代码块标记，跳过语言标识
            }
            // 处理引用
            else if (line.StartsWith("> "))
            {
                AddRun(inlines, "│ ", foreground: Brushes.Gray);
                ParseInline(inlines, line.Substring(2), foreground: Brushes.Gray);
            }
            // 普通文本
            else
            {
                ParseInline(inlines, line);
            }
            
            // 添加换行（除了最后一行）
            if (i < lines.Length - 1)
            {
                inlines.Add(new LineBreak());
            }
        }

        return inlines;
    }

    /// <summary>
    /// 解析行内 Markdown 语法
    /// </summary>
    private static void ParseInline(InlineCollection inlines, string text, IBrush? foreground = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            inlines.Add(new Run(""));
            return;
        }

        // 匹配行内元素：**粗体**、*斜体*、`代码`、[链接](url)
        var pattern = @"(\*\*(.+?)\*\*)|(\*(.+?)\*)|(`(.+?)`)|(\[(.+?)\]\((.+?)\))";
        var lastIndex = 0;

        foreach (Match match in Regex.Matches(text, pattern))
        {
            // 添加匹配前的文本
            if (match.Index > lastIndex)
            {
                var plainText = text.Substring(lastIndex, match.Index - lastIndex);
                inlines.Add(new Run(plainText) { Foreground = foreground });
            }

            if (match.Groups[1].Success) // **粗体**
            {
                inlines.Add(new Run(match.Groups[2].Value)
                {
                    FontWeight = FontWeight.Bold,
                    Foreground = foreground
                });
            }
            else if (match.Groups[3].Success) // *斜体*
            {
                inlines.Add(new Run(match.Groups[4].Value)
                {
                    FontStyle = FontStyle.Italic,
                    Foreground = foreground
                });
            }
            else if (match.Groups[5].Success) // `代码`
            {
                inlines.Add(new Run(match.Groups[6].Value)
                {
                    FontFamily = new FontFamily("Consolas, Menlo, Monaco, monospace"),
                    Background = new SolidColorBrush(Color.Parse("#2D2D30")),
                    Foreground = new SolidColorBrush(Color.Parse("#E06C75"))
                });
            }
            else if (match.Groups[7].Success) // [链接](url)
            {
                inlines.Add(new Run(match.Groups[8].Value)
                {
                    Foreground = new SolidColorBrush(Color.Parse("#61AFEF")),
                    TextDecorations = TextDecorations.Underline
                });
            }

            lastIndex = match.Index + match.Length;
        }

        // 添加剩余文本
        if (lastIndex < text.Length)
        {
            var remaining = text.Substring(lastIndex);
            inlines.Add(new Run(remaining) { Foreground = foreground });
        }
    }

    /// <summary>
    /// 添加格式化的 Run 元素
    /// </summary>
    private static void AddRun(InlineCollection inlines, string text, 
        double? fontSize = null, FontWeight? fontWeight = null, 
        IBrush? foreground = null)
    {
        var run = new Run(text);
        
        if (fontSize.HasValue)
            run.FontSize = fontSize.Value;
        if (fontWeight.HasValue)
            run.FontWeight = fontWeight.Value;
        if (foreground != null)
            run.Foreground = foreground;
            
        inlines.Add(run);
    }
}
