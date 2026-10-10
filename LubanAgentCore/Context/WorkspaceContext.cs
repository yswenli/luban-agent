/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Context
*文件名： WorkspaceContext
*版本号： V1.0.0.0
*创建时间：2026/10/10
*描述：工作区作用域上下文（P0 由会话作用域承载）
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCore.Context;

/// <summary>
/// 工作区作用域上下文（P0 由会话作用域承载）。
/// </summary>
public sealed class WorkspaceContext(SessionContextState state) : IWorkspaceContext
{
    /// <inheritdoc />
    public string? WorkspaceId => state.WorkspaceId;

    /// <inheritdoc />
    public string? RootPath => state.WorkspaceRoot;

    /// <inheritdoc />
    public string? RootPathFor(string? workspaceId) => state.WorkspaceRoot;
}