/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WorkspaceMapping
*描述：工作区实体到对外模型的统一映射
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 工作区映射：避免各服务各自复制一遍字段投影。
/// </summary>
public static class WorkspaceMapping
{
    /// <summary>把工作区实体映射为对外模型。</summary>
    /// <param name="workspace">工作区实体。</param>
    /// <returns>对外模型。</returns>
    public static WorkspaceDto ToDto(WorkspaceInfo workspace) => new(
        workspace.WorkspaceId,
        workspace.Name,
        workspace.RootPath,
        workspace.Type,
        workspace.UserId,
        workspace.ConfigPath,
        workspace.LastActiveAt,
        workspace.IsAuthorized);
}