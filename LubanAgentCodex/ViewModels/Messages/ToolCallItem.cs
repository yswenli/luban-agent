/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： ToolCallItem
*版本号： V1.0.0.0
*唯一标识：工具调用消息项
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工具调用消息数据模型，包含工具名称、参数和执行状态
*
*****************************************************************************/
using CommunityToolkit.Mvvm.ComponentModel;

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// 工具调用消息项
/// </summary>
public partial class ToolCallItem : MessageItemBase
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; init; } = "";

    /// <summary>
    /// 调用ID
    /// </summary>
    public string? CallId { get; init; }

    /// <summary>
    /// 工具参数
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>
    /// 执行状态
    /// </summary>
    [ObservableProperty] private ToolCallState _state;

    /// <summary>
    /// 错误信息
    /// </summary>
    [ObservableProperty] private string? _errorMessage;
}

/// <summary>
/// 工具调用状态
/// </summary>
public enum ToolCallState
{
    /// <summary>运行中</summary>
    Running,
    /// <summary>已完成</summary>
    Done,
    /// <summary>失败</summary>
    Failed
}
