/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： SystemMessageItem
*版本号： V1.0.0.0
*唯一标识：系统消息项
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：系统消息数据模型，用于显示系统提示和错误信息
*
*****************************************************************************/
namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// 系统消息项
/// </summary>
public class SystemMessageItem : MessageItemBase
{
    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; init; } = "";

    /// <summary>
    /// 是否为错误消息
    /// </summary>
    public bool IsError { get; init; }
}
