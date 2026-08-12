/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models
*文件名： PlannedAction
*版本号： V1.0.0.0
*唯一标识：计划项模型
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Plan 模式下 Agent 规划的单个执行步骤
*
*****************************************************************************/
namespace LubanAgentCli.App.Models;

/// <summary>
/// Plan 模式下 Agent 规划的单个执行步骤。Plan 模式退出时用户逐项确认。
/// </summary>
public sealed class PlannedAction
{
    /// <summary>步骤标题。</summary>
    public string Title { get; }

    /// <summary>步骤描述。</summary>
    public string Description { get; }

    /// <summary>是否已确认执行。</summary>
    public bool Confirmed { get; set; }

    /// <summary>是否已跳过。</summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// 初始化计划项。
    /// </summary>
    /// <param name="title">步骤标题。</param>
    /// <param name="description">步骤描述。</param>
    public PlannedAction(string title, string description)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Description = description ?? string.Empty;
    }
}
