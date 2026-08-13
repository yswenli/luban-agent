/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： ThinkingBlock
*版本号： V1.0.0.0
*唯一标识：思考过程 Block
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Agent 思考过程（CoT），紫色，默认折叠
*
*****************************************************************************/
using System.Text;

using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 思考过程 Block。紫色着色，默认折叠，展开时显示完整 CoT 内容。
/// </summary>
public sealed class ThinkingBlock : Block
{
    private readonly StringBuilder _content = new();

    /// <summary>
    /// 思考文本内容（流式追加后为累积结果）。
    /// </summary>
    public string Content => _content.ToString();

    /// <summary>
    /// 初始化思考过程 Block，默认折叠。
    /// </summary>
    public ThinkingBlock()
    {
        IsCollapsed = true;
    }

    /// <summary>
    /// 追加流式 token。
    /// </summary>
    /// <param name="token">思考 token。</param>
    public void AppendContent(string token)
    {
        _content.Append(token);
    }

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        if (IsCollapsed)
        {
            LineCount = 1;
        }
        else
        {
            // 与 Render 保持一致：标题行 + 内容行（空内容时为"（无内容）"提示行）
            var text = _content.ToString();
            if (string.IsNullOrEmpty(text))
            {
                LineCount = 2;
                return;
            }

            var w = Math.Max(1, width - 1); // 缩进 1 列
            LineCount = 1 + Math.Max(1, (text.Length + w - 1) / w);
        }
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        if (IsCollapsed)
        {
            var summary = Content.Length > 0
                ? $"▸ 思考中 · {Content.Length} 字符"
                : "▸ 思考中…";
            lines.Add(RenderLine.Single(summary, BlockColors.Thinking, TextStyle.Italic));
        }
        else
        {
            lines.Add(RenderLine.Single("▾ 思考过程", BlockColors.Thinking, TextStyle.Bold));
            var text = Content;
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(RenderLine.Single("  （无内容）", BlockColors.Thinking, TextStyle.Italic));
                return;
            }

            var w = Math.Max(1, width - 1);
            var remaining = text.AsSpan();
            while (remaining.Length > 0)
            {
                var take = Math.Min(w, remaining.Length);
                lines.Add(RenderLine.Single($" {remaining[..take].ToString()}", BlockColors.Thinking));
                remaining = remaining[take..];
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
