/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： ToolCallBlock
*版本号： V1.0.0.0
*唯一标识：工具执行中状态块
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Agent 工具调用，单行“工具执行中”可见状态块（参考 Claude Code）。
 *运行中显示动画 spinner + ⏳ + 友好中文动作名 + 实时耗时；完成后变为
 *✓ 动作完成（成功）或 ✗ 动作失败（失败）。工具方法名经 ToolDisplayNames
 *映射为友好中文动作名（如 GrepAsync → 全仓搜索）。替代原先“正在调用工具”
 *spinner 与静态“正在使用工具”两行冗余显示，单一状态块清晰表达执行状态。
 *
 *****************************************************************************/
using LubanAgentCli.App.ViewModels;
using LubanAgentCore.Utils;

// 消歧：全局 using 引入了 Spectre.Console.Color
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 工具执行中状态块。单行显示工具的实时执行状态：
/// <list type="bullet">
///   <item>运行中：<c>⠋ ⏳ 正在全仓搜索… · N.Ns</c>（淡黄，带动画）。</item>
///   <item>成功：<c>✓ 全仓搜索完成 · N.Ns</c>（绿色）。</item>
///   <item>失败：<c>✗ 全仓搜索失败 · N.Ns</c>（红色）。</item>
/// </list>
/// 工具名经 <see cref="ToolDisplayNames.GetAction"/> 映射为友好中文名。
/// 不可折叠，不展示参数与返回内容（参考 Claude Code 的精简提示）。
/// </summary>
public sealed class ToolCallBlock : Block
{
    private static readonly string[] Frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    private readonly ConversationDocument _doc;
    private readonly IUiDispatcher _dispatcher;
    private int _frameIndex;
    private Timer? _animationTimer;
    private volatile bool _stopped;
    private bool _failed;

    /// <summary>工具名称（框架原始方法名）。</summary>
    public string ToolName { get; }

    /// <summary>工具中文动作名（映射后的友好名称）。</summary>
    public string DisplayName { get; }

    /// <summary>工具调用 ID（MCP 协议中的 callId）。</summary>
    public string? CallId { get; set; }

    /// <summary>
    /// 初始化工具执行中状态块。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="callId">调用 ID（可选）。</param>
    /// <param name="doc">会话文档，用于动画刷新时通知重绘。</param>
    /// <param name="dispatcher">UI 线程调度器，动画帧需编组到 UI 线程。</param>
    public ToolCallBlock(string toolName, string? callId, ConversationDocument doc, IUiDispatcher dispatcher)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        DisplayName = ToolDisplayNames.GetAction(toolName);
        CallId = callId;
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
        var elapsed = Duration ?? (DateTime.UtcNow - StartedAtUtc);
        string text;
        Color color;

        if (_failed)
        {
            text = $"✗ {DisplayName}失败 · {elapsed.TotalSeconds:F1}s";
            color = BlockColors.Failure;
        }
        else if (IsComplete)
        {
            text = $"✓ {DisplayName}完成 · {elapsed.TotalSeconds:F1}s";
            color = BlockColors.Success;
        }
        else
        {
            var frame = Frames[_frameIndex % Frames.Length];
            text = $"{frame} ⏳ 正在{DisplayName}… · {elapsed.TotalSeconds:F1}s";
            color = BlockColors.ToolCall;
        }

        var truncated = TextMeasure.TruncateByColumns(text, width);
        lines.Add(RenderLine.Single(truncated, color));
    }

    /// <summary>
    /// 标记工具执行失败：停止动画并以失败样式渲染。
    /// </summary>
    /// <param name="error">失败原因（仅记录，不重复渲染消息；具体错误由 ViewModel 单独附红行）。</param>
    public void MarkFailed(string? error)
    {
        _failed = true;
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
