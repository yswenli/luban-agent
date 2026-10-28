/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App.Models.Blocks
*文件名： ActionSpinnerBlock
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/17
*描述：每次 AI 动作时插入对话流，显示动画 + 阶段 + 耗时，完成后变为最终状态
*
*****************************************************************************/

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
    /// 对话流中的动态 spinner 块。每次动作（思考、调用工具、生成回复）插入一个，
    /// 完成后变为最终状态（"✓ 阶段 · 耗时"）。
    /// </summary>
public sealed class ActionSpinnerBlock : Block
{
    private static readonly string[] Frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    private string _description;
    private readonly ConversationDocument _doc;
    private readonly IUiDispatcher _dispatcher;
    private int _frameIndex;
    private Timer? _animationTimer;
    private volatile bool _stopped;

    /// <summary>
    /// 该块不可折叠：始终以单行动画形式出现在对话流中。
    /// </summary>
    public override bool IsFoldable => false;

    /// <summary>
    /// 初始化 spinner 块并立即启动 100ms 帧动画，用于表示一次进行中的 AI 动作。
    /// </summary>
    /// <param name="description">初始阶段描述文案（如「AI 正在思考…」）。</param>
    /// <param name="doc">所属对话文档，用于触发块重绘。</param>
    /// <param name="dispatcher">把动画帧推进切回 UI 线程的调度器。</param>
    public ActionSpinnerBlock(string description, ConversationDocument doc, IUiDispatcher dispatcher)
    {
        _description = description;
        _doc = doc;
        _dispatcher = dispatcher;
        StartAnimation();
    }

    /// <summary>
    /// 更新 spinner 阶段文案（如「AI 正在思考…」→「正在规划任务…」）。
    /// 必须在 UI 线程调用。
    /// </summary>
    /// <param name="description">新的阶段描述。</param>
    public void SetDescription(string description)
    {
        if (_stopped || string.Equals(_description, description, StringComparison.Ordinal)) return;
        _description = description;
        NotifyChanged();
    }

    /// <summary>
    /// 按可用宽度重排该块，spinner 恒占一行高度。
    /// </summary>
    /// <param name="width">可用列宽。</param>
    public override void Layout(int width)
    {
        base.Layout(width);
        LineCount = 1;
    }

    /// <summary>
    /// 输出一行文本：帧动画符（完成后为「✓」）+ 阶段描述 + 已耗时秒数，超宽时按列截断。
    /// </summary>
    /// <param name="lines">收集渲染行的输出列表。</param>
    /// <param name="width">可用列宽。</param>
    public override void Render(List<RenderLine> lines, int width)
    {
        var elapsed = Duration ?? (DateTime.UtcNow - StartedAtUtc);
        var frame = IsComplete ? "✓" : Frames[_frameIndex % Frames.Length];
        var text = $"{frame} {_description} · {elapsed.TotalSeconds:F1}s";

        var truncated = TextMeasure.TruncateByColumns(text, width);
        lines.Add(RenderLine.Single(truncated, BlockColors.Spinner));
    }

    /// <summary>
    /// 标记动作完成：停掉帧动画并把显示符号固定为完成态，随后通知重绘。
    /// </summary>
    public override void MarkComplete()
    {
        base.MarkComplete();
        StopAnimation();
        NotifyChanged();
    }

    /// <summary>
    /// 块从对话文档移除时停止动画计时器，避免后台线程继续触发重绘。
    /// </summary>
    public override void OnRemoved()
    {
        base.OnRemoved();
        StopAnimation();
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