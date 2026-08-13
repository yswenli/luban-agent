/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： ToolResultBlock
*版本号： V1.0.0.0
*唯一标识：工具结果 Block
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：工具执行结果，淡青色，默认折叠；展开显示完整结果文本
*
*****************************************************************************/
using System.Text;

using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 工具结果 Block。淡青色着色，默认折叠显示摘要，
/// 展开后显示完整结果文本（超过 20 行时截断，底部显示省略提示）。
/// </summary>
public sealed class ToolResultBlock : Block
{
    /// <summary>结果文本内容。</summary>
    public string Content { get; }

    /// <summary>是否为错误结果。</summary>
    public bool IsError { get; set; }

    /// <summary>关联的工具调用 ID。</summary>
    public string? CallId { get; set; }

    /// <summary>展开最大显示行数。</summary>
    public const int MaxExpandedLines = 20;

    /// <summary>
    /// 初始化工具结果 Block，默认折叠。
    /// </summary>
    /// <param name="content">结果文本。</param>
    /// <param name="callId">关联的调用 ID（可选）。</param>
    public ToolResultBlock(string content, string? callId = null)
    {
        Content = content ?? string.Empty;
        CallId = callId;
        IsCollapsed = true;
    }

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        if (IsCollapsed)
        {
            LineCount = 1;
            return;
        }

        // 与 Render 完全一致：标题行 + 内容行（每行前缀 " " 占 1 字符）
        if (string.IsNullOrEmpty(Content))
        {
            LineCount = 2; // 标题行 + "（无输出）"
            return;
        }

        var w = Math.Max(1, width - 1); // Render 中每行前缀 " "，实际可用 width-1
        var contentLines = (Content.Length + w - 1) / w;
        var truncated = contentLines > MaxExpandedLines;
        LineCount = 1 + Math.Min(contentLines, MaxExpandedLines) + (truncated ? 1 : 0);
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        if (IsCollapsed)
        {
            var preview = Content.Length <= 40 ? Content : Content[..37] + "...";
            var status = IsError ? "✗" : "✓";
            lines.Add(RenderLine.Single($"▸ {status} {preview}", IsError ? BlockColors.Failure : BlockColors.ToolResult));
        }
        else
        {
            var status = IsError ? "✗" : "✓";
            var color = IsError ? BlockColors.Failure : BlockColors.ToolResult;
            lines.Add(RenderLine.Single($"▾ 结果 ({status})", color, TextStyle.Bold));

            var text = Content;
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(RenderLine.Single("  （无输出）", color));
                return;
            }

            var w = Math.Max(1, width - 2);
            var remaining = text.AsSpan();
            var drawnLines = 0;

            while (remaining.Length > 0 && drawnLines < MaxExpandedLines)
            {
                var take = Math.Min(w, remaining.Length);
                lines.Add(RenderLine.Single($" {remaining[..take].ToString()}", color));
                remaining = remaining[take..];
                drawnLines++;
            }

            if (remaining.Length > 0)
            {
                lines.Add(RenderLine.Single($" … (还有 {remaining.Length} 字符)", BlockColors.System, TextStyle.Italic));
            }
        }
    }

    /// <inheritdoc/>
    public override HitActionResult? HitTest(int localLine)
    {
        if (localLine == 0)
        {
            return new HitActionResult(HitActionType.ToggleCollapse);
        }
        return null;
    }
}
