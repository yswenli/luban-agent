/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： McpController
*描述：MCP 设置接口（浏览器端，文件型条目）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// MCP 设置接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebMcpService"/>。
/// 与桌面端设置页一致，只暴露文件型条目（<c>mcps/&lt;name&gt;.json</c>）：不提供连接测试与工具列举。
/// </remarks>
public sealed class McpController : Controller
{
    /// <summary>列出作用域内的 MCP 服务名。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识；缺省按全局。</param>
    /// <returns>服务名数组。</returns>
    [HttpGet]
    public Task<ActionResult> List(string? scope) => WebMcpService.ListAsync(scope);

    /// <summary>读取 MCP 配置。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识。</param>
    /// <param name="key">服务名（文件名去扩展名）。</param>
    /// <returns>MCP 配置；不存在时 404。</returns>
    [HttpGet]
    public Task<ActionResult> Read(string? scope, string? key) => WebMcpService.ReadAsync(scope, key);

    /// <summary>保存 MCP 配置。</summary>
    /// <param name="payload">包装后的 <see cref="McpSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键。</returns>
    [HttpPost]
    public Task<ActionResult> Save(string payload) => WebMcpService.SaveAsync(payload);

    /// <summary>删除 MCP 配置。</summary>
    /// <param name="payload">包装后的 <see cref="SettingsKeyRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebMcpService.DeleteAsync(payload);
}