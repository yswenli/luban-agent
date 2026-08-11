/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models
*文件名： ConversationDocument
*版本号： V1.0.0.0
*唯一标识：会话文档模型
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：会话区文档模型，管理 Block 集合、滚动偏移、自动跟随与可见行查询
*
*****************************************************************************/
using LubanAgent.Models.Blocks;

namespace LubanAgent.Models;

/// <summary>
/// 会话区文档模型。持有 Block 集合，管理布局、滚动状态、自动跟随与可见行查询。
/// 由 ConversationViewModel 写入（Application.Invoke marshaling），
/// ConversationView 读取（OnDrawingContent 消费 RenderLine 列表）。
/// </summary>
public sealed class ConversationDocument
{
    private readonly List<Block> _blocks = new();
    private int _layoutWidth;

    /// <summary>
    /// Block 列表（只读）。
    /// </summary>
    public IReadOnlyList<Block> Blocks
    {
        get
        {
            lock (_lock) return _blocks.ToList().AsReadOnly();
        }
    }

    /// <summary>Block 数量。</summary>
    public int BlockCount
    {
        get { lock (_lock) return _blocks.Count; }
    }

    /// <summary>所有 Block 的总行数。</summary>
    public int TotalLines
    {
        get { lock (_lock) return _totalLines; }
        private set { lock (_lock) _totalLines = value; }
    }

    /// <summary>当前视口高度（行数），由 View 在尺寸变化时设置。</summary>
    public int ViewportHeight
    {
        get => _viewportHeight;
        set
        {
            _viewportHeight = value;
            ClampScroll();
        }
    }

    /// <summary>当前布局宽度（列数），由 View 在尺寸变化时设置。</summary>
    public int LayoutWidth
    {
        get => _layoutWidth;
        set
        {
            if (_layoutWidth != value && value > 0)
            {
                _layoutWidth = value;
                RelayoutAll();
            }
        }
    }

    /// <summary>是否自动跟随底部（流式追加时保持贴底）。</summary>
    public bool AutoScroll { get; set; } = true;

    /// <summary>当前滚动偏移（从顶部算起的行数）。</summary>
    public int ScrollOffset
    {
        get => _scrollOffset;
        private set => _scrollOffset = Math.Max(0, value);
    }

    /// <summary>AutoScroll 关闭后，底部有 N 行新内容未显示。</summary>
    public int PendingNewLines => AutoScroll ? 0 : Math.Max(0, TotalLines - (ScrollOffset + ViewportHeight));

    // Internal state
    private int _totalLines;
    private int _scrollOffset;
    private int _viewportHeight;
    private readonly object _lock = new();

    /// <summary>Block 集合变化时触发（ViewModel 订阅后通知 View 刷新）。</summary>
    public event Action? BlocksChanged;

    /// <summary>
    /// 追加一个 Block 到文档末尾。对新 Block 执行 Layout(width)，更新总行数，
    /// 若 AutoScroll 为 true 则自动滚动到底部。
    /// </summary>
    /// <param name="block">要追加的 Block。</param>
    public void AppendBlock(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);

        block.Layout(_layoutWidth);

        lock (_lock)
        {
            _blocks.Add(block);
            TotalLines += block.LineCount;
        }

        if (AutoScroll)
        {
            SnapToBottom();
        }

        BlocksChanged?.Invoke();
    }

    /// <summary>
    /// 向最后一个 Block 追加流式 token。若当前不是 AssistantMessageBlock 则先新建一个。
    /// 调用方负责在追加后调用 <see cref="RelayoutLastBlock"/> 更新布局。
    /// </summary>
    /// <param name="token">流式 token 文本。</param>
    /// <returns>追加到的 AssistantMessageBlock。</returns>
    public AssistantMessageBlock AppendToAnswerBlock(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        AssistantMessageBlock ab;

        lock (_lock)
        {
            if (_blocks.Count > 0 && _blocks[^1] is AssistantMessageBlock existing)
            {
                ab = existing;
            }
            else
            {
                ab = new AssistantMessageBlock();
                _blocks.Add(ab);
                // 暂不 Layout，等 RelayoutLastBlock 时一起做
            }
        }

        ab.AppendContent(token);
        return ab;
    }

    /// <summary>
    /// 仅重新布局最后一个 Block（流式追加优化：token 追加只影响末尾）。
    /// 应先调用 <see cref="AppendToAnswerBlock(string)"/> 再调用本方法。
    /// </summary>
    public void RelayoutLastBlock()
    {
        Block? last;
        int oldLineCount;

        lock (_lock)
        {
            if (_blocks.Count == 0)
            {
                return;
            }

            last = _blocks[^1];
            oldLineCount = last.LineCount;
            last.Layout(_layoutWidth);
            TotalLines = TotalLines - oldLineCount + last.LineCount;
        }

        if (AutoScroll)
        {
            SnapToBottom();
        }

        BlocksChanged?.Invoke();
    }

    /// <summary>
    /// 标记最后一个未完成的 Block 为完成（记录 Duration）。
    /// </summary>
    public void MarkLastComplete()
    {
        lock (_lock)
        {
            if (_blocks.Count > 0 && !_blocks[^1].IsComplete)
            {
                _blocks[^1].MarkComplete();
            }
        }

        BlocksChanged?.Invoke();
    }

    /// <summary>
    /// 获取当前视口内可见的渲染行列表。
    /// 先对所有 Block 执行 Render，然后从 ScrollOffset 开始取 ViewportHeight 行。
    /// </summary>
    /// <param name="output">接收渲染行的列表（由调用方提供以避免分配）。</param>
    /// <param name="width">当前布局宽度。</param>
    public void GetVisibleLines(List<RenderLine> output, int width)
    {
        output.Clear();

        List<Block> blocks;
        int totalLines;
        int scrollOffset;
        int viewportHeight;

        lock (_lock)
        {
            blocks = _blocks.ToList(); // 快照
            totalLines = _totalLines;
            scrollOffset = _scrollOffset;
            viewportHeight = _viewportHeight;
        }

        if (viewportHeight <= 0 || width <= 0)
        {
            return;
        }

        var lines = new List<RenderLine>();
        var consumed = 0;

        foreach (var block in blocks)
        {
            var blockStart = consumed;
            consumed += block.LineCount;

            // 跳过完全在视口上方的 Block
            if (blockStart + block.LineCount <= scrollOffset)
            {
                continue;
            }

            // 跳过完全在视口下方的 Block
            if (blockStart >= scrollOffset + viewportHeight)
            {
                break;
            }

            block.Render(lines, width);

            // 根据偏移裁切
            var blockLocalOffset = Math.Max(0, scrollOffset - blockStart);
            var blockVisibleEnd = Math.Min(lines.Count, scrollOffset + viewportHeight - blockStart + blockLocalOffset);

            for (var i = blockLocalOffset; i < blockVisibleEnd && i < lines.Count; i++)
            {
                output.Add(lines[i]);
            }

            lines.Clear();

            if (output.Count >= viewportHeight)
            {
                break;
            }
        }
    }

    /// <summary>
    /// 命中测试：给定全局行号，返回对应的 Block 与内联行号。
    /// </summary>
    /// <param name="globalLine">全局行号（0-based，从文档顶部算起）。</param>
    /// <returns>Block 与内联行号元组；行号无效时返回 (null, -1)。</returns>
    public (Block? Block, int LocalLine) BlockAtLine(int globalLine)
    {
        if (globalLine < 0)
        {
            return (null, -1);
        }

        lock (_lock)
        {
            var consumed = 0;
            foreach (var block in _blocks)
            {
                if (globalLine < consumed + block.LineCount)
                {
                    return (block, globalLine - consumed);
                }
                consumed += block.LineCount;
            }
        }

        return (null, -1);
    }

    /// <summary>
    /// 滚动到底部。
    /// </summary>
    public void SnapToBottom()
    {
        ScrollOffset = Math.Max(0, TotalLines - ViewportHeight);
    }

    /// <summary>
    /// 向上滚动指定行数。
    /// </summary>
    /// <param name="lines">行数（正值）。</param>
    public void ScrollUp(int lines)
    {
        if (lines <= 0) return;
        ScrollOffset = Math.Max(0, _scrollOffset - lines);
        if (ScrollOffset < TotalLines - ViewportHeight)
        {
            AutoScroll = false;
        }
    }

    /// <summary>
    /// 向下滚动指定行数。
    /// </summary>
    /// <param name="lines">行数（正值）。</param>
    public void ScrollDown(int lines)
    {
        if (lines <= 0) return;
        ScrollOffset = Math.Min(TotalLines - ViewportHeight, _scrollOffset + lines);
        if (ScrollOffset >= TotalLines - ViewportHeight)
        {
            AutoScroll = true;
        }
    }

    /// <summary>
    /// 重新布局所有 Block（宽度变化时调用）。
    /// </summary>
    private void RelayoutAll()
    {
        lock (_lock)
        {
            TotalLines = 0;
            foreach (var block in _blocks)
            {
                block.Layout(_layoutWidth);
                TotalLines += block.LineCount;
            }
        }

        ClampScroll();
        BlocksChanged?.Invoke();
    }

    /// <summary>
    /// 确保滚动偏移在有效范围内。
    /// </summary>
    private void ClampScroll()
    {
        var maxOffset = Math.Max(0, TotalLines - ViewportHeight);
        if (_scrollOffset > maxOffset)
        {
            _scrollOffset = maxOffset;
        }
    }
}
