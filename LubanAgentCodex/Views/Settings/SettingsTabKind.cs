/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Views
*文件名： SettingsTabKind
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗口的可编辑配置类型（原定义在 SettingsWindow.axaml.cs 顶部，Task 19 拆出）
*
*****************************************************************************/

namespace LubanAgentCodex.Views;

/// <summary>
/// 设置窗可编辑的配置类型
/// </summary>
public enum SettingsTabKind
{
    /// <summary>技能（skills/&lt;name&gt;/SKILL.md）</summary>
    Skill,

    /// <summary>规则（rules/&lt;id&gt;.json）</summary>
    Rule,

    /// <summary>MCP 服务（mcps/&lt;name&gt;.json）</summary>
    Mcp,

    /// <summary>A2A 协作（.luban-agent/a2a/server.json + client.&lt;name&gt;.json）</summary>
    A2A,

    /// <summary>供应商（全局 config.json）</summary>
    Provider,

    /// <summary>模型（跨供应商，全局 config.json）</summary>
    Model,

    /// <summary>云效连接（外部集成：组织 ID + PAT，全局 config.json / 凭据存储）</summary>
    Yunxiao,
}