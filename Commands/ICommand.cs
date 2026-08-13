/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： ICommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：命令接口
*
*****************************************************************************/
namespace LubanAgentCli.Commands;

/// <summary>
/// 命令接口
/// </summary>
public interface ICommand
{
    /// <summary>
    /// 命令名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 命令描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 执行命令
    /// </summary>
    /// <returns>执行结果</returns>
    Task ExecuteAsync();

    /// <summary>
    /// 执行命令（带子命令和参数）
    /// </summary>
    /// <param name="args">子命令和参数</param>
    /// <returns>是否已处理（返回 false 表示不支持子命令，由主菜单处理）</returns>
    Task<bool> ExecuteAsync(string[] args);
}