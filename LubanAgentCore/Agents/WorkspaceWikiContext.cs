/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Agents
*文件名： WorkspaceWikiContext
*版本号： V1.0.0.0
*唯一标识：dc26ed68-27d6-42b5-9830-b05fbf7aaf01
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/20
*描述：基于 WorkspaceManager.Current 的 wiki 工作区上下文实现
*
*****************************************************************************/
using LuBan.AIAgent.Wiki;

namespace LubanAgentCore.Agents;

/// <summary>基于 <see cref="WorkspaceManager.Current"/> 的 wiki 上下文。</summary>
public sealed class WorkspaceWikiContext : IWikiContext
{
    /// <inheritdoc />
    public string? WorkspaceRoot => WorkspaceManager.Current?.RootPath;
}