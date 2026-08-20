/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models
*文件名： PlanExitAction
*版本号： V1.0.0.0
*唯一标识：Plan 模式退出动作枚举
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Plan 模式退出时用户的选择
*
*****************************************************************************/
namespace LubanAgentCli.App.Models;

/// <summary>
/// Plan 模式退出时用户的选择动作。
/// </summary>
public enum PlanExitAction
{
    /// <summary>执行全部计划项，切换到 Default 模式逐个确认。</summary>
    ExecuteAll,

    /// <summary>放弃所有计划项。</summary>
    Discard,

    /// <summary>逐项确认，每项选择执行或跳过。</summary>
    ReviewEach
}
