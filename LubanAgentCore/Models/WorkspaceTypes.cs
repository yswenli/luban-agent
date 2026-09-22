/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Models
*文件名： WorkspaceTypes
*版本号： V1.0.0.0
*唯一标识：工作区类型常量
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/21
*描述：工作区类型常量，避免散落的魔法字符串
*
*****************************************************************************/
namespace LubanAgentCore.Models;

/// <summary>
/// 工作区类型常量
/// </summary>
public static class WorkspaceTypes
{
    /// <summary>
    /// 普通工作区
    /// </summary>
    public const string Normal = "Normal";

    /// <summary>
    /// LLM Wiki 知识库工作区
    /// </summary>
    public const string LlmWiki = "LlmWiki";
}