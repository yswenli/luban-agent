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