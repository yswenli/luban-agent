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

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// AI 助手消息项
/// </summary>
public partial class AssistantMessageItem : MessageItemBase
{
    /// <summary>
    /// 消息内容
    /// </summary>
    [ObservableProperty] private string _content = "";

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
        Content += delta;
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
    }
}
