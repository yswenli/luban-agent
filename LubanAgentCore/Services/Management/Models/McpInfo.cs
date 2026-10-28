/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： McpInfo
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：MCP 客户端展示信息（内置 / 配置 / 插件注册统一视图）。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：MCP 客户端展示信息（内置 / 配置 / 插件注册统一视图）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management.Models;

/// <summary>MCP 客户端展示信息（内置 / 配置 / 插件注册统一视图）。</summary>
/// <param name="Name">客户端名称。</param>
/// <param name="Description">描述。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="IsBuiltin">是否内置客户端。</param>
/// <param name="IsConnected">当前是否已连接。</param>
/// <param name="IsPlugin">是否由插件注册（运行期注册，不落配置）。</param>
public sealed record McpInfo(
    string Name,
    string Description,
    bool IsEnabled,
    bool IsBuiltin,
    bool IsConnected,
    bool IsPlugin);