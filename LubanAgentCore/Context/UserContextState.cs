/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Context
*文件名： UserContextState
*版本号： V1.0.0.0
*创建时间：2026/10/13
*描述：用户作用域内可写的身份状态，由宿主/会话运行时在创建 scope 后填充
*
*****************************************************************************/
namespace LubanAgentCore.Context;

/// <summary>
/// 用户作用域内可写的身份状态，由宿主在创建 user scope 后、或 <c>SessionRuntimeManager</c>
/// 在创建 session scope 后填充。
/// </summary>
public sealed class UserContextState
{
    /// <summary>当前用户标识。</summary>
    public string? UserId { get; set; }
}