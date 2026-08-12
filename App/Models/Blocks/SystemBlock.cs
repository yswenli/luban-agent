/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： SystemBlock
*版本号： V1.0.0.0
*唯一标识：系统消息 Block
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：系统消息与 `/` 命令输出，灰色，不可折叠；支持自定义前景色
*
*****************************************************************************/
using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 系统消息 Block。默认灰色着色，不可折叠。用于显示 `/` 命令输出、启动提示、
/// 工作区信息、权限模式切换通知等系统级消息。前景色可通过构造函数自定义。
/// </summary>
public sealed class SystemBlock : Block
{
    /// <summary>系统消息文本。</summary>
    public string Text { get; }

    /// <summary>不可折叠。</summary>
    public override bool IsFoldable => false;

    /// <summary>是否加粗渲染。</summary>
    public bool IsBold { get; }

    /// <summary>前景色（默认 <see cref="BlockColors.System"/> 灰色）。</summary>
    public Color Foreground { get; }

    /// <summary>
    /// 初始化系统消息。
    /// </summary>
    /// <param name="text">消息文本。</param>
    /// <param name="isBold">是否加粗。</param>
    /// <param name="foreground">前景色，默认灰色。启动横幅可设为 <see cref="BlockColors.Accent"/>。</param>
    public SystemBlock(string text, bool isBold = false, Color? foreground = null)
    {
        Text = text ?? string.Empty;
        IsBold = isBold;
        Foreground = foreground ?? BlockColors.System;
        IsComplete = true;
    }

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        LineCount = Math.Max(1, (Text.Length + Math.Max(1, width) - 1) / Math.Max(1, width));
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var style = IsBold ? TextStyle.Bold : TextStyle.None;

        if (string.IsNullOrEmpty(Text))
        {
            lines.Add(RenderLine.Single(string.Empty, Foreground));
            return;
        }

        var remaining = Text.AsSpan();
        while (remaining.Length > 0)
        {
            var take = Math.Min(width, remaining.Length);
            lines.Add(RenderLine.Single(remaining[..take].ToString(), Foreground, style));
            remaining = remaining[take..];
        }
    }
}
