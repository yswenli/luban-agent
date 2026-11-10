/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： A2AController
*描述：A2A 协作接口（运行状态只读 + 工作区文件读写）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// A2A 协作接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebA2AService"/>。
/// </remarks>
public sealed class A2AController : Controller
{
    /// <summary>读取 A2A 运行状态。</summary>
    /// <returns>状态快照。</returns>
    [HttpGet]
    public Task<ActionResult> Status() => WebA2AService.StatusAsync();

    /// <summary>读取工作区服务端配置。</summary>
    /// <param name="scope">工作区标识。</param>
    /// <returns>服务端配置；文件不存在时返回默认值。</returns>
    [HttpGet]
    public Task<ActionResult> GetServer(string? scope) => WebA2AService.GetServerAsync(scope);

    /// <summary>保存工作区服务端配置。</summary>
    /// <param name="payload">包装后的 <see cref="A2AServerSaveRequest"/> JSON。</param>
    /// <returns>固定键 <c>server</c>。</returns>
    [HttpPost]
    public Task<ActionResult> SaveServer(string payload) => WebA2AService.SaveServerAsync(payload);

    /// <summary>列出工作区客户端配置。</summary>
    /// <param name="scope">工作区标识。</param>
    /// <returns>客户端列表项。</returns>
    [HttpGet]
    public Task<ActionResult> ListClients(string? scope) => WebA2AService.ListClientsAsync(scope);

    /// <summary>读取单个客户端配置。</summary>
    /// <param name="scope">工作区标识。</param>
    /// <param name="key">客户端文件名。</param>
    /// <returns>客户端配置；不存在时 404。</returns>
    [HttpGet]
    public Task<ActionResult> GetClient(string? scope, string? key) => WebA2AService.GetClientAsync(scope, key);

    /// <summary>保存客户端配置（改名时删除旧文件）。</summary>
    /// <param name="payload">包装后的 <see cref="A2AClientSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键。</returns>
    [HttpPost]
    public Task<ActionResult> SaveClient(string payload) => WebA2AService.SaveClientAsync(payload);

    /// <summary>删除 A2A 配置（服务端固定文件名 <c>server.json</c>）。</summary>
    /// <param name="payload">包装后的 <see cref="SettingsKeyRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    [HttpPost]
    public Task<ActionResult> DeleteClient(string payload) => WebA2AService.DeleteClientAsync(payload);
}