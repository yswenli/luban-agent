/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： MessageItemBase
*版本号： V1.0.0.0
*唯一标识：消息项基类
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：所有消息类型的基类，包含时间戳
*
*****************************************************************************/
using CommunityToolkit.Mvvm.ComponentModel;

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// 消息项基类
/// </summary>
public abstract class MessageItemBase : ObservableObject
{
    /// <summary>
    /// 消息创建时间
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.Now;
}
