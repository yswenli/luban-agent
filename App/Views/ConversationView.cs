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


using LubanAgentCli.App.Models;
using LubanAgentCli.App.Models.Blocks;

namespace LubanAgentCli.App.Views;

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

    // 鼠标拖拽选择状态
    private (int Row, int Col)? _selectionStart;
    private (int Row, int Col)? _selectionEnd;
    private bool _isDragging;
    private bool _justDragged;
    private string? _selectedText;
    private (int Row, int Col)? _dragStartPos; // 记录拖拽起始位置，用于判断是否为有效拖拽

    /// <summary>
    /// 初始化会话区视图并关联文档模型。
    /// </summary>
    /// <param name="doc">会话文档模型（由 RootView 注入）。</param>
    public ConversationView(ConversationDocument doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        CanFocus = false;
        // 鼠标位置跟踪（DECSET 1003）会使终端在每次鼠标移动时上送事件、加重主循环负担，
        // 因此平时关闭，仅在左键按下后的拖拽选择期间启用（见 OnMouseEvent）。

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
        using var _diagScope = TuiDiag.Measure("ConvView.Draw");

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
                var style = seg.Style;

                // 检查当前段是否在选中范围内
                if (_selectionStart.HasValue && _selectionEnd.HasValue)
                {
                    var (startRow, startCol) = _selectionStart.Value;
                    var (endRow, endCol) = _selectionEnd.Value;
                    // 确保 start <= end
                    if (startRow > endRow || (startRow == endRow && startCol > endCol))
                    {
                        (startRow, startCol, endRow, endCol) = (endRow, endCol, startRow, startCol);
                    }

                    bool inSelection = false;
                    if (row > startRow && row < endRow) inSelection = true;
                    else if (row == startRow && row == endRow && col >= startCol && col < endCol) inSelection = true;
                    else if (row == startRow && col >= startCol) inSelection = true;
                    else if (row == endRow && col < endCol) inSelection = true;

                    if (inSelection)
                    {
                        bg = TuiTheme.Accent;
                        style |= TextStyle.Bold;
                    }
                }

                SetAttribute(TuiTheme.Attr(seg.Fg, style, bg));

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

        var pos = mouse.Position;
        if (pos is null) return false;

        // 鼠标左键按下：记录起始位置，暂不开始选择；启用位置跟踪以接收拖拽移动事件
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            _dragStartPos = (pos.Value.Y, pos.Value.X);
            _isDragging = false;
            MousePositionTracking = true;
            return true;
        }

        // 鼠标移动：如果距离起始位置超过阈值，开始拖拽选择
        if (_dragStartPos.HasValue && mouse.Flags.HasFlag(MouseFlags.PositionReport))
        {
            var (startRow, startCol) = _dragStartPos.Value;
            var rowDiff = Math.Abs(pos.Value.Y - startRow);
            var colDiff = Math.Abs(pos.Value.X - startCol);

            // 拖拽阈值：移动超过 2 个字符或 1 行才认为是拖拽
            if (!_isDragging && (rowDiff > 1 || colDiff > 2))
            {
                _isDragging = true;
                _selectionStart = _dragStartPos;
                _selectionEnd = (pos.Value.Y, pos.Value.X);
                _selectedText = null;
                _dirty = true;
                SetNeedsDraw();
            }
            else if (_isDragging)
            {
                _selectionEnd = (pos.Value.Y, pos.Value.X);
                _dirty = true;
                SetNeedsDraw();
            }
            return true;
        }

        // 鼠标左键释放：结束选择，提取选中文本，关闭位置跟踪
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased) && _isDragging)
        {
            _isDragging = false;
            _justDragged = true;
            _selectionEnd = (pos.Value.Y, pos.Value.X);
            _selectedText = ExtractSelectedText();
            _dragStartPos = null; // 清除拖拽起始位置
            MousePositionTracking = false;
            _dirty = true;
            SetNeedsDraw();
            return true;
        }

        // 单击（非拖拽）：折叠/展开 Block 或选择选项
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && !mouse.Flags.HasFlag(MouseFlags.Shift))
        {
            // 未形成拖拽的点击：关闭位置跟踪
            if (!_isDragging)
            {
                MousePositionTracking = false;
            }

            // 拖拽刚结束时的 LeftButtonClicked 事件：保留选择，仅清除标志
            if (_justDragged)
            {
                _justDragged = false;
                _dragStartPos = null; // 清除拖拽起始位置
                return true;
            }

            // 真正的单击：如果有选中内容，点击空白处清除选择
            if (_selectionStart.HasValue)
            {
                _selectionStart = null;
                _selectionEnd = null;
                _selectedText = null;
                _dirty = true;
                SetNeedsDraw();
            }

            _dragStartPos = null; // 清除拖拽起始位置

            var globalLine = _doc.ScrollOffset + pos.Value.Y;
            var (block, localLine) = _doc.BlockAtLine(globalLine);

            if (block is not null)
            {
                var action = block.HitTest(localLine);
                if (action?.Type == HitActionType.ToggleCollapse)
                {
                    block.IsCollapsed = !block.IsCollapsed;
                    // 统一走文档记账：重布局该 Block、重算 TotalLines 并通知重绘。
                    // （原写法仅在宽度变化时触发重布局，同宽时展开内容的行数账本不更新）
                    _doc.NotifyBlockChanged(block);
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

        return false;
    }

    /// <summary>
    /// 提取当前选中区域的文本内容。
    /// </summary>
    private string? ExtractSelectedText()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue) return null;

        var (startRow, startCol) = _selectionStart.Value;
        var (endRow, endCol) = _selectionEnd.Value;

        // 确保 start <= end
        if (startRow > endRow || (startRow == endRow && startCol > endCol))
        {
            (startRow, startCol, endRow, endCol) = (endRow, endCol, startRow, startCol);
        }

        if (startRow == endRow && startCol == endCol) return null;

        var sb = new System.Text.StringBuilder();
        for (var row = startRow; row <= endRow && row < _renderBuffer.Count; row++)
        {
            var line = _renderBuffer[row];
            var lineText = string.Concat(line.Segments.Select(s => s.Text));

            if (row == startRow && row == endRow)
            {
                // 同一行
                var start = Math.Min(startCol, lineText.Length);
                var end = Math.Min(endCol, lineText.Length);
                if (start < end) sb.Append(lineText[start..end]);
            }
            else if (row == startRow)
            {
                // 起始行
                var start = Math.Min(startCol, lineText.Length);
                if (start < lineText.Length) sb.Append(lineText[start..]);
                sb.AppendLine();
            }
            else if (row == endRow)
            {
                // 结束行
                var end = Math.Min(endCol, lineText.Length);
                if (end > 0) sb.Append(lineText[..end]);
            }
            else
            {
                // 中间行
                sb.AppendLine(lineText);
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    // ────────────── 快捷键 ──────────────

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        // Ctrl+C：复制选中文本到剪贴板
        if (key == Key.C.WithCtrl && _selectedText is not null)
        {
            try
            {
                GetApp()?.Clipboard?.SetClipboardData(_selectedText);
            }
            catch
            {
                // 剪贴板访问失败时静默忽略
            }
            return true;
        }

        // 将按键转发给最后一个 Block（如果是 InlineChoiceBlock）
        if (_doc.BlockCount > 0)
        {
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
