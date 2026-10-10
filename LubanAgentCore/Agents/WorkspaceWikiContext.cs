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

/// <summary>基于 <see cref="IWorkspaceManager"/> 实例的 wiki 上下文。</summary>
public sealed class WorkspaceWikiContext(IWorkspaceManager workspaceManager) : IWikiContext
{
    /// <inheritdoc />
    public string? WorkspaceRoot => workspaceManager.CurrentWorkspace?.RootPath;

    /// <inheritdoc />
    /// <remarks>
    /// 显式指定 workspaceId 时优先解析该工作区的根（命中已加载缓存），
    /// 未指定或未命中时回退当前工作区根。后台任务因此不依赖「当前工作区」。
    /// </remarks>
    public string? WorkspaceRootFor(string? workspaceId)
    {
        var current = workspaceManager.CurrentWorkspace;
        if (string.IsNullOrEmpty(workspaceId)) return current?.RootPath;
        if (current != null && string.Equals(current.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            return current.RootPath;
        return workspaceManager.GetKnownRootPath(workspaceId) ?? current?.RootPath;
    }
}