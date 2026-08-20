/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： UserMessageItem
*版本号： V1.0.0.0
*唯一标识：用户消息项
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：用户发送的消息数据模型
*
*****************************************************************************/
namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// 用户消息项
/// </summary>
public class UserMessageItem : MessageItemBase
{
    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; init; } = "";

    /// <summary>
    /// 格式化时间文本（用于 UI 绑定）
    /// </summary>
    public string TimeText => Timestamp.ToString("HH:mm");
}
