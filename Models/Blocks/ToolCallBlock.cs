/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： ToolCallBlock
*版本号： V1.0.0.0
*唯一标识：工具调用 Block
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Agent 工具调用，淡黄色，默认折叠；展开显示完整参数
*
*****************************************************************************/
using System.Text;
using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgent.Models.Blocks;

/// <summary>
/// 工具调用 Block。淡黄色着色，默认折叠显示工具名与参数摘要；
/// 展开后显示完整参数 JSON。
/// </summary>
public sealed class ToolCallBlock : Block
{
    /// <summary>工具名称。</summary>
    public string ToolName { get; }

    /// <summary>工具参数（JSON 格式）。</summary>
    public string? Arguments { get; set; }

    /// <summary>工具调用 ID（MCP 协议中的 callId）。</summary>
    public string? CallId { get; set; }

    /// <summary>
    /// 初始化工具调用 Block，默认折叠。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="arguments">工具参数（可选）。</param>
    /// <param name="callId">调用 ID（可选）。</param>
    public ToolCallBlock(string toolName, string? arguments = null, string? callId = null)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Arguments = arguments;
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

        var w = Math.Max(1, width - 2); // 缩进
        var args = Arguments ?? string.Empty;
        var totalChars = ToolName.Length + args.Length + 8; // 额外标记字符
        LineCount = Math.Max(2, 1 + (totalChars + w - 1) / w);
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var durationStr = Duration.HasValue ? $" · {Duration.Value.TotalSeconds:F1}s" : string.Empty;

        if (IsCollapsed)
        {
            var summary = $"▸ {ToolName}";
            if (!string.IsNullOrEmpty(Arguments))
            {
                var argPreview = Arguments.Length <= 30 ? Arguments : Arguments[..27] + "...";
                summary += $"({argPreview})";
            }
            summary += durationStr;
            lines.Add(RenderLine.Single(summary, BlockColors.ToolCall));
        }
        else
        {
            lines.Add(RenderLine.Single($"▾ {ToolName}{durationStr}", BlockColors.ToolCall, TextStyle.Bold));
            lines.Add(RenderLine.Single($"  调用 ID: {CallId ?? "—"}", BlockColors.ToolCall));

            if (!string.IsNullOrEmpty(Arguments))
            {
                var w = Math.Max(1, width - 3);
                var remaining = ("  参数: " + Arguments).AsSpan();
                while (remaining.Length > 0)
                {
                    var take = Math.Min(w, remaining.Length);
                    lines.Add(RenderLine.Single(remaining[..take].ToString(), BlockColors.ToolCall));
                    remaining = remaining[take..];
                }
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
