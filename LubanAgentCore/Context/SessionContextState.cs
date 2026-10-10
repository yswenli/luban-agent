/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Context
*文件名： SessionContextState
*版本号： V1.0.0.0
*创建时间：2026/10/10
*描述：会话作用域内可写的上下文状态，由 SessionRuntimeManager 在创建 scope 后填充
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions.Models;

namespace LubanAgentCore.Context;

/// <summary>
/// 会话作用域内可写的上下文状态，由 SessionRuntimeManager 在创建 scope 后填充。
/// </summary>
public sealed class SessionContextState
{
    /// <summary>
    /// 会话标识。
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 工作区标识。
    /// </summary>
    public string? WorkspaceId { get; set; }

    /// <summary>
    /// 工作区根路径。
    /// </summary>
    public string? WorkspaceRoot { get; set; }

    /// <summary>
    /// 活动 worktree（若已进入隔离）。
    /// </summary>
    public WorktreeSession? ActiveWorktree { get; set; }

    /// <summary>
    /// 允许访问的根路径集合。
    /// </summary>
    public List<string> AllowedRoots { get; } = new();

    /// <summary>
    /// 会话临时目录（可覆盖）。
    /// </summary>
    public string? TempDirectory { get; set; }

    /// <summary>
    /// 有效根：优先 worktree，其次工作区根。
    /// </summary>
    public string EffectiveRoot
        => ActiveWorktree?.WorktreePath
           ?? WorkspaceRoot
           ?? throw new InvalidOperationException("会话未设置工作区根或 worktree");
}