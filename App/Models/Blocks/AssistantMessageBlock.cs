/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： AssistantMessageBlock
*版本号： V1.0.0.0
*唯一标识：助手回复 Block
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：AI 助手回复消息，支持流式追加与轻量 Markdown 着色
*
*****************************************************************************/
using System.Text;

using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// AI 助手回复 Block。支持流式追加内容（<see cref="AppendContent(string)"/>），
/// 不可折叠。最终渲染时由 MarkdownLightRenderer 逐行着色。
/// </summary>
public sealed class AssistantMessageBlock : Block
{
    private readonly StringBuilder _content = new();

    /// <summary>不可折叠。</summary>
    public override bool IsFoldable => false;

    /// <summary>
    /// 当前已累积的文本内容。
    /// </summary>
    public string Content => _content.ToString();

    /// <summary>
    /// 追加流式 token。追加后需重新 <see cref="Layout"/> 以更新 LineCount。
    /// </summary>
    /// <param name="token">流式 token 文本。</param>
    public void AppendContent(string token)
    {
        _content.Append(token);
    }

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        var text = _content.ToString();
        if (string.IsNullOrEmpty(text))
        {
            LineCount = 1;
            return;
        }

        var parsed = MarkdownLightRenderer.ParseLines(text, BlockColors.AssistantText);
        var wrapped = MarkdownLightRenderer.WrapLines(parsed, Math.Max(1, width));
        LineCount = Math.Max(1, wrapped.Count);
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var text = _content.ToString();
        if (string.IsNullOrEmpty(text))
        {
            lines.Add(RenderLine.Single(string.Empty, BlockColors.AssistantText));
            return;
        }

        var parsed = MarkdownLightRenderer.ParseLines(text, BlockColors.AssistantText);
        var wrapped = MarkdownLightRenderer.WrapLines(parsed, Math.Max(1, width));
        lines.AddRange(wrapped);
    }
}
