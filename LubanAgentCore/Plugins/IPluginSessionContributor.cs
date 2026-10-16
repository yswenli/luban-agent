/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Plugins
*文件名： IPluginSessionContributor
*版本号： V1.0.0.0
*唯一标识：新建
*创建时间：2026/10/15
*描述：插件对会话级组件的贡献回放（PRD §3.11）
*
*****************************************************************************/

namespace LubanAgentCore.Plugins;

/// <summary>
/// 插件会话级贡献回放器：把插件贡献应用到**每个会话**的会话级注册表。
/// </summary>
/// <remarks>
/// <para>
/// 插件程序集与清单是<b>应用级</b>，而插件贡献的 Skill / MCP 是<b>工作区级</b>（PRD §3.3）。
/// Skill 走文件式（<c>.luban-agent/skills</c>），新会话经 <c>SkillRegistry.LoadFromWorkspace</c>
/// 天然可见；但进程内 MCP 客户端（如 <c>YunxiaoMCPClient</c>）无法用文件描述，且
/// <c>MCPRegistry</c> 为会话 scope 注册（每会话一份），故必须在每次会话构建 Agent 时回放。
/// </para>
/// <para>
/// 宿主（如 LubanAgentCodex）以<b>单例</b>注册本接口实现；框架在会话 scope 内解析并按需调用，
/// 实现内部自行决定贡献内容与目标注册表。
/// </para>
/// </remarks>
public interface IPluginSessionContributor
{
    /// <summary>
    /// 回放插件贡献到指定会话的注册表（在 Agent 构建前调用，可重复调用）。
    /// </summary>
    /// <param name="sessionServices">会话 scope 的服务提供者（含该会话的 <c>MCPRegistry</c> / <c>SkillRegistry</c>）。</param>
    void Contribute(IServiceProvider sessionServices);
}