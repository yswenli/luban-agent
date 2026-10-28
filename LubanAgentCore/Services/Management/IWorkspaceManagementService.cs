/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： IWorkspaceManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：工作区管理。 方法面 = 现有 <c>/work</c> 命令与 Codex 工作区面板的能力，不新增能力。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：工作区管理。 方法面 = 现有 <c>/work</c> 命令与 Codex 工作区面板的能力，不新增能力。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 工作区管理。
/// 方法面 = 现有 <c>/work</c> 命令与 Codex 工作区面板的能力，不新增能力。
/// </summary>
public interface IWorkspaceManagementService
{
    /// <summary>当前用户的全部工作区。</summary>
    Task<IReadOnlyList<WorkspaceInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>当前活动工作区；未初始化时为 null。</summary>
    Task<WorkspaceInfo?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>创建工作区（<paramref name="type"/> 见 <see cref="WorkspaceTypes"/>）。</summary>
    /// <param name="name">显示名；为 null 时按工作区类型取默认名。</param>
    /// <param name="rootPath">根目录绝对路径。</param>
    /// <param name="type">工作区类型。</param>
    /// <param name="ct">取消令牌。</param>
    Task<WorkspaceInfo> CreateAsync(string? name, string rootPath, string type, CancellationToken ct = default);

    /// <summary>删除工作区记录（不删除磁盘文件）。</summary>
    Task DeleteAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>切换当前工作区。</summary>
    Task SwitchAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>确保工作区已授权（首次使用时写授权标记）。</summary>
    Task GrantAccessAsync(string workspaceId, CancellationToken ct = default);
}