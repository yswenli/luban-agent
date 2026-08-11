/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： UserMessageBlock
*版本号： V1.0.0.0
*唯一标识：用户消息 Block
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：用户输入消息，不可折叠，单行或多行文本 + 淡蓝白粗体 `>` 前缀
*
*****************************************************************************/
using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除），此处显式指向 Terminal.Gui 类型
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgent.Models.Blocks;

/// <summary>
/// 用户消息 Block。不可折叠，以 `>` 前缀 + 用户输入文本渲染。
/// </summary>
public sealed class UserMessageBlock : Block
{
    /// <summary>用户输入文本。</summary>
    public string Text { get; }

    /// <summary>不可折叠。</summary>
    public override bool IsFoldable => false;

    /// <summary>
    /// 初始化用户消息。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    public UserMessageBlock(string text)
    {
        Text = text ?? string.Empty;
        IsComplete = true;
    }

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        // 简单估算：每行最多 width 字符（含 "> " 前缀）
        var contentWidth = Math.Max(1, width - 2);
        LineCount = Math.Max(1, (Text.Length + contentWidth - 1) / contentWidth);
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var contentWidth = Math.Max(1, width - 2);
        var offset = 0;

        while (offset < Text.Length)
        {
            var seg = Text.AsSpan(offset, Math.Min(contentWidth, Text.Length - offset));
            var line = RenderLine.Single(
                $"> {seg.ToString()}",
                BlockColors.UserMessage,
                TextStyle.Bold);
            lines.Add(line);

            offset += contentWidth;
        }

        if (LineCount == 0)
        {
            lines.Add(RenderLine.Single("> ", BlockColors.UserMessage, TextStyle.Bold));
        }
    }
}
