/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： SessionClearScope
*描述：清空会话请求的范围判定——把「清全部 / 清指定工作区 / 补建落点」的规则集中成纯计算，
*      供 Web 控制器与单测共用，避免规则散落在请求处理流程里。
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 清空会话的范围判定结果。
/// </summary>
public sealed record SessionClearScope
{
    /// <summary>是否清理该用户全部工作区的会话。</summary>
    public bool ClearAll { get; init; }

    /// <summary>按工作区清理时的目标工作区；<see cref="ClearAll"/> 为 true 时为 null。</summary>
    public string? WorkspaceId { get; init; }

    /// <summary>清理后需补建空会话的工作区；null 表示不补建。</summary>
    public string? RebuildWorkspaceId { get; init; }

    /// <summary>请求指定的工作区不属于当前用户；非 null 时应按「工作区不存在」拒绝，不做任何删除。</summary>
    public string? MissingWorkspaceId { get; init; }

    /// <summary>未显式清全部且没有指定工作区；调用方应按「workspaceId 不能为空」拒绝。</summary>
    public bool WorkspaceRequired { get; init; }

    /// <summary>
    /// 按请求解析清理范围（不再依赖宿主「当前工作区」）。
    /// </summary>
    /// <remarks>
    /// 判定规则：
    /// <list type="bullet">
    /// <item>显式清全部：指定工作区只作为补建落点，未指定或不属于当前用户时不补建；</item>
    /// <item>未指定工作区：拒绝（<see cref="WorkspaceRequired"/>），不再隐式清全部；</item>
    /// <item>指定工作区：只清该区，不补建；不属于当前用户则拒绝。</item>
    /// </list>
    /// </remarks>
    /// <param name="allWorkspaces">是否显式清理全部工作区。</param>
    /// <param name="requestedWorkspaceId">请求指定的工作区（可空、可含首尾空白）。</param>
    /// <param name="workspaceExists">判断工作区是否属于当前用户。</param>
    /// <returns>范围判定结果。</returns>
    public static SessionClearScope Resolve(
        bool allWorkspaces,
        string? requestedWorkspaceId,
        Func<string, bool> workspaceExists)
    {
        var requested = string.IsNullOrWhiteSpace(requestedWorkspaceId) ? null : requestedWorkspaceId.Trim();

        if (allWorkspaces)
        {
            // 指定工作区只用于决定补建落点：必须是自己的有效工作区，否则不补建
            var rebuild = requested is not null && workspaceExists(requested) ? requested : null;
            return new SessionClearScope { ClearAll = true, RebuildWorkspaceId = rebuild };
        }

        if (requested is null)
        {
            return new SessionClearScope { WorkspaceRequired = true };
        }

        if (!workspaceExists(requested))
        {
            return new SessionClearScope { MissingWorkspaceId = requested };
        }

        return new SessionClearScope { WorkspaceId = requested };
    }
}