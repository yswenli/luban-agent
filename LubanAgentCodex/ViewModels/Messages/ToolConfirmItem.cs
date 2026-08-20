/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： ToolConfirmItem
*版本号： V1.0.0.0
*唯一标识：工具确认消息项
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工具确认消息数据模型，用于需要用户确认的工具调用
*
*****************************************************************************/
using LubanAgentCore.Models;

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// 工具确认消息项
/// </summary>
public class ToolConfirmItem : MessageItemBase
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; init; } = "";

    /// <summary>
    /// 工具参数
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>
    /// 用户响应回调
    /// </summary>
    public required Action<ConfirmResult> OnRespond { get; init; }
}
