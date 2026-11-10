/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebA2AService
*描述：A2A 协作业务（运行状态只读 + 工作区文件读写）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// A2A 协作业务。
/// </summary>
/// <remarks>
/// <para>
/// A2A 运行时随宿主进程启动与退出，客户端不提供启停，故运行状态只有只读。
/// </para>
/// <para>
/// 配置部分是工作区文件（<c>.luban-agent/a2a/*.json</c>）的读写，与桌面端设置页共用
/// <see cref="A2AWorkspaceConfigStore"/>；全局作用域不支持。
/// </para>
/// </remarks>
public static class WebA2AService
{
    /// <summary>读取 A2A 运行状态。</summary>
    /// <returns>状态快照。</returns>
    public static Task<ActionResult> StatusAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var status = await scope.GetRequiredService<IA2AStatusProvider>()
            .GetStatusAsync()
            .ConfigureAwait(false);
        return WebApi.OkJson(status);
    });

    /// <summary>读取工作区服务端配置。</summary>
    /// <param name="scope">工作区标识。</param>
    /// <returns>服务端配置；文件不存在时返回默认值。</returns>
    public static Task<ActionResult> GetServerAsync(string? scope) => WebApi.RunGuardedAsync(async () =>
    {
        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveWorkspaceOnlyAsync(requestScope.GetRequiredService<IWorkspaceManager>(), scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var file = A2AWorkspaceConfigStore.LoadServer(root!);
        return WebApi.OkJson(file is null
            ? new A2AServerDto("127.0.0.1", 0, "/a2a", true, Array.Empty<string>(), null, null, null)
            : ToServerDto(file));
    });

    /// <summary>保存工作区服务端配置。</summary>
    /// <param name="payload">包装后的 <see cref="A2AServerSaveRequest"/> JSON。</param>
    /// <returns>固定键 <c>server</c>。</returns>
    public static Task<ActionResult> SaveServerAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<A2AServerSaveRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveWorkspaceOnlyAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var file = A2AWorkspaceConfigStore.LoadServer(root!) ?? new A2AWorkspaceFile();
        file.Mode = "server";
        file.Host = string.IsNullOrWhiteSpace(input.Host) ? "127.0.0.1" : input.Host.Trim();
        file.Port = input.Port ?? 0;
        file.Path = string.IsNullOrWhiteSpace(input.Path) ? "/a2a" : input.Path.Trim();
        file.RequireApiKey = input.RequireApiKey;
        file.ApiKeys = input.ApiKeys is null
            ? new List<string>()
            : input.ApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();
        file.AgentName = string.IsNullOrWhiteSpace(input.AgentName) ? null : input.AgentName.Trim();
        file.AgentDescription = string.IsNullOrWhiteSpace(input.AgentDescription) ? null : input.AgentDescription.Trim();
        file.AgentVersion = string.IsNullOrWhiteSpace(input.AgentVersion) ? "1.0.0" : input.AgentVersion.Trim();

        A2AWorkspaceConfigStore.SaveServer(root!, file);
        return WebApi.OkJson(new SettingsKeyResult(A2AWorkspaceConfigStore.ServerFileName));
    });

    /// <summary>列出工作区客户端配置。</summary>
    /// <param name="scope">工作区标识。</param>
    /// <returns>客户端列表项。</returns>
    public static Task<ActionResult> ListClientsAsync(string? scope) => WebApi.RunGuardedAsync(async () =>
    {
        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveWorkspaceOnlyAsync(requestScope.GetRequiredService<IWorkspaceManager>(), scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var items = A2AWorkspaceConfigStore.LoadClients(root!)
            .Select(pair => new A2AClientListItemDto(pair.FileName, pair.Config.Name, pair.Config.Endpoint))
            .ToList();
        return WebApi.OkJson(new { items });
    });

    /// <summary>读取单个客户端配置。</summary>
    /// <param name="scope">工作区标识。</param>
    /// <param name="key">客户端文件名。</param>
    /// <returns>客户端配置；不存在时 404。</returns>
    public static Task<ActionResult> GetClientAsync(string? scope, string? key) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return WebApi.FailJson(400, "key 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveWorkspaceOnlyAsync(requestScope.GetRequiredService<IWorkspaceManager>(), scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var matches = A2AWorkspaceConfigStore.LoadClients(root!)
            .Where(x => string.Equals(x.FileName, key.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            return WebApi.FailJson(404, "A2A 客户端配置不存在");
        }

        var pair = matches[0];

        return WebApi.OkJson(new A2AClientDto(
            pair.FileName,
            pair.Config.Name,
            pair.Config.Endpoint,
            pair.Config.ApiKey,
            pair.Config.RegisterAsSubAgent == true,
            pair.Config.RemoteAgentName,
            pair.Config.AllowAll == true));
    });

    /// <summary>保存客户端配置（改名时删除旧文件）。</summary>
    /// <param name="payload">包装后的 <see cref="A2AClientSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键。</returns>
    public static Task<ActionResult> SaveClientAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<A2AClientSaveRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        var name = input.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return WebApi.FailJson(400, "客户端名称不能为空");
        }

        var endpoint = input.Endpoint.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return WebApi.FailJson(400, "远端地址不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveWorkspaceOnlyAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var targetFile = A2AWorkspaceConfigStore.BuildClientFileName(name);
        var oldKey = string.IsNullOrWhiteSpace(input.OldKey) ? null : input.OldKey.Trim();
        if (oldKey is not null
            && !string.Equals(oldKey, targetFile, StringComparison.OrdinalIgnoreCase)
            && System.IO.File.Exists(Path.Combine(A2AWorkspaceConfigStore.ResolveDir(root!), targetFile)))
        {
            return WebApi.FailJson(409, $"已存在同名客户端文件 {targetFile}，请更换名称");
        }

        var file = (oldKey is null
            ? null
            : A2AWorkspaceConfigStore.LoadClients(root!)
                .FirstOrDefault(x => string.Equals(x.FileName, oldKey, StringComparison.OrdinalIgnoreCase)).Config)
            ?? new A2AWorkspaceFile();

        file.Mode = "client";
        file.Name = name;
        file.Endpoint = endpoint;
        file.ApiKey = string.IsNullOrWhiteSpace(input.ApiKey) ? null : input.ApiKey.Trim();
        file.RegisterAsSubAgent = input.RegisterAsSubAgent;
        file.RemoteAgentName = string.IsNullOrWhiteSpace(input.RemoteAgentName) ? null : input.RemoteAgentName.Trim();
        file.AllowAll = input.AllowAll;

        A2AWorkspaceConfigStore.SaveClient(root!, targetFile, file);
        if (oldKey is not null && !string.Equals(oldKey, targetFile, StringComparison.OrdinalIgnoreCase))
        {
            A2AWorkspaceConfigStore.DeleteFile(root!, oldKey);
        }

        return WebApi.OkJson(new SettingsKeyResult(targetFile));
    });

    /// <summary>删除 A2A 配置（服务端固定文件名 <c>server.json</c>）。</summary>
    /// <param name="payload">包装后的 <see cref="SettingsKeyRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    public static Task<ActionResult> DeleteClientAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
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
            .ResolveWorkspaceOnlyAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var key = input.Key.Trim();
        var fileName = string.Equals(key, "server", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, A2AWorkspaceConfigStore.ServerFileName, StringComparison.OrdinalIgnoreCase)
            ? A2AWorkspaceConfigStore.ServerFileName
            : key;
        A2AWorkspaceConfigStore.DeleteFile(root!, fileName);
        return WebApi.OkJson();
    });

    private static A2AServerDto ToServerDto(A2AWorkspaceFile file) => new(
        string.IsNullOrWhiteSpace(file.Host) ? "127.0.0.1" : file.Host!,
        file.Port ?? 0,
        string.IsNullOrWhiteSpace(file.Path) ? "/a2a" : file.Path!,
        file.RequireApiKey == true,
        file.ApiKeys ?? new List<string>(),
        file.AgentName,
        file.AgentDescription,
        file.AgentVersion);
}