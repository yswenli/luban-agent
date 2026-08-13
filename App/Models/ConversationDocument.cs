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
using LubanAgentCli.App.Models.Blocks;

namespace LubanAgentCli.App.Models;

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
    /// 清空所有 Block 并重置滚动状态。
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _blocks.Clear();
            TotalLines = 0;
        }
        _scrollOffset = 0;
        AutoScroll = true;
        var handler = BlocksChanged;
        handler?.Invoke();
    }

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

        var handler = BlocksChanged;
        handler?.Invoke();
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
                // 暂不 Layout，等 RelayoutLastBlock 时一起做；
                // 但初始 LineCount（默认 1）必须先记账，否则 RelayoutLastBlock
                // 的 TotalLines - oldLineCount 会使账本永久少 1 行
                TotalLines += ab.LineCount;
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
        using var _diagScope = TuiDiag.Measure("Doc.RelayoutLastBlock");

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

        var handler = BlocksChanged;
        handler?.Invoke();
    }

    /// <summary>
    /// 通知文档某个已存在的 Block 内容发生变化：重新布局该 Block、重算总行数并触发
    /// <see cref="BlocksChanged"/>。供流式追加 ThinkingBlock 等不走 <see cref="AppendBlock"/>/
    /// <see cref="RelayoutLastBlock"/> 路径的更新使用，避免 TotalLines 账本脱节。
    /// </summary>
    /// <param name="block">内容已变化的 Block（必须已在文档中）。</param>
    public void NotifyBlockChanged(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);

        lock (_lock)
        {
            block.Layout(_layoutWidth);

            var total = 0;
            foreach (var b in _blocks)
            {
                total += b.LineCount;
            }
            TotalLines = total;
        }

        if (AutoScroll)
        {
            SnapToBottom();
        }

        var handler = BlocksChanged;
        handler?.Invoke();
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

        var handler = BlocksChanged;
        handler?.Invoke();
    }

    /// <summary>
    /// 获取当前视口内可见的渲染行列表。
    /// 遍历所有 Block，按全局行号裁切出 [ScrollOffset, ScrollOffset+ViewportHeight) 范围内的行。
    /// </summary>
    /// <param name="output">接收渲染行的列表（由调用方提供以避免分配）。</param>
    /// <param name="width">当前布局宽度。</param>
    public void GetVisibleLines(List<RenderLine> output, int width)
    {
        using var _diagScope = TuiDiag.Measure("Doc.GetVisibleLines", $"blocks={BlockCount}");

        output.Clear();

        List<Block> blocks;
        int scrollOffset;
        int viewportHeight;

        lock (_lock)
        {
            blocks = _blocks.ToList(); // 快照
            scrollOffset = _scrollOffset;
            viewportHeight = _viewportHeight;
        }

        if (viewportHeight <= 0 || width <= 0)
        {
            return;
        }

        // 视口的全局行范围 [viewStart, viewEnd)
        var viewStart = scrollOffset;
        var viewEnd = scrollOffset + viewportHeight;

        var globalLine = 0; // 当前 Block 起始的全局行号

        foreach (var block in blocks)
        {
            var blockStart = globalLine;
            var blockEnd = blockStart + block.LineCount;
            globalLine = blockEnd;

            // 跳过完全在视口上方的 Block
            if (blockEnd <= viewStart)
            {
                continue;
            }

            // 跳过完全在视口下方的 Block
            if (blockStart >= viewEnd)
            {
                break;
            }

            // 渲染当前 Block 的所有行
            var blockLines = new List<RenderLine>();
            block.Render(blockLines, width);

            // 计算该 Block 在视口内的局部行范围
            var localStart = Math.Max(0, viewStart - blockStart);
            var localEnd = Math.Min(blockLines.Count, viewEnd - blockStart);

            for (var i = localStart; i < localEnd; i++)
            {
                output.Add(blockLines[i]);
                if (output.Count >= viewportHeight)
                {
                    return;
                }
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
        var handler = BlocksChanged;
        handler?.Invoke();
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
