/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgent.Models.Blocks
*文件名： OrchestrationNodeBlock
*版本号： V1.0.0.0
*唯一标识：编排子 Agent 节点状态块
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/16
*描述：编排子 Agent 节点的单行实时状态块（参考 opencode 的 subagent 展示）。
 *运行中显示动画 spinner + ▶ + 节点描述 + 实时耗时；完成后变为
 *✓ 描述 · 耗时（绿色）或 ✗ 描述 · 失败原因（红色）。同层并行节点各占一行，
 *各自独立动画，避免节点执行期（最长可达节点超时阈值）界面无任何反馈。
 *同时缓存该节点内部活动（思考/正文/工具调用/结果），供点击进入整屏详情查看。
 *
*****************************************************************************/
using LubanAgentCli.App.ViewModels;

// 消歧：全局 using 引入了 Spectre.Console.Color
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 编排子 Agent 节点的单行实时状态块：
/// <list type="bullet">
///   <item>运行中：<c>⠋ ▶ 节点描述 · N.Ns</c>（淡黄，带动画）。</item>
///   <item>成功：<c>✓ 节点描述 · N.Ns</c>（绿色）。</item>
///   <item>失败：<c>✗ 节点描述 · 失败原因</c>（红色）。</item>
///   <item>跳过：<c>− 节点描述 · 已跳过（前驱失败）</c>（灰色）。</item>
/// </list>
/// 不可折叠；点击该行进入节点详情（整屏模态），查看子代理的思考、工具调用与结果。
/// </summary>
public sealed class OrchestrationNodeBlock : Block
{
    private static readonly string[] Frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    /// <summary>活动缓存上限，防止长时间节点（可达超时阈值）无限增长。</summary>
    private const int MaxActivities = 500;

    private readonly ConversationDocument _doc;
    private readonly IUiDispatcher _dispatcher;
    private readonly List<NodeActivityItem> _activities = new();
    private int _frameIndex;
    private Timer? _animationTimer;
    private volatile bool _stopped;
    private string _description;
    private bool _failed;
    private string? _error;
    private double? _elapsedSeconds;
    private bool _skipped;

    /// <summary>节点标识（任务图谱中的节点 Id）。</summary>
    public string NodeId { get; }

    /// <summary>节点描述（展示文案）。</summary>
    public string Description => _description;

    /// <summary>节点是否已失败。</summary>
    public bool IsFailed => _failed;

    /// <summary>节点是否已因关键前驱失败被跳过。</summary>
    public bool IsSkipped => _skipped;

    /// <summary>节点失败原因（仅失败时有值）。</summary>
    public string? Error => _error;

    /// <summary>节点耗时秒数（完成后有值）。</summary>
    public double? ElapsedSeconds => _elapsedSeconds;

    /// <summary>节点内部活动快照（按发生顺序，最多 <see cref="MaxActivities"/> 条）。</summary>
    public IReadOnlyList<NodeActivityItem> Activities
    {
        get
        {
            lock (_activities) return _activities.ToArray();
        }
    }

    /// <summary>
    /// 初始化编排节点状态块。
    /// </summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="description">节点描述（用作展示文案）。</param>
    /// <param name="doc">会话文档，用于动画刷新时通知重绘。</param>
    /// <param name="dispatcher">UI 线程调度器，动画帧需编组到 UI 线程。</param>
    public OrchestrationNodeBlock(string nodeId, string description, ConversationDocument doc, IUiDispatcher dispatcher)
    {
        NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        _description = string.IsNullOrWhiteSpace(description) ? nodeId : description;
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        StartAnimation();
    }

    /// <inheritdoc/>
    public override bool IsFoldable => false;

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        LineCount = 1;
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var elapsed = _elapsedSeconds ?? (Duration ?? (DateTime.UtcNow - StartedAtUtc)).TotalSeconds;
        string text;
        Color color;

        if (_failed)
        {
            // 失败行把耗时放在原因之前，与成功行 `✓ 描述 · Ns` 的节奏一致
            var timing = _elapsedSeconds is { } sec ? $"{sec:F1}s · " : "";
            text = $"✗ {_description} · {timing}{_error ?? "失败"}";
            color = BlockColors.Failure;
        }
        else if (_skipped)
        {
            text = $"− {_description} · 已跳过（前驱失败）";
            color = BlockColors.System;
        }
        else if (IsComplete)
        {
            text = $"✓ {_description} · {elapsed:F1}s";
            color = BlockColors.Success;
        }
        else
        {
            var frame = Frames[_frameIndex % Frames.Length];
            text = $"{frame} ▶ {_description} · {elapsed:F1}s";
            color = BlockColors.ToolCall;
        }

        // 追加可点击提示，让用户知道该行能打开子代理详情（仿 opencode 的 subagent 展开）
        var hint = HasActivities ? "  [点击查看详情]" : "";
        var truncated = TextMeasure.TruncateByColumns(text + hint, width);
        lines.Add(RenderLine.Single(truncated, color));
    }

    /// <summary>是否已缓存到节点内部活动（有活动时该行才可点击查看详情）。</summary>
    public bool HasActivities
    {
        get
        {
            lock (_activities) return _activities.Count > 0;
        }
    }

    /// <inheritdoc/>
    public override HitActionResult? HitTest(int localLine)
    {
        if (localLine != 0) return null;
        return HasActivities
            ? new HitActionResult(HitActionType.OpenNodeDetail, this)
            : null;
    }

    /// <summary>
    /// 追加一条节点内部活动。须在 UI 线程调用。
    /// 超过 <see cref="MaxActivities"/> 时丢弃最旧条目（保留最近活动，供详情视图查看）。
    /// </summary>
    /// <param name="activity">活动条目。</param>
    public void AppendActivity(NodeActivityItem activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var becameClickable = false;
        lock (_activities)
        {
            if (_activities.Count == 0) becameClickable = true;
            _activities.Add(activity);
            if (_activities.Count > MaxActivities)
            {
                _activities.RemoveRange(0, _activities.Count - MaxActivities);
            }
        }

        // 首条活动到达时行内提示从无到有，需要重新布局该行
        if (becameClickable)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// 标记节点执行成功。须在 UI 线程调用。
    /// </summary>
    /// <param name="elapsedSeconds">节点耗时（秒），null 表示用本块自身计时。</param>
    /// <param name="description">节点描述（用于刷新文案），null 表示沿用原描述。</param>
    public void MarkSucceeded(double? elapsedSeconds, string? description)
    {
        if (IsComplete) return;
        if (!string.IsNullOrWhiteSpace(description)) _description = description;
        _elapsedSeconds = elapsedSeconds;
        MarkComplete();
    }

    /// <summary>
    /// 标记节点执行失败。须在 UI 线程调用。
    /// </summary>
    /// <param name="error">失败原因。</param>
    /// <param name="elapsedSeconds">节点耗时（秒），null 表示用本块自身计时。</param>
    /// <param name="description">节点描述（用于刷新文案），null 表示沿用原描述。</param>
    public void MarkFailed(string? error, double? elapsedSeconds, string? description)
    {
        if (IsComplete) return;
        if (!string.IsNullOrWhiteSpace(description)) _description = description;
        _failed = true;
        _error = error;
        _elapsedSeconds = elapsedSeconds;
        MarkComplete();
    }

    /// <summary>
    /// 标记节点因关键前驱失败被跳过。须在 UI 线程调用。
    /// </summary>
    /// <param name="description">节点描述（用于刷新文案），null 表示沿用原描述。</param>
    public void MarkSkipped(string? description)
    {
        if (IsComplete) return;
        if (!string.IsNullOrWhiteSpace(description)) _description = description;
        _skipped = true;
        MarkComplete();
    }

    /// <inheritdoc/>
    public override void MarkComplete()
    {
        base.MarkComplete();
        StopAnimation();
        NotifyChanged();
    }

    private void StartAnimation()
    {
        _animationTimer = new Timer(_ =>
        {
            if (_stopped) return;
            _dispatcher.Invoke(() =>
            {
                if (_stopped) return;
                _frameIndex++;
                NotifyChanged();
            });
        }, null, 0, 100);
    }

    private void StopAnimation()
    {
        _stopped = true;
        _animationTimer?.Dispose();
        _animationTimer = null;
    }

    private void NotifyChanged()
    {
        _doc.NotifyBlockChanged(this);
    }
}