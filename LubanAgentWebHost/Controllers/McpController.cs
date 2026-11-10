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
/// 与桌面端设置页一致，只暴露文件型条目（<c>mcps/&lt;name&gt;.json</c>）：不提供连接测试与工具列举。
/// </remarks>
public sealed class McpController : Controller
{
    /// <summary>列出作用域内的 MCP 服务名。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识；缺省按全局。</param>
    /// <returns>服务名数组。</returns>
    [HttpGet]
    public Task<ActionResult> List(string? scope) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var ids = requestScope.GetRequiredService<IMcpManagementService>().ListFileIds(root);
        return WebApi.OkJson(new { items = ids });
    });

    /// <summary>读取 MCP 配置。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识。</param>
    /// <param name="key">服务名（文件名去扩展名）。</param>
    /// <returns>MCP 配置；不存在时 404。</returns>
    [HttpGet]
    public Task<ActionResult> Read(string? scope, string? key) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return WebApi.FailJson(400, "key 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var config = requestScope.GetRequiredService<IMcpManagementService>().ReadFile(key.Trim(), root);
        return config is null ? WebApi.FailJson(404, "MCP 配置不存在") : WebApi.OkJson(ToDto(config));
    });

    /// <summary>保存 MCP 配置。</summary>
    /// <param name="payload">包装后的 <see cref="McpSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键。</returns>
    [HttpPost]
    public Task<ActionResult> Save(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<McpSaveRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return WebApi.FailJson(400, "服务名不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var name = input.Name.Trim();
        var transport = string.IsNullOrWhiteSpace(input.Transport) ? "stdio" : input.Transport.Trim().ToLowerInvariant();
        var target = input.Target.Trim();

        var service = requestScope.GetRequiredService<IMcpManagementService>();
        // 表单不含 headers / environment：更新已有条目时沿用文件里的原值，避免保存一次就丢掉它们。
        var previous = string.IsNullOrWhiteSpace(input.OriginalId) ? null : service.ReadFile(input.OriginalId.Trim(), root);
        service.SaveFile(
            input.OriginalId,
            new McpServerConfig
            {
                Name = name,
                Description = input.Description,
                Transport = transport,
                Command = transport == "stdio" ? target : "",
                Url = transport == "stdio" ? "" : target,
                Args = input.Args is null ? new List<string>() : input.Args.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList(),
                Enabled = input.Enabled,
                Headers = previous?.Headers ?? new Dictionary<string, string>(),
                Environment = previous?.Environment ?? new Dictionary<string, string>(),
            },
            root);
        return WebApi.OkJson(new SettingsKeyResult(name));
    });

    /// <summary>删除 MCP 配置。</summary>
    /// <param name="payload">包装后的 <see cref="SettingsKeyRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SettingsKeyRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.Key))
        {
            return WebApi.FailJson(400, "key 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        await requestScope.GetRequiredService<IMcpManagementService>()
            .DeleteAsync(input.Key.Trim(), ManagedOrigin.File, root)
            .ConfigureAwait(false);
        return WebApi.OkJson();
    });

    private static McpFileDto ToDto(McpServerConfig config)
    {
        var transport = string.IsNullOrWhiteSpace(config.Transport) ? "stdio" : config.Transport.Trim().ToLowerInvariant();
        var target = transport == "stdio" ? config.Command : config.Url;
        if (string.IsNullOrWhiteSpace(target) && config.Args.Count > 0)
        {
            // 历史写法把 URL 塞进了 Args[0]
            target = config.Args[0];
        }

        return new McpFileDto(
            config.Name,
            config.Description,
            transport,
            target ?? "",
            transport == "stdio" ? config.Args : Array.Empty<string>(),
            config.Enabled);
    }
}