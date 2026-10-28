/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Context
*文件名： UserContext
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：用户作用域上下文（Scoped 只读视图，身份来自 UserContextState）
*
*****************************************************************************/

namespace LubanAgentCore.Context;

/// <summary>
/// 用户作用域上下文（Scoped 只读视图），身份来自可写的 <see cref="UserContextState"/>。
/// </summary>
public sealed class UserContext(UserContextState state) : IUserContext
{
    /// <inheritdoc />
    public string? UserId => state.UserId;
}