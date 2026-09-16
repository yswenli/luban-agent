/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： OrchestrationNodeItem
*版本号： V1.0.0.0
*唯一标识：编排节点消息项
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/16
*描述：自动编排子代理节点消息项，承载节点状态与其内部活动时间线
*
*****************************************************************************/
using LuBan.AIAgent.Orchestration.Models;

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// 编排子代理节点消息项：主视图显示一行节点卡片，点击可打开详情窗口查看
/// 该节点的思考/工具调用/结果完整时间线。
/// </summary>
public partial class OrchestrationNodeItem : MessageItemBase
{
    private const int MaxActivities = 500;

    /// <summary>
    /// 节点标识
    /// </summary>
    public string NodeId { get; init; } = "";

    /// <summary>
    /// 节点描述
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// 节点活动时间线
    /// </summary>
    public ObservableCollection<NodeActivityDisplayItem> Activities { get; } = new();

    /// <summary>
    /// 是否已完成
    /// </summary>
    [ObservableProperty] private bool _isComplete;

    /// <summary>
    /// 是否失败
    /// </summary>
    [ObservableProperty] private bool _isFailed;

    /// <summary>
    /// 是否因关键前驱失败被跳过
    /// </summary>
    [ObservableProperty] private bool _isSkipped;

    /// <summary>
    /// 耗时秒数
    /// </summary>
    [ObservableProperty] private double _elapsedSeconds;

    /// <summary>
    /// 失败原因
    /// </summary>
    [ObservableProperty] private string? _error;

    /// <summary>
    /// 开始时间（UTC）
    /// </summary>
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 状态图标
    /// </summary>
    public string StatusIcon => IsFailed ? "✗" : IsSkipped ? "−" : IsComplete ? "✓" : "▶";

    /// <summary>
    /// 状态文本
    /// </summary>
    public string StatusText => IsFailed
        ? $"失败 · {ElapsedSeconds:F1}s"
        : IsSkipped
            ? "已跳过（前驱失败）"
            : IsComplete
                ? $"完成 · {ElapsedSeconds:F1}s"
                : "运行中";

    /// <summary>
    /// 详情提示
    /// </summary>
    public string DetailHint => Activities.Count > 0 ? $"[{Activities.Count} 项活动 · 点击查看详情]" : "";

    /// <summary>
    /// 是否有活动
    /// </summary>
    public bool HasActivities => Activities.Count > 0;

    /// <summary>
    /// 追加一条节点活动；连续同类增量（思考/正文）合并到上一条
    /// </summary>
    /// <param name="activity">框架活动条目。</param>
    public void AppendActivity(NodeActivityItem activity)
    {
        var display = NodeActivityDisplayItem.From(activity);

        if (display.Mergeable && Activities.Count > 0)
        {
            var last = Activities[^1];
            if (last.Kind == display.Kind)
            {
                last.AppendContent(display.Content);
                return;
            }
        }

        Activities.Add(display);

        if (Activities.Count > MaxActivities)
            Activities.RemoveAt(0);

        OnPropertyChanged(nameof(DetailHint));
        OnPropertyChanged(nameof(HasActivities));
    }

    partial void OnIsCompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsSkippedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnElapsedSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(StatusText));
    }
}