/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Plugins
*文件名： PluginMcpCatalog
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：插件声明的进程内 MCP 客户端解析器（名称 → 客户端，交由框架连接器派生）
*
*****************************************************************************/
using LuBan.AIAgent.Connectors;
using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Security;

namespace LubanAgentCodex.Plugins;

/// <summary>
/// 插件声明的进程内 MCP 客户端解析器。
/// </summary>
/// <remarks>
/// <para>
/// 插件清单的 <c>mcpServers</c> 只声明<b>名称</b>（如 <c>yunxiao</c>），实际客户端由框架
/// <see cref="ConnectorRegistry"/> 按「连接器暴露面」解析：进程内客户端无法用
/// <c>.luban-agent/mcps/*.json</c> 描述（<c>Command</c> 必填、PAT 需明文落盘，PRD §4.11.3 注②）。
/// </para>
/// <para>
/// 宿主<b>不再</b>按名称硬编码 <c>switch</c>：新增连接器只需在 <c>config.json:connectors</c> 声明
/// 并注册工厂（PRD §3.10 连接器配置化）。
/// </para>
/// <para>
/// <b>每次调用都新建客户端实例</b>：<c>MCPRegistry</c> 为会话 scope（每会话一份），若把同一实例注册进
/// 多个会话，则某个会话注销时框架会 <c>Dispose</c> 它，导致其余会话持有已释放实例。按会话各持一份
/// 可彻底避免该跨会话串扰；进程内客户端构造代价可忽略（连接器实例仍为宿主级单例，仅被借用）。
/// </para>
/// </remarks>
internal static class PluginMcpCatalog
{
    /// <summary>
    /// 按名称创建进程内 MCP 客户端实例。
    /// </summary>
    /// <param name="name">MCP 服务器名（不区分大小写）。</param>
    /// <param name="sessionServices">会话 scope 的服务提供者（连接器注册表为 scoped）。</param>
    /// <param name="pluginId">声明该 MCP 服务器的插件标识（用于权限环境）。</param>
    /// <param name="permissions">插件声明的权限；为空时视为无授权（默认拒绝）。</param>
    /// <returns>客户端实例；名称未由任何已声明连接器暴露时返回 <c>null</c>。</returns>
    public static IMCPClient? Create(
        string name,
        IServiceProvider sessionServices,
        string pluginId,
        PluginPermissions? permissions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (sessionServices is null)
            return null;

        var registry = sessionServices.GetService<ConnectorRegistry>();
        if (registry is null)
        {
            Logger.Warn($"插件声明的 MCP 服务器 '{name}' 不可用：ConnectorRegistry 未注册。");
            return null;
        }

        try
        {
            // 显式注入权限环境：该 MCP 客户端经连接器发起的请求将按插件声明判定越界（PRD §3.7）
            var client = registry.CreateMcpClient(name, new PluginPermissionScope(pluginId, permissions));
            if (client is null)
            {
                Logger.Warn(
                    $"插件声明的 MCP 服务器 '{name}' 未由任何已声明连接器暴露，已跳过" +
                    "（请先在 config.json:connectors 声明该连接器；外部 MCP 请改用工作区 .luban-agent/mcps/*.json 配置）。");
            }

            return client;
        }
        catch (Exception ex)
        {
            Logger.Warn($"创建插件 MCP 客户端 '{name}' 失败。", ex);
            return null;
        }
    }
}