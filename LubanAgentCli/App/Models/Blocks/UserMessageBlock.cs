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
using LuBan.Common.IO;
using LubanAgentCore.Models;
using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除），此处显式指向 Terminal.Gui 类型
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 用户消息 Block。不可折叠，以 `>` 前缀 + 用户输入文本渲染。
/// </summary>
public sealed class UserMessageBlock : Block
{
    /// <summary>用户输入文本。</summary>
    public string Text { get; }

    /// <summary>附件展示信息。</summary>
    public IReadOnlyList<AttachmentDisplayInfo> Attachments { get; }

    /// <summary>不可折叠。</summary>
    public override bool IsFoldable => false;

    /// <summary>
    /// 初始化用户消息。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    public UserMessageBlock(string text) : this(text, Array.Empty<AttachmentDisplayInfo>()) { }

    /// <summary>
    /// 初始化用户消息（带附件）。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    /// <param name="attachments">附件展示信息。</param>
    public UserMessageBlock(string text, IReadOnlyList<AttachmentDisplayInfo> attachments)
    {
        Text = text ?? string.Empty;
        Attachments = attachments ?? Array.Empty<AttachmentDisplayInfo>();
        IsComplete = true;
    }

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        var contentWidth = Math.Max(1, width - 2); // "> " 前缀占 2 列
        var textLines = TextMeasure.WrapTextByLines(Text, contentWidth).Count;
        LineCount = Math.Max(1, textLines) + Attachments.Count;
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var contentWidth = Math.Max(1, width - 2);

        foreach (var a in Attachments)
        {
            var icon = a.IsImage ? "🖼" : "📄";
            var size = FileUtil.FormatFileSize(a.FileSize);
            var dim = a.IsImage && !string.IsNullOrEmpty(a.PixelSize) ? $" ({a.PixelSize})" : "";
            var label = $"  [{icon} {a.FileName}{dim} {size}]";
            lines.Add(RenderLine.Single(
                TextMeasure.TruncateByColumns(label, width),
                BlockColors.Accent));
        }

        foreach (var line in TextMeasure.WrapTextByLines(Text, contentWidth))
        {
            lines.Add(RenderLine.Single(
                $"> {line}",
                BlockColors.UserMessage,
                TextStyle.Bold));
        }
    }
}
