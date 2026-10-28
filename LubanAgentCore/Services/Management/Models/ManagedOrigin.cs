/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： ManagedOrigin
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：管理项的来源（决定写回哪个后端）。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：管理项的来源（决定写回哪个后端）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management.Models;

/// <summary>
/// 管理项的来源（决定写回哪个后端）。
/// </summary>
public enum ManagedOrigin
{
    /// <summary>硬编码内置项（DI 注册），只能启停，不能改内容。</summary>
    Builtin,

    /// <summary>
    /// 文件型：<c>~/.luban-agent/skills|rules</c>（全局）或
    /// <c>&lt;工作区&gt;/.luban-agent/skills|rules</c>（工作区）。
    /// </summary>
    File,

    /// <summary>config.json 自定义项（<c>CustomSkills</c> / <c>CustomRules</c>）。</summary>
    Config,
}