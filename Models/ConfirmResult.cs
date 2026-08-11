/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models
*文件名： ConfirmResult
*版本号： V1.0.0.0
*唯一标识：确认结果枚举
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：确认块用户选择的结果
*
*****************************************************************************/
namespace LubanAgent.Models;

/// <summary>
/// 工具确认块中用户的选择结果。
/// </summary>
public enum ConfirmResult
{
    /// <summary>允许本次调用。</summary>
    Allow,

    /// <summary>拒绝本次调用。</summary>
    Deny,

    /// <summary>本轮（当前 agent 交互回合内）后续同类工具调用全部允许，免确认。</summary>
    AllowAll
}
