/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： NodeActivityDisplayItem
*版本号： V1.0.0.0
*唯一标识：节点活动展示项
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/16
*描述：子代理节点活动（思考/正文/工具调用/工具结果）的展示模型
*
*****************************************************************************/
using LuBan.AIAgent.Orchestration.Models;

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// 节点活动展示项：把 <see cref="NodeActivityItem"/> 转成可直接绑定的展示模型，
/// 连续的同类型增量（思考/正文）在追加时合并，避免刷屏。
/// </summary>
public partial class NodeActivityDisplayItem : ObservableObject
{
    private static readonly IBrush ThinkingBrush = new SolidColorBrush(Color.Parse("#AFA9EC"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#D3D1C7"));
    private static readonly IBrush ToolCallBrush = new SolidColorBrush(Color.Parse("#FAC775"));
    private static readonly IBrush ToolResultBrush = new SolidColorBrush(Color.Parse("#5DCAA5"));
    private static readonly FontFamily MonospaceFont = new("Consolas,Menlo,monospace");

    /// <summary>
    /// 活动类型
    /// </summary>
    public NodeActivityKind Kind { get; init; }

    /// <summary>
    /// 行首标题（如 "▾ 思考"、"→ Grep"）
    /// </summary>
    public string Header { get; init; } = "";

    /// <summary>
    /// 是否有行首标题
    /// </summary>
    public bool HasHeader => !string.IsNullOrEmpty(Header);

    /// <summary>
    /// 前景色
    /// </summary>
    public IBrush Foreground { get; init; } = TextBrush;

    /// <summary>
    /// 是否使用等宽字体（工具参数/结果）
    /// </summary>
    public bool IsMonospace { get; init; }

    /// <summary>
    /// 展示字体
    /// </summary>
    public FontFamily FontFamily => IsMonospace ? MonospaceFont : FontFamily.Default;

    /// <summary>
    /// 是否斜体（思考内容）
    /// </summary>
    public bool IsItalic { get; init; }

    /// <summary>
    /// 展示字形
    /// </summary>
    public FontStyle FontStyle => IsItalic ? FontStyle.Italic : FontStyle.Normal;

    /// <summary>
    /// 是否可与同类型相邻增量合并
    /// </summary>
    public bool Mergeable { get; init; }

    /// <summary>
    /// 活动内容
    /// </summary>
    [ObservableProperty] private string _content = "";

    /// <summary>
    /// 追加增量内容（流式合并）
    /// </summary>
    /// <param name="delta">增量文本。</param>
    public void AppendContent(string delta)
    {
        Content += delta;
    }

    /// <summary>
    /// 由框架活动条目创建展示项
    /// </summary>
    /// <param name="activity">框架活动条目。</param>
    /// <returns>展示项。</returns>
    public static NodeActivityDisplayItem From(NodeActivityItem activity)
    {
        return activity.Kind switch
        {
            NodeActivityKind.Thinking => new NodeActivityDisplayItem
            {
                Kind = activity.Kind,
                Header = "▾ 思考",
                Foreground = ThinkingBrush,
                IsItalic = true,
                Mergeable = true,
                Content = activity.Content ?? ""
            },
            NodeActivityKind.Text => new NodeActivityDisplayItem
            {
                Kind = activity.Kind,
                Header = "正文",
                Foreground = TextBrush,
                Mergeable = true,
                Content = activity.Content ?? ""
            },
            NodeActivityKind.ToolCall => new NodeActivityDisplayItem
            {
                Kind = activity.Kind,
                Header = $"→ {activity.ToolName ?? "工具"}",
                Foreground = ToolCallBrush,
                IsMonospace = true,
                Content = activity.Content ?? ""
            },
            _ => new NodeActivityDisplayItem
            {
                Kind = activity.Kind,
                Header = "  ↳ 结果",
                Foreground = ToolResultBrush,
                IsMonospace = true,
                Content = string.IsNullOrEmpty(activity.Content) ? "(无返回)" : activity.Content
            }
        };
    }
}