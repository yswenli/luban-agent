/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Context
*文件名： SessionContext
*版本号： V1.0.0.0
*创建时间：2026/10/10
*描述：会话作用域上下文（scoped，只读视图）
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Abstractions.Models;

namespace LubanAgentCore.Context;

/// <summary>
/// 会话作用域上下文（scoped，只读视图）。
/// </summary>
public sealed class SessionContext(SessionContextState state) : ISessionContext
{
    /// <inheritdoc />
    public string? SessionId => state.SessionId;

    /// <inheritdoc />
    public string EffectiveRoot => state.EffectiveRoot;

    /// <inheritdoc />
    public WorktreeSession? ActiveWorktree => state.ActiveWorktree;

    /// <inheritdoc />
    public IReadOnlyList<string> AllowedRoots => state.AllowedRoots;

    /// <inheritdoc />
    public string ResolveTempDirectory()
        => state.TempDirectory
           ?? Path.Combine(state.WorkspaceRoot ?? Path.GetTempPath(), ".luban-agent", "temp");

    /// <inheritdoc />
    public bool IsWithinEffectiveRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(EffectiveRoot);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               || state.AllowedRoots.Any(r => full.StartsWith(Path.GetFullPath(r), StringComparison.OrdinalIgnoreCase));
    }
}