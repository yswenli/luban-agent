/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： ActionSpinnerBlock
*版本号： V1.0.0.0
*唯一标识：对话流中的动态 spinner 块
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/17
*描述：每次 AI 动作时插入对话流，显示动画 + 阶段 + 耗时，完成后变为最终状态
*
*****************************************************************************/
using LubanAgentCli.App.ViewModels;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
    /// 对话流中的动态 spinner 块。每次动作（思考、调用工具、生成回复）插入一个，
    /// 完成后变为最终状态（"✓ 阶段 · 耗时"）。
    /// </summary>
public sealed class ActionSpinnerBlock : Block
{
    private static readonly string[] Frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    private readonly string _description;
    private readonly ConversationDocument _doc;
    private readonly IUiDispatcher _dispatcher;
    private int _frameIndex;
    private Timer? _animationTimer;
    private volatile bool _stopped;

    public override bool IsFoldable => false;

    public ActionSpinnerBlock(string description, ConversationDocument doc, IUiDispatcher dispatcher)
    {
        _description = description;
        _doc = doc;
        _dispatcher = dispatcher;
        StartAnimation();
    }

    public override void Layout(int width)
    {
        base.Layout(width);
        LineCount = 1;
    }

    public override void Render(List<RenderLine> lines, int width)
    {
        var elapsed = Duration ?? (DateTime.UtcNow - StartedAtUtc);
        var frame = IsComplete ? "✓" : Frames[_frameIndex % Frames.Length];
        var text = $"{frame} {_description} · {elapsed.TotalSeconds:F1}s";

        var truncated = TextMeasure.TruncateByColumns(text, width);
        lines.Add(RenderLine.Single(truncated, BlockColors.Spinner));
    }

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