/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： ThinkingMessageItem
*版本号： V1.0.0.0
*唯一标识：思考过程消息项
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：AI 思考过程消息数据模型，独立显示思考内容
*
*****************************************************************************/
using CommunityToolkit.Mvvm.ComponentModel;

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// AI 思考过程消息项
/// </summary>
public partial class ThinkingMessageItem : MessageItemBase
{
    /// <summary>
    /// 思考内容
    /// </summary>
    [ObservableProperty] private string _content = "";

    /// <summary>
    /// 是否已完成
    /// </summary>
    [ObservableProperty] private bool _isComplete;

    /// <summary>
    /// 思考状态标签（流式输出时显示"思考中"，完成后显示"已思考"）
    /// </summary>
    public string ThinkingLabel => IsStreaming ? " 思考中..." : " 已思考";

    /// <summary>
    /// 是否正在流式输出
    /// </summary>
    public bool IsStreaming => !IsComplete;

    /// <summary>
    /// 追加思考内容
    /// </summary>
    public void AppendDelta(string delta)
    {
        Content += delta;
        OnPropertyChanged(nameof(Content));
    }

    partial void OnIsCompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStreaming));
        OnPropertyChanged(nameof(ThinkingLabel));
    }
}
