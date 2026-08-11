/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Views
*文件名： ConversationView
*版本号： V1.0.0.0
*唯一标识：会话区自绘视图
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：会话区自绘视图，消费 Block 文档模型，支持流式刷新节流、自动滚动跟随与鼠标交互
*
*****************************************************************************/
using LubanAgent.App;
using LubanAgent.Infrastructure;
using LubanAgent.Models;
using LubanAgent.Models.Blocks;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除）
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgent.Views;

/// <summary>
/// 会话区自绘视图。消费 <see cref="ConversationDocument"/>，逐 Segment 渲染 Block 输出，
/// 通过 <see cref="FlushThrottle"/> 合并流式追加的重绘，支持尾部自动跟随、手动上滚断开、
/// 鼠标滚轮滚动、鼠标点击折叠/展开 Block。
/// </summary>
internal sealed class ConversationView : View
{
    private readonly ConversationDocument _doc;
    private readonly FlushThrottle _throttle;
    private readonly List<RenderLine> _renderBuffer = new(256);
    private bool _dirty = true;
    private int _lastRenderedScrollOffset = -1;
    private int _lastRenderedViewportHeight;

    /// <summary>
    /// 初始化会话区视图并关联文档模型。
    /// </summary>
    /// <param name="doc">会话文档模型（由 RootView 注入）。</param>
    public ConversationView(ConversationDocument doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        CanFocus = false;
        MousePositionTracking = true; // 允许终端原生鼠标选择

        _throttle = new FlushThrottle(() =>
        {
            GetApp()?.Invoke(() =>
            {
                SetNeedsDraw();
            });
        });

        _doc.BlocksChanged += OnDocChanged;
    }

    /// <summary>
    /// 所关联的会话文档模型。
    /// </summary>
    public ConversationDocument Document => _doc;

    /// <summary>
    /// 追加用户消息到文档并立即刷新。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    public void AppendUserMessage(string text)
    {
        _doc.AppendBlock(new UserMessageBlock(text));
    }

    /// <summary>
    /// 追加系统消息到文档。
    /// </summary>
    /// <param name="text">消息文本。</param>
    /// <param name="bold">是否加粗。</param>
    public void AppendSystemMessage(string text, bool bold = false)
    {
        _doc.AppendBlock(new SystemBlock(text, bold));
    }

    /// <summary>
    /// 流式 token 追加（步骤 4 接入 agent 后使用）。追加到末尾 AssistantMessageBlock
    /// 并节流刷新。
    /// </summary>
    /// <param name="token">流式 token。</param>
    public void AppendStreamToken(string token)
    {
        _doc.AppendToAnswerBlock(token);
        _doc.RelayoutLastBlock();
        _throttle.Schedule();
    }

    /// <summary>
    /// 标记最后一个 Block 为完成并立即刷新。
    /// </summary>
    public void FlushAndComplete()
    {
        _throttle.Flush();
        _doc.MarkLastComplete();
        SetNeedsDraw();
    }

    /// <summary>
    /// 文档变更时标记脏，下次 OnDrawingContent 重新计算。
    /// </summary>
    private void OnDocChanged()
    {
        _dirty = true;
        SetNeedsDraw();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _doc.BlocksChanged -= OnDocChanged;
            _throttle.Dispose();
        }
        base.Dispose(disposing);
    }

    // ────────────── 自绘 ──────────────

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        var viewport = Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return true;
        }

        // 仅当文档变更、滚动偏移变化或视口尺寸变化时才重新计算可见行
        var viewportChanged = _lastRenderedViewportHeight != viewport.Height;
        var scrollChanged = _doc.ScrollOffset != _lastRenderedScrollOffset;

        if (_dirty || viewportChanged || scrollChanged)
        {
            _doc.LayoutWidth = viewport.Width;
            _doc.ViewportHeight = viewport.Height;
            _lastRenderedViewportHeight = viewport.Height;
            _lastRenderedScrollOffset = _doc.ScrollOffset;
            _dirty = false;

            _doc.GetVisibleLines(_renderBuffer, viewport.Width);
        }

        // 逐行逐 Segment 渲染（使用缓存的 _renderBuffer）
        for (var row = 0; row < _renderBuffer.Count && row < viewport.Height; row++)
        {
            var line = _renderBuffer[row];
            if (line.Segments.Count == 0) continue;

            var col = 0;
            foreach (var seg in line.Segments)
            {
                if (col >= viewport.Width) break;

                var bg = seg.Bg ?? TuiTheme.Background;
                SetAttribute(TuiTheme.Attr(seg.Fg, seg.Style, bg));

                var text = Truncate(seg.Text, viewport.Width - col);
                if (text.Length > 0)
                {
                    AddStr(col, row, text);
                    col += text.Length;
                }
            }
        }

        // 非自动滚动时底部显示提示
        if (!_doc.AutoScroll && _doc.PendingNewLines > 0)
        {
            var hint = $"↓ {_doc.PendingNewLines} 行新内容 (滚到底部恢复自动跟随)";
            SetAttribute(TuiTheme.Attr(TuiTheme.Accent, TextStyle.Bold));
            var row = Math.Min(_renderBuffer.Count, viewport.Height - 1);
            AddStr(0, row, Truncate(hint, viewport.Width));
        }

        return true;
    }

    // ────────────── 鼠标事件 ──────────────

    /// <inheritdoc/>
    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            _doc.ScrollDown(3);
            _dirty = true;
            SetNeedsDraw();
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            _doc.ScrollUp(3);
            _dirty = true;
            SetNeedsDraw();
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && !mouse.Flags.HasFlag(MouseFlags.Shift))
        {
            var pos = mouse.Position;
            if (pos is null) return false;

            var globalLine = _doc.ScrollOffset + pos.Value.Y;
            var (block, localLine) = _doc.BlockAtLine(globalLine);

            if (block is not null)
            {
                var action = block.HitTest(localLine);
                if (action?.Type == HitActionType.ToggleCollapse)
                {
                    block.IsCollapsed = !block.IsCollapsed;
                    _doc.LayoutWidth = Viewport.Width; // 触发重新布局
                    _dirty = true;
                    SetNeedsDraw();
                    return true;
                }

                if (action?.Type == HitActionType.SelectOption && block is InlineChoiceBlock choice)
                {
                    choice.Select((int)(action.Data ?? 0));
                    _dirty = true;
                    SetNeedsDraw();
                    return true;
                }
            }
        }

        return false; // 未处理，交给基类
    }

    // ────────────── 快捷键 ──────────────

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        // 将按键转发给最后一个 Block（如果是 InlineChoiceBlock）
        if (_doc.BlockCount > 0)
        {
            // BlockAtLine 取末尾最后一个 Block，比 _doc.Blocks[^1] 避免全量快照分配
            var (lastBlock, _) = _doc.BlockAtLine(Math.Max(0, _doc.TotalLines - 1));
            if (lastBlock is InlineChoiceBlock choice && choice.Selected is null)
            {
            if (choice.HandleKey(key))
            {
                _dirty = true;
                SetNeedsDraw();
                return true;
            }
            }
        }

        return base.OnKeyDown(key);
    }

    // ────────────── 文本截断 ──────────────

    /// <summary>
    /// 按字符数直接截断（非 CJK 精确宽度）。Segment 文本通常已被 Markdown 拆分为较短片段。
    /// </summary>
    private static string Truncate(string text, int maxWidth)
    {
        if (maxWidth <= 0) return string.Empty;
        return text.Length <= maxWidth ? text : text[..maxWidth];
    }
}
