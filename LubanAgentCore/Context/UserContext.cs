/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Context
*文件名： UserContext
*版本号： V1.0.0.0
*创建时间：2026/10/10
*描述：用户作用域上下文（root）
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCore.Context;

/// <summary>
/// 用户作用域上下文（root）。
/// </summary>
public sealed class UserContext(string? userId) : IUserContext
{
    /// <inheritdoc />
    public string? UserId { get; } = userId;
}