/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： AssistantMessageItem
*版本号： V1.0.0.0
*唯一标识：AI 助手消息项
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：AI 助手消息数据模型，支持流式追加和思考内容
*
*****************************************************************************/
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text;

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// AI 助手消息项
/// </summary>
public partial class AssistantMessageItem : MessageItemBase
{
    // 流式正文字符串由 StringBuilder 累积，避免 `Content += delta` 每次冲刷
    // 都重新分配整串（O(n²) 分配 + 触发 TextBox 全文重排）。
    private readonly StringBuilder _contentBuilder = new();
    private string _content = "";

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content
    {
        get => _content;
        set
        {
            var v = value ?? "";
            if (string.Equals(_content, v, StringComparison.Ordinal)) return;
            _content = v;
            _contentBuilder.Clear();
            _contentBuilder.Append(v);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 思考内容
    /// </summary>
    [ObservableProperty] private string _thinking = "";

    /// <summary>
    /// 是否已完成
    /// </summary>
    [ObservableProperty] private bool _isComplete;

    /// <summary>
    /// 是否有思考内容
    /// </summary>
    public bool HasThinking => !string.IsNullOrEmpty(Thinking);

    /// <summary>
    /// 思考状态标签（流式输出时显示"思考中"，完成后显示"已思考"）
    /// </summary>
    public string ThinkingLabel => IsStreaming ? " 思考中..." : " 已思考";

    /// <summary>
    /// 是否正在流式输出
    /// </summary>
    public bool IsStreaming => !IsComplete;

    /// <summary>
    /// 思考内容别名（用于绑定）
    /// </summary>
    public string ThinkingContent => Thinking;

    /// <summary>
    /// 追加文本内容
    /// </summary>
    public void AppendDelta(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;
        _contentBuilder.Append(delta);
        _content = _contentBuilder.ToString();
        OnPropertyChanged(nameof(Content));
    }

    /// <summary>
    /// 追加思考内容
    /// </summary>
    public void AppendThinking(string delta)
    {
        Thinking += delta;
        OnPropertyChanged(nameof(Thinking));
        OnPropertyChanged(nameof(HasThinking));
        OnPropertyChanged(nameof(ThinkingContent));
    }

    partial void OnIsCompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStreaming));
        OnPropertyChanged(nameof(ThinkingLabel));
    }
}
