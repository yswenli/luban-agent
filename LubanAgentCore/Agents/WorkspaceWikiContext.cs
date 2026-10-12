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
*描述：基于 WorkspaceManager 实例的 wiki 工作区上下文实现
*
*****************************************************************************/
using LuBan.AIAgent.Wiki;

namespace LubanAgentCore.Agents;

/// <summary>会话感知的 wiki 上下文：默认取本会话绑定的工作区根，避免跨会话串扰。</summary>
public sealed class WorkspaceWikiContext(
    IWorkspaceContext workspaceContext,
    IWorkspaceManager workspaceManager) : IWikiContext
{
    /// <inheritdoc />
    public string? WorkspaceRoot => workspaceContext.RootPath;

    /// <inheritdoc />
    /// <remarks>
    /// 未指定 workspaceId 或指定为本会话工作区时返回会话根；
    /// 显式指定其他工作区时优先命中已加载缓存，未命中回退会话根（后台任务不依赖「当前工作区」）。
    /// </remarks>
    public string? WorkspaceRootFor(string? workspaceId)
    {
        if (string.IsNullOrEmpty(workspaceId)) return workspaceContext.RootPath;
        if (string.Equals(workspaceContext.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            return workspaceContext.RootPath;
        return workspaceManager.GetKnownRootPath(workspaceId) ?? workspaceContext.RootPath;
    }
}