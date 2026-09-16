/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCli.App.Views
*文件名： NodeDetailView
*版本号： V1.0.0.0
*唯一标识：子代理节点详情视图
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/16
*描述：子代理节点详情视图（整屏模态）。展示某个编排节点内部的思考、正文、
 *工具调用与工具结果时间轴，让子代理执行过程像主对话一样可读（参考 opencode 的
 *subagent 详情）。支持 ←/→ 切换同批节点、↑/↓/PgUp/PgDn/Home/End 滚动、Esc 返回。
 *节点仍在运行时按 250ms 轮询活动快照增量刷新（不依赖 Block 的变更通知）。
 *
*****************************************************************************/
using Terminal.Gui.Text;

// 消歧：全局 using 引入了 Spectre.Console.Color 与 System.Attribute
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Views;

/// <summary>
/// 子代理节点详情视图。整屏模态覆盖主界面，头部显示节点序号/描述/状态，
/// 正文为该节点内部活动的自绘时间轴，底部为操作提示。
/// </summary>
internal sealed class NodeDetailView : Dialog
{
    private readonly IReadOnlyList<OrchestrationNodeBlock> _nodes;
    private readonly Label _header;
    private readonly Label _footer;
    private readonly NodeActivityView _body;
    private int _index;
    private int _lastSignature = -1;
    private Timer? _refreshTimer;
    private volatile bool _closed;

    /// <summary>
    /// 初始化子代理节点详情视图。
    /// </summary>
    /// <param name="nodes">同批节点快照（按文档顺序），←/→ 在本列表内切换。</param>
    /// <param name="index">初始展示的节点下标。</param>
    public NodeDetailView(IReadOnlyList<OrchestrationNodeBlock> nodes, int index)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0) throw new ArgumentException("节点列表不能为空", nameof(nodes));

        _nodes = nodes;
        _index = Math.Clamp(index, 0, nodes.Count - 1);

        Title = "子代理详情";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        BorderStyle = LineStyle.Single;
        SetScheme(TuiTheme.BuildScheme());

        _header = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(2),
            Height = 1,
            Text = string.Empty
        };
        _header.SetScheme(new Scheme(new Attribute(BlockColors.Accent, TuiTheme.Background, TextStyle.Bold))
        {
            Normal = new Attribute(BlockColors.Accent, TuiTheme.Background, TextStyle.Bold),
            Focus = new Attribute(BlockColors.Accent, TuiTheme.Background, TextStyle.Bold),
            Disabled = new Attribute(BlockColors.System, TuiTheme.Background)
        });

        _body = new NodeActivityView(() => _nodes[_index])
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(3)
        };

        _footer = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(2),
            Height = 1,
            Text = "←/→ 切换节点 · ↑/↓ PgUp/PgDn 滚动 · Esc 返回"
        };
        _footer.SetScheme(new Scheme(new Attribute(BlockColors.System, TuiTheme.Background))
        {
            Normal = new Attribute(BlockColors.System, TuiTheme.Background),
            Focus = new Attribute(BlockColors.System, TuiTheme.Background),
            Disabled = new Attribute(BlockColors.System, TuiTheme.Background)
        });

        Add(_header, _body, _footer);

        UpdateHeader();
        _body.SetFocus();

        // 运行中的节点持续产出活动，而 Block 仅在首条活动时通知变更，
        // 因此详情视图自行轮询活动快照（不做任何框架/VM 侧改造）。
        _refreshTimer = new Timer(_ => Refresh(), null, 250, 250);
    }

    /// <summary>
    /// 处理节点切换与关闭。滚动键由正文视图自行消费，未处理时冒泡到这里。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <returns>是否已处理。</returns>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorLeft)
        {
            SwitchNode(-1);
            return true;
        }

        if (key == Key.CursorRight)
        {
            SwitchNode(1);
            return true;
        }

        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closed = true;
            _refreshTimer?.Dispose();
            _refreshTimer = null;
        }
        base.Dispose(disposing);
    }

    private void SwitchNode(int delta)
    {
        if (_nodes.Count <= 1) return;

        _index = (_index + delta + _nodes.Count) % _nodes.Count;
        _lastSignature = -1;
        _body.OnNodeChanged();
        UpdateHeader();
        SetNeedsDraw();
    }

    private void Refresh()
    {
        if (_closed) return;

        var app = GetApp();
        if (app is null) return;

        app.Invoke(() =>
        {
            if (_closed) return;

            var node = _nodes[_index];
            var signature = node.Activities.Count * 2 + (node.IsComplete ? 1 : 0);

            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                UpdateHeader();
                SetNeedsDraw();
                return;
            }

            // 运行中的耗时/动画提示需要持续重绘；已完成且无新活动则不动
            if (!node.IsComplete) SetNeedsDraw();
        });
    }

    private void UpdateHeader()
    {
        var node = _nodes[_index];

        string status;
        if (node.IsFailed)
        {
            status = $"✗ 失败 · {node.ElapsedSeconds.GetValueOrDefault():F1}s · {node.Error ?? "未知原因"}";
        }
        else if (node.IsComplete)
        {
            status = $"✓ 完成 · {node.ElapsedSeconds.GetValueOrDefault():F1}s";
        }
        else
        {
            var elapsed = (DateTime.UtcNow - node.StartedAtUtc).TotalSeconds;
            status = $"▶ 运行中 · {elapsed:F1}s";
        }

        _header.Text = $"[{_index + 1}/{_nodes.Count}] {node.Description}  ·  {status}";
    }

    /// <summary>
    /// 节点活动时间轴自绘视图。自带滚动偏移与自动跟随底部，键处理只消费滚动键，
    /// ←/→/Esc 交还上层 Dialog。
    /// </summary>
    private sealed class NodeActivityView : View
    {
        private readonly Func<OrchestrationNodeBlock> _node;
        private readonly List<RenderLine> _lines = new(256);
        private int _builtWidth = -1;
        private int _builtCount = -1;
        private int _builtFirstStamp;
        private int _scroll;
        private bool _autoScroll = true;

        /// <summary>
        /// 初始化活动时间轴视图。
        /// </summary>
        /// <param name="node">当前节点的取值委托（切换节点后取到新节点）。</param>
        public NodeActivityView(Func<OrchestrationNodeBlock> node)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            CanFocus = true;
        }

        /// <summary>切换节点后重置缓存与滚动状态。</summary>
        public void OnNodeChanged()
        {
            _builtWidth = -1;
            _builtCount = -1;
            _builtFirstStamp = 0;
            _scroll = 0;
            _autoScroll = true;
        }

        /// <inheritdoc/>
        protected override bool OnKeyDown(Key key)
        {
            var page = Math.Max(1, Viewport.Height - 1);

            if (key == Key.CursorUp)
            {
                _autoScroll = false;
                _scroll = Math.Max(0, _scroll - 1);
                SetNeedsDraw();
                return true;
            }

            if (key == Key.CursorDown)
            {
                _scroll = Math.Min(MaxScroll, _scroll + 1);
                if (_scroll >= MaxScroll) _autoScroll = true;
                SetNeedsDraw();
                return true;
            }

            if (key == Key.PageUp)
            {
                _autoScroll = false;
                _scroll = Math.Max(0, _scroll - page);
                SetNeedsDraw();
                return true;
            }

            if (key == Key.PageDown)
            {
                _scroll = Math.Min(MaxScroll, _scroll + page);
                if (_scroll >= MaxScroll) _autoScroll = true;
                SetNeedsDraw();
                return true;
            }

            if (key == Key.Home)
            {
                _autoScroll = false;
                _scroll = 0;
                SetNeedsDraw();
                return true;
            }

            if (key == Key.End)
            {
                _autoScroll = true;
                _scroll = MaxScroll;
                SetNeedsDraw();
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        protected override bool OnDrawingContent(DrawContext? context)
        {
            var viewport = Viewport;
            if (viewport.Width <= 0 || viewport.Height <= 0) return true;

            var activities = _node().Activities;

            // 活动数或宽度变化才重建缓存（活动条目不可变，追加即新计数）
            var firstStamp = activities.Count > 0
                ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(activities[0])
                : 0;

            if (_builtWidth != viewport.Width || _builtCount != activities.Count || _builtFirstStamp != firstStamp)
            {
                Rebuild(activities, viewport.Width);
                _builtWidth = viewport.Width;
                _builtCount = activities.Count;
                _builtFirstStamp = firstStamp;
            }

            var maxScroll = MaxScroll;
            if (_autoScroll) _scroll = maxScroll;
            _scroll = Math.Clamp(_scroll, 0, maxScroll);

            if (_lines.Count == 0)
            {
                SetAttribute(TuiTheme.Attr(BlockColors.System, TextStyle.Italic));
                AddStr(1, 0, "（子代理暂未产生任何活动）");
                return true;
            }

            for (var row = 0; row < viewport.Height; row++)
            {
                var index = _scroll + row;
                if (index >= _lines.Count) break;
                DrawLine(_lines[index], row, viewport.Width);
            }

            return true;
        }

        private int MaxScroll => Math.Max(0, _lines.Count - Math.Max(1, Viewport.Height));

        private void DrawLine(RenderLine line, int row, int width)
        {
            var col = 0;
            foreach (var segment in line.Segments)
            {
                if (col >= width) break;

                SetAttribute(TuiTheme.Attr(segment.Fg, segment.Style, segment.Bg ?? TuiTheme.Background));

                var text = TextMeasure.TruncateByColumns(segment.Text, width - col);
                if (text.Length == 0) continue;

                AddStr(col, row, text);
                col += text.GetColumns();
            }
        }

        private void Rebuild(IReadOnlyList<NodeActivityItem> activities, int width)
        {
            _lines.Clear();

            var contentWidth = Math.Max(1, width - 2);

            for (var i = 0; i < activities.Count; i++)
            {
                var activity = activities[i];

                // 思考/正文为 150ms 节流的增量片段，显示前合并同类连续片段成段
                if (activity.Kind is NodeActivityKind.Thinking or NodeActivityKind.Text)
                {
                    var isThinking = activity.Kind == NodeActivityKind.Thinking;
                    var buffer = new StringBuilder(activity.Content ?? string.Empty);

                    while (i + 1 < activities.Count && activities[i + 1].Kind == activity.Kind)
                    {
                        buffer.Append(activities[++i].Content);
                    }

                    var color = isThinking ? BlockColors.Thinking : BlockColors.AssistantText;
                    var style = isThinking ? TextStyle.Italic : TextStyle.None;

                    _lines.Add(RenderLine.Single(isThinking ? "▾ 思考" : " 正文",
                        color, isThinking ? TextStyle.Bold : TextStyle.None));
                    AppendWrapped(buffer.ToString(), color, style, contentWidth);
                    continue;
                }

                if (activity.Kind == NodeActivityKind.ToolCall)
                {
                    AppendWrapped($"→ {activity.ToolName ?? "工具"} {activity.Content}".TrimEnd(),
                        BlockColors.ToolCall, TextStyle.None, contentWidth);
                    continue;
                }

                if (activity.Kind == NodeActivityKind.ToolResult)
                {
                    AppendWrapped($"  ↳ {activity.Content ?? "(无返回)"}".TrimEnd(),
                        BlockColors.ToolResult, TextStyle.None, contentWidth);
                }
            }
        }

        private void AppendWrapped(string text, Color color, TextStyle style, int width)
        {
            if (string.IsNullOrEmpty(text))
            {
                _lines.Add(RenderLine.Blank);
                return;
            }

            // TextMeasure.WrapByColumns 不处理换行符，先按 \n 拆行再逐行按列宽折行
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var rawLine in normalized.Split('\n'))
            {
                if (rawLine.Length == 0)
                {
                    _lines.Add(RenderLine.Blank);
                    continue;
                }

                var wrapped = TextMeasure.WrapByColumns(
                    new List<TextSegment> { new("  " + rawLine, color, null, style) }, width);

                if (wrapped.Count == 0)
                {
                    _lines.Add(RenderLine.Blank);
                    continue;
                }

                _lines.AddRange(wrapped);
            }
        }
    }
}