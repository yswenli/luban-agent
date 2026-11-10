/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： SettingsScope
*描述：设置页作用域（全局 / 工作区）到「文件根目录」的解析
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 设置页作用域解析。
/// </summary>
/// <remarks>
/// 设置页的四个文件型页签（技能 / 规则 / MCP / A2A）与桌面端一致：全局作用域写
/// <c>~/.luban-agent/*</c>，工作区作用域写 <c>&lt;workspace&gt;/.luban-agent/*</c>。
/// 各管理服务用 <c>workspaceRoot == null</c> 表示全局，故这里只在工作区作用域下返回根路径。
/// </remarks>
public static class SettingsScope
{
    /// <summary>全局作用域的字面值。</summary>
    public const string Global = "global";

    /// <summary>
    /// 解析作用域。
    /// </summary>
    /// <param name="workspaces">工作区管理器。</param>
    /// <param name="scope"><c>global</c>（或空）或工作区标识。</param>
    /// <returns>成功时 <c>Root</c> 为工作区根路径（全局为 null）、<c>Error</c> 为 null；失败时只给 <c>Error</c>。</returns>
    public static async Task<(string? Root, ActionResult? Error)> ResolveAsync(IWorkspaceManager workspaces, string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)
            || string.Equals(scope.Trim(), Global, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var workspace = await workspaces.GetWorkspaceAsync(scope.Trim()).ConfigureAwait(false);
        if (workspace is null)
        {
            return (null, WebApi.FailJson(404, "工作区不存在"));
        }

        return (workspace.RootPath, null);
    }

    /// <summary>
    /// 解析要求必须是工作区的作用域（A2A 页）。
    /// </summary>
    /// <param name="workspaces">工作区管理器。</param>
    /// <param name="scope">工作区标识。</param>
    /// <returns>成功时 <c>Root</c> 为工作区根路径；全局作用域返回提示错误。</returns>
    public static async Task<(string? Root, ActionResult? Error)> ResolveWorkspaceOnlyAsync(
        IWorkspaceManager workspaces,
        string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)
            || string.Equals(scope.Trim(), Global, StringComparison.OrdinalIgnoreCase))
        {
            return (null, WebApi.FailJson(400, "A2A 配置仅作用于工作区，请先在顶栏选择一个工作区"));
        }

        return await ResolveAsync(workspaces, scope).ConfigureAwait(false);
    }
}