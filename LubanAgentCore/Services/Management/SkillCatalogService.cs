/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： SkillCatalogService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/23
*描述：当前会话可用技能查询实现（会话工作区 + SkillRegistry 合并视图）。
*
*=================================================
*修改标记
*修改时间：2026/11/23
*修改人： yswenli
*版本号： V1.0.0.0
*描述：当前会话可用技能查询实现（会话工作区 + SkillRegistry 合并视图）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 当前会话可用技能查询实现。
/// </summary>
/// <remarks>
/// 与 Agent 构建时的口径一致：先 <see cref="SkillRegistry.LoadFromWorkspace"/>
/// （方法内部会重建合并视图，可重复调用），再读 <see cref="SkillRegistry.GetAll"/>。
/// 因此结果天然排除已禁用的内置 / 自定义技能，并包含工作区与用户级文件技能。
/// </remarks>
public sealed class SkillCatalogService(
    SkillRegistry registry,
    IWorkspaceManager workspaces,
    ISessionWorkspaceQuery sessionWorkspaceQuery,
    IUserContext userContext) : ISkillCatalogService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableSkillItem>> ListAvailableAsync(
        string? sessionId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var workspaceRoot = await ResolveWorkspaceRootAsync(sessionId, ct).ConfigureAwait(false);

        // 无工作区时只保留 config 自定义 + 内置技能（与 SkillRegistry 构造后的初始状态一致）
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            registry.LoadFromWorkspace(workspaceRoot);
        }

        var items = new List<AvailableSkillItem>();
        foreach (var skill in registry.GetAll())
        {
            items.Add(new AvailableSkillItem(
                skill.Id,
                skill.Name,
                skill.Description,
                skill.Category,
                registry.IsBuiltin(skill.Id)));
        }

        return items;
    }

    /// <summary>
    /// 解析本次查询应使用的工作区根目录：会话绑定工作区优先，缺失时回退进程当前工作区。
    /// </summary>
    private async Task<string?> ResolveWorkspaceRootAsync(string? sessionId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            try
            {
                var workspaceId = await sessionWorkspaceQuery
                    .GetSessionWorkspaceIdAsync(sessionId.Trim(), userContext.ResolveUserKey())
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(workspaceId))
                {
                    var workspace = await workspaces.GetWorkspaceAsync(workspaceId).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(workspace?.RootPath))
                    {
                        return workspace.RootPath;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 会话工作区解析失败不应让 @ 弹层整体失败：回退当前工作区
                Logger.Warn($"解析会话工作区失败（sessionId={sessionId}），回退当前工作区: {ex.Message}");
            }
        }

        ct.ThrowIfCancellationRequested();
        return workspaces.CurrentWorkspace?.RootPath;
    }
}