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

    // Markdown 解析缓存：内容与宽度均未变化时直接复用上次结果，
    // 避免每次 Layout/Render 对全文重新 ParseLines + WrapLines（O(n)）。
    private string? _cachedText;
    private int _cachedWidth = -1;
    private List<RenderLine>? _cachedLines;

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
        var lines = GetLines(width);
        LineCount = Math.Max(1, lines.Count);
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        lines.AddRange(GetLines(width));
    }

    /// <summary>
    /// 获取当前内容按指定宽度解析换行后的渲染行，命中缓存时零解析开销。
    /// </summary>
    private List<RenderLine> GetLines(int width)
    {
        var w = Math.Max(1, width);
        var text = _content.ToString();

        if (string.IsNullOrEmpty(text))
        {
            return [RenderLine.Single(string.Empty, BlockColors.AssistantText)];
        }

        if (_cachedLines is not null && _cachedWidth == w
            && string.Equals(_cachedText, text, StringComparison.Ordinal))
        {
            return _cachedLines;
        }

        var parsed = MarkdownLightRenderer.ParseLines(text, BlockColors.AssistantText);
        var wrapped = MarkdownLightRenderer.WrapLines(parsed, w);

        _cachedText = text;
        _cachedWidth = w;
        _cachedLines = wrapped;

        return wrapped;
    }
}
