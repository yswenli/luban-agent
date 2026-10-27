using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.MCP;
using LuBan.AIAgent.Tools.McpManage;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// MCP 客户端管理实现（内置 + 配置 + 插件注册）。
/// 业务规则委托 <see cref="ConfigManager"/>（config.json 持久化）与 <see cref="MCPRegistry"/>（三级合并视图），
/// 文件后端直接操作 <c>mcps/&lt;name&gt;.json</c>（与 Codex 设置页原逻辑逐字一致）。
/// </summary>
public sealed class McpManagementService : IMcpManagementService
{
    /// <summary>文件后端的落盘格式：与 Codex 设置页旧实现一致（缩进、属性名不转换）。</summary>
    private static readonly JsonSerializerOptions FileJsonOptions = new() { WriteIndented = true };

    private readonly ConfigManager _config;
    private readonly MCPRegistry _registry;
    private readonly IWorkspaceManager _workspaces;
    private readonly IEnumerable<IMCPClient> _frameworkClients;
    private readonly IMcpConfigValueResolver _resolver;
    private readonly McpOptions _mcpOptions;

    /// <summary>
    /// 创建 MCP 管理服务
    /// </summary>
    /// <param name="config">配置管理器</param>
    /// <param name="registry">MCP 注册表（会话级合并视图）</param>
    /// <param name="workspaces">工作区管理器（判定文件型来源）</param>
    /// <param name="frameworkClients">框架 DI 登记的进程内客户端（用于区分「内置」与「插件 / 连接器运行期注册」）</param>
    /// <param name="resolver">配置值解析器（<c>$credential:</c> 引用）</param>
    /// <param name="mcpOptions">MCP 运行期选项（连接超时）</param>
    public McpManagementService(
        ConfigManager config,
        MCPRegistry registry,
        IWorkspaceManager workspaces,
        IEnumerable<IMCPClient> frameworkClients,
        IMcpConfigValueResolver resolver,
        IOptions<McpOptions> mcpOptions)
    {
        _config = config;
        _registry = registry;
        _workspaces = workspaces;
        _frameworkClients = frameworkClients;
        _resolver = resolver;
        _mcpOptions = mcpOptions.Value;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<McpInfo>> ListAsync(CancellationToken ct = default)
    {
        // 框架 DI 直接注入的客户端 = 真·内置；注册表里其余「硬编码」项来自插件 / 连接器的运行期注册
        var frameworkNames = new HashSet<string>(
            _frameworkClients.Where(c => c is not null).Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        var items = new List<McpInfo>();

        foreach (var client in _registry.GetAll())
        {
            var isBuiltin = _registry.IsBuiltin(client.Name);
            items.Add(new McpInfo(
                client.Name,
                client.Description ?? "",
                true,
                isBuiltin,
                client.IsConnected,
                isBuiltin && !frameworkNames.Contains(client.Name)));
        }

        // 已禁用的配置项 / 内置项都不在合并视图里，单独补出（与 /mcp -list 的分组顺序一致）
        foreach (var cfg in _config.McpServers.Where(s => !s.Enabled))
            items.Add(new McpInfo(cfg.Name, cfg.Description ?? "", false, false, false, false));

        foreach (var name in _config.DisabledBuiltinMcpClients)
            items.Add(new McpInfo(name, "", false, true, false, false));

        return Task.FromResult<IReadOnlyList<McpInfo>>(items);
    }

    /// <inheritdoc />
    public Task AddAsync(McpServerConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config.AddMcpServer(config);
        _registry.Reload();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(McpServerConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config.UpdateMcpServer(config);
        _registry.Reload();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string name, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default)
    {
        var safeName = EnsureSafeName(name, nameof(name));

        switch (origin)
        {
            case ManagedOrigin.Builtin:
                throw new InvalidOperationException($"内置 MCP 客户端 '{safeName}' 不可删除");

            case ManagedOrigin.Config:
                // 与旧 /mcp -delete 一致：先断开再删配置
                await DisconnectIfConnectedAsync(safeName).ConfigureAwait(false);
                _config.RemoveMcpServer(safeName);
                _registry.Reload();
                break;

            case ManagedOrigin.File:
                var file = Path.Combine(ResolveDir(workspaceRoot), safeName + ".json");
                if (File.Exists(file)) File.Delete(file);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default)
    {
        var safeName = EnsureSafeName(name, nameof(name));

        if (_registry.IsBuiltin(safeName))
        {
            _config.SetBuiltinMcpClientEnabled(safeName, enabled);
            _registry.Reload();
        }
        else if (_config.McpServers.Any(s => s.Name.Equals(safeName, StringComparison.OrdinalIgnoreCase)))
        {
            // 与旧 /mcp -switch 一致：停用前先断开
            if (!enabled) await DisconnectIfConnectedAsync(safeName).ConfigureAwait(false);
            _config.SetMcpServerEnabled(safeName, enabled);
            _registry.Reload();
        }
        else if (ListFileIds(_workspaces.CurrentWorkspace?.RootPath).Contains(safeName, StringComparer.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"文件型 MCP 服务 '{safeName}' 不支持在此启用/禁用，请直接编辑 .luban-agent/mcps 下该文件的 Enabled 字段");
        }
        else
        {
            throw new InvalidOperationException($"MCP 客户端 '{safeName}' 不存在");
        }
    }

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(string name, CancellationToken ct = default)
    {
        var safeName = EnsureSafeName(name, nameof(name));
        var client = _registry.Get(safeName)
                     ?? throw new InvalidOperationException($"未找到客户端: {safeName}");

        return await client.ConnectAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MCPTool>> ListToolsAsync(string name, CancellationToken ct = default)
    {
        var safeName = EnsureSafeName(name, nameof(name));
        var client = _registry.Get(safeName)
                     ?? throw new InvalidOperationException($"未找到客户端: {safeName}");

        if (!client.IsConnected)
            throw new InvalidOperationException($"客户端 {safeName} 未连接，请先连接");

        var tools = await client.ListToolsAsync(ct).ConfigureAwait(false);
        return tools.ToList();
    }

    /// <inheritdoc />
    public async Task<McpTestResult> TestAsync(string name, CancellationToken ct = default)
    {
        var safeName = EnsureSafeName(name, nameof(name));

        var cfg = _config.McpServers.FirstOrDefault(s => s.Name.Equals(safeName, StringComparison.OrdinalIgnoreCase))
                  ?? ReadFile(safeName, _workspaces.CurrentWorkspace?.RootPath);

        if (cfg is null)
        {
            // 内置 / 插件 / 连接器派生的进程内客户端：没有可复制的配置，只能回报当前连接状态
            var existing = _registry.Get(safeName)
                           ?? throw new InvalidOperationException($"未找到 MCP 服务器: {safeName}");

            return new McpTestResult
            {
                Connected = existing.IsConnected,
                ToolCount = 0,
                Message = existing.IsConnected
                    ? "进程内客户端常驻可用（工具数请用 ListToolsAsync 查看）。"
                    : "进程内客户端当前未连接。",
            };
        }

        var timeoutSeconds = _mcpOptions.ConnectTimeoutSeconds > 0 ? _mcpOptions.ConnectTimeoutSeconds : 10;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var client = MCPClientFactory.Create(cfg, _resolver);
        try
        {
            var connected = await client.ConnectAsync(cts.Token).ConfigureAwait(false);
            var result = new McpTestResult { Connected = connected };

            if (connected)
            {
                try
                {
                    var tools = await client.ListToolsAsync(cts.Token).ConfigureAwait(false);
                    result.ToolCount = tools.Count();
                    result.Message = $"连接成功，发现 {result.ToolCount} 个工具。";
                }
                catch
                {
                    result.Message = "连接成功，但获取工具列表失败。";
                }
            }
            else
            {
                result.Message = "连接失败（进程未启动或超时）。";
            }

            return result;
        }
        catch (Exception ex)
        {
            return new McpTestResult { Connected = false, ToolCount = 0, Message = $"测试失败: {ex.Message}" };
        }
        finally
        {
            try { if (client.IsConnected) await client.DisconnectAsync().ConfigureAwait(false); }
            catch (Exception ex) { Logger.Debug($"测试后断开 MCP 客户端失败: {safeName}", ex); }

            (client as IDisposable)?.Dispose();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFileIds(string? workspaceRoot)
    {
        var dir = ResolveDir(workspaceRoot);
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        return Directory.GetFiles(dir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public McpServerConfig? ReadFile(string id, string? workspaceRoot)
    {
        var safeId = EnsureSafeName(id, nameof(id));
        var path = Path.Combine(ResolveDir(workspaceRoot), safeId + ".json");
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<McpServerConfig>(json) ?? new McpServerConfig { Name = safeId };
    }

    /// <inheritdoc />
    public void SaveFile(string? originalId, McpServerConfig config, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(config);

        var name = EnsureSafeName(config.Name, nameof(config));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("MCP 服务的 Name 不能为空。", nameof(config));

        var root = ResolveDir(workspaceRoot);
        Directory.CreateDirectory(root);

        // 改名：删除旧文件（与 Codex 设置页旧实现一致，大小写视为不同）
        if (!string.IsNullOrEmpty(originalId) && originalId != name)
        {
            var oldPath = Path.Combine(root, EnsureSafeName(originalId, nameof(originalId)) + ".json");
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }

        config.Name = name;
        File.WriteAllText(Path.Combine(root, name + ".json"), JsonSerializer.Serialize(config, FileJsonOptions));
    }

    // ---------------- 内部实现 ----------------

    private async Task DisconnectIfConnectedAsync(string name)
    {
        var client = _registry.Get(name);
        if (client is null || !client.IsConnected) return;

        try
        {
            await client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Debug($"断开 MCP 客户端失败: {name}", ex);
        }
    }

    private static string ResolveDir(string? workspaceRoot)
        => string.IsNullOrWhiteSpace(workspaceRoot)
            ? GlobalLubanAgentPath.McpsDir
            : Path.Combine(workspaceRoot, ".luban-agent", "mcps");

    /// <summary>拒绝空名与路径穿越（Name 即文件名）。</summary>
    private static string EnsureSafeName(string? value, string paramName)
    {
        var name = value?.Trim() ?? "";
        if (name.Length == 0) return name;
        if (name is "." or ".." || name.Contains("..", StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"非法名称: '{value}'", paramName);
        }
        return name;
    }
}