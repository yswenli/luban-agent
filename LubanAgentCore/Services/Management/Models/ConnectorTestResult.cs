namespace LubanAgentCore.Services.Management.Models;

/// <summary>连接器探测结果。</summary>
/// <param name="Success">凭据与端点是否可用。</param>
/// <param name="McpServerCount">连接器暴露的 MCP 服务器数量。</param>
/// <param name="Message">结果消息（失败时为原因）。</param>
public sealed record ConnectorTestResult(bool Success, int McpServerCount, string Message);