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
*描述：插件声明的进程内 MCP 客户端目录（名称 → 客户端工厂）
*
*****************************************************************************/
using LuBan.AIAgent.Connectors.AliyunDevOps;
using LuBan.AIAgent.Connectors.AliyunDevOps.Mcp;
using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Security;

namespace LubanAgentCodex.Plugins;

/// <summary>
/// 插件声明的进程内 MCP 客户端目录。
/// </summary>
/// <remarks>
/// <para>
/// 插件清单的 <c>mcpServers</c> 只声明<b>名称</b>（如 <c>yunxiao</c>），实际客户端由宿主按名称解析：
/// 进程内客户端无法用 <c>.luban-agent/mcps/*.json</c> 描述（<c>Command</c> 必填、PAT 需明文落盘，
/// PRD §4.11.3 注②），故只能由宿主内置映射。
/// </para>
/// <para>
/// <b>每次调用都新建实例</b>：<c>MCPRegistry</c> 为会话 scope（每会话一份），若把同一实例注册进
/// 多个会话，则某个会话注销时框架会 <c>Dispose</c> 它，导致其余会话持有已释放实例。按会话各持一份
/// 可彻底避免该跨会话串扰；进程内客户端构造代价可忽略。
/// </para>
/// </remarks>
internal static class PluginMcpCatalog
{
    /// <summary>
    /// 按名称创建进程内 MCP 客户端实例。
    /// </summary>
    /// <param name="name">MCP 服务器名（不区分大小写）。</param>
    /// <param name="root">根级 DI 容器（解析客户端依赖）。</param>
    /// <param name="pluginId">声明该 MCP 服务器的插件标识（用于权限环境）。</param>
    /// <param name="permissions">插件声明的权限；为空时视为无授权（默认拒绝）。</param>
    /// <returns>客户端实例；名称不是宿主内置的进程内客户端时返回 <c>null</c>。</returns>
    public static IMCPClient? Create(
        string name,
        IServiceProvider root,
        string pluginId,
        PluginPermissions? permissions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        switch (name.Trim().ToLowerInvariant())
        {
            case "yunxiao":
                try
                {
                    var connector = root.GetService<YunxiaoConnector>();
                    if (connector is null)
                    {
                        Logger.Warn($"插件声明的 MCP 服务器 'yunxiao' 不可用：YunxiaoConnector 未注册。");
                        return null;
                    }

                    // 注入权限环境：该 MCP 客户端经连接器发起的请求将按插件声明判定越界（PRD §3.7）
                    return new YunxiaoMCPClient(connector)
                    {
                        PermissionScope = new PluginPermissionScope(pluginId, permissions)
                    };
                }
                catch (Exception ex)
                {
                    Logger.Warn($"创建插件 MCP 客户端 'yunxiao' 失败。", ex);
                    return null;
                }

            default:
                Logger.Warn(
                    $"插件声明的 MCP 服务器 '{name}' 不是宿主内置的进程内客户端，" +
                    "已跳过（外部 MCP 请改用工作区 .luban-agent/mcps/*.json 配置）。");
                return null;
        }
    }
}