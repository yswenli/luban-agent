using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Connectors;
using LuBan.AIAgent.Plugins.Security;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgentCore.Services.Management;

/// <summary>
/// 连接器管理实现。
/// 读走 <see cref="ConnectorRegistry"/>（声明 + 启用 + 工厂三级合并），写走
/// <see cref="IConnectorConfigWriter"/>（工作区 <c>.luban-agent/connectors/&lt;name&gt;.json</c>），
/// 探测走 <see cref="ConnectorInstanceHost"/>（实例化 + 初始化 + 数暴露面）。
/// </summary>
/// <remarks>
/// 连接器的启用状态只能落在工作区（<c>config.json</c> 只表达「可用性」），故写操作必须先选中工作区；
/// 未选中工作区时直接抛异常，不做隐式回落，避免把启用状态写到用户级目录。
/// </remarks>
public sealed class ConnectorManagementService : IConnectorManagementService
{
    private const string NoWorkspaceMessage = "连接器启用状态只能写入工作区，请先选择一个工作区。";

    private readonly ConnectorRegistry _registry;
    private readonly ConnectorFactoryRegistry _factories;
    private readonly ConnectorInstanceHost _host;
    private readonly IConnectorConfigWriter _writer;
    private readonly IWorkspaceManager _workspaces;
    private readonly IAppConfigReader _configReader;
    private readonly IConfiguration _configuration;
    private readonly IAgentReloadTrigger? _reloadTrigger;
    private readonly ICredentialStore? _credentials;

    /// <summary>
    /// 创建连接器管理服务
    /// </summary>
    /// <param name="registry">连接器注册表（会话级）</param>
    /// <param name="factories">连接器工厂表（单例）</param>
    /// <param name="host">连接器实例宿主（单例）</param>
    /// <param name="writer">连接器配置写入器（工作区声明落盘）</param>
    /// <param name="workspaces">工作区管理器</param>
    /// <param name="configReader">config.json 读取器（连接器声明）</param>
    /// <param name="configuration">宿主配置（appsettings 层，可选参数覆盖）</param>
    /// <param name="reloadTrigger">Agent 重载触发器（可选）</param>
    /// <param name="credentials">凭据存储（可选；缺失时无法校验凭据引用）</param>
    public ConnectorManagementService(
        ConnectorRegistry registry,
        ConnectorFactoryRegistry factories,
        ConnectorInstanceHost host,
        IConnectorConfigWriter writer,
        IWorkspaceManager workspaces,
        IAppConfigReader configReader,
        IConfiguration configuration,
        IAgentReloadTrigger? reloadTrigger = null,
        ICredentialStore? credentials = null)
    {
        _registry = registry;
        _factories = factories;
        _host = host;
        _writer = writer;
        _workspaces = workspaces;
        _configReader = configReader;
        _configuration = configuration;
        _reloadTrigger = reloadTrigger;
        _credentials = credentials;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ConnectorStatus>> ListAsync(CancellationToken ct = default)
        => Task.FromResult(_registry.GetStates(_workspaces.CurrentWorkspace?.RootPath));

    /// <inheritdoc />
    public async Task AddAsync(ConnectorConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var name = config.Name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("连接器名称不能为空。", nameof(config));

        if (string.IsNullOrWhiteSpace(config.Provider))
            throw new ArgumentException("连接器的提供方键不能为空。", nameof(config));

        if (!_factories.Has(config.Provider))
            throw new InvalidOperationException($"提供方 '{config.Provider}' 未注册，请先安装对应连接器。");

        var root = RequireWorkspaceRoot();

        var entry = new ConnectorWorkspaceEntry
        {
            Name = name,
            Provider = config.Provider.Trim(),
            Settings = config.Settings is null || config.Settings.Count == 0
                ? null
                : new Dictionary<string, string?>(config.Settings, StringComparer.OrdinalIgnoreCase),
            Enabled = config.Enabled,
        };

        await WriteEntryAsync(entry, root, ct).ConfigureAwait(false);
        RefreshWorkspace(root);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var safeName = EnsureSafeName(name, nameof(name));
        var root = RequireWorkspaceRoot();

        var result = await _writer.DeleteAsync(safeName, root, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Message ?? $"删除连接器 '{safeName}' 失败。");

        RefreshWorkspace(root);
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default)
    {
        var safeName = EnsureSafeName(name, nameof(name));
        var root = RequireWorkspaceRoot();

        // 保留既有声明的 provider / settings，只改启用标记（文件不存在时写「仅启用状态」条目）
        var existing = ReadWorkspaceEntry(root, safeName);
        var entry = existing ?? new ConnectorWorkspaceEntry { Name = safeName };
        entry.Name ??= safeName;
        entry.Enabled = enabled;

        await WriteEntryAsync(entry, root, ct).ConfigureAwait(false);
        RefreshWorkspace(root);
    }

    /// <inheritdoc />
    public async Task<ConnectorTestResult> TestAsync(string name, CancellationToken ct = default)
    {
        var safeName = EnsureSafeName(name, nameof(name));
        var root = _workspaces.CurrentWorkspace?.RootPath;
        var declared = FindDeclaration(safeName, root);

        if (declared is null)
            return new ConnectorTestResult(false, 0, $"未找到连接器 '{safeName}' 的声明（config.json:connectors 或工作区 .luban-agent/connectors）。");

        if (string.IsNullOrWhiteSpace(declared.Provider))
            return new ConnectorTestResult(false, 0, $"连接器 '{safeName}' 未指定提供方（provider），无法实例化。");

        if (!_factories.Has(declared.Provider))
            return new ConnectorTestResult(false, 0, $"提供方 '{declared.Provider}' 未注册，连接器不可用。");

        // 凭据引用可解析性：settings 里出现 $credential:<键> 时校验其存在
        var missingKey = FindMissingCredential(declared.Settings);
        if (missingKey is not null)
            return new ConnectorTestResult(false, 0, $"缺少凭据 '{missingKey}'，请先在凭据存储中配置。");

        try
        {
            var connector = _host.GetOrCreate(
                safeName,
                declared.Provider,
                BuildSettings(declared, root),
                _configuration);

            if (connector is null)
                return new ConnectorTestResult(false, 0, $"提供方 '{declared.Provider}' 未注册，连接器不可用。");

            await connector.InitializeAsync(ct).ConfigureAwait(false);

            var count = connector.McpExposures.Count;
            return new ConnectorTestResult(true, count,
                count > 0
                    ? $"连接器 '{safeName}' 可用，暴露 {count} 个 MCP 服务器。"
                    : $"连接器 '{safeName}' 可用，但未暴露任何 MCP 服务器。");
        }
        catch (Exception ex)
        {
            return new ConnectorTestResult(false, 0, $"连接器 '{safeName}' 初始化失败: {ex.Message}");
        }
    }

    // ---------------- 内部实现 ----------------

    private async Task WriteEntryAsync(ConnectorWorkspaceEntry entry, string workspaceRoot, CancellationToken ct)
    {
        var result = await _writer.WriteAsync(entry, overwrite: true, workspaceRoot, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Message ?? $"写入连接器 '{entry.Name}' 失败。");
    }

    /// <summary>写盘后刷新会话注册表，并请求 Agent 重载（与框架连接器管理工具同口径）。</summary>
    private void RefreshWorkspace(string workspaceRoot)
    {
        _registry.LoadFromWorkspace(workspaceRoot);
        _reloadTrigger?.RequestReload();
    }

    private string RequireWorkspaceRoot()
    {
        var root = _workspaces.CurrentWorkspace?.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(NoWorkspaceMessage);
        return root;
    }

    /// <summary>读取工作区连接器声明文件；不存在或无法解析时返回 null。</summary>
    private static ConnectorWorkspaceEntry? ReadWorkspaceEntry(string workspaceRoot, string name)
    {
        var file = Path.Combine(workspaceRoot, ".luban-agent", "connectors", name + ".json");
        if (!File.Exists(file)) return null;

        try
        {
            return JsonSerializer.Deserialize<ConnectorWorkspaceEntry>(File.ReadAllText(file));
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取工作区连接器声明失败: {file}", ex);
            return null;
        }
    }

    /// <summary>
    /// 取有效声明：config.json 为基，工作区完整声明覆盖（与 <c>ConnectorRegistry</c> 合并语义一致，
    /// 只取本方法所需字段：provider / settings / enabled）。
    /// </summary>
    private ConnectorConfig? FindDeclaration(string name, string? workspaceRoot)
    {
        ConnectorConfig? declared = null;
        try
        {
            declared = _configReader.Connectors
                .FirstOrDefault(c => string.Equals(c.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Warn("读取 config.json 连接器声明失败", ex);
        }

        ConnectorWorkspaceEntry? entry = null;
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            entry = ReadWorkspaceEntry(workspaceRoot!, name);

        if (declared is null)
        {
            // 工作区自带的完整声明（config.json 未声明）也算已声明
            return entry is null || entry.IsEnableOnly ? null : entry.ToConnectorConfig(name);
        }

        if (entry is null)
            return declared;

        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in declared.Settings ?? new Dictionary<string, string?>())
            settings[kvp.Key] = kvp.Value;
        foreach (var kvp in entry.Settings ?? new Dictionary<string, string?>())
            settings[kvp.Key] = kvp.Value;

        return new ConnectorConfig
        {
            Name = name,
            Provider = string.IsNullOrWhiteSpace(entry.Provider) ? declared.Provider : entry.Provider!.Trim(),
            Enabled = entry.Enabled,
            Settings = settings,
        };
    }

    /// <summary>合并非敏感参数：声明为基，appsettings <c>LuBanAgent:Connectors:&lt;实例名&gt;</c> 覆盖。</summary>
    private ConnectorSettings BuildSettings(ConnectorConfig config, string? workspaceRoot)
    {
        _ = workspaceRoot; // 工作区覆盖已在 FindDeclaration 内合并

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in config.Settings ?? new Dictionary<string, string?>())
            values[kvp.Key] = kvp.Value;

        var section = _configuration.GetSection($"LuBanAgent:Connectors:{config.Name}");
        foreach (var child in section.GetChildren())
        {
            // AccessToken 属凭据，绝不进入参数包
            if (string.Equals(child.Key, "AccessToken", StringComparison.OrdinalIgnoreCase)) continue;
            values[child.Key] = child.Value;
        }

        return new ConnectorSettings(values);
    }

    /// <summary>找出无法解析的凭据引用键；全部可解析（或没有引用）时返回 null。</summary>
    private string? FindMissingCredential(Dictionary<string, string?>? settings)
    {
        if (settings is null || settings.Count == 0) return null;

        foreach (var value in settings.Values)
        {
            if (!CredentialValueResolver.IsReference(value)) continue;

            var key = CredentialValueResolver.ExtractKey(value);
            if (string.IsNullOrEmpty(key)) continue;
            if (_credentials is null || !_credentials.Contains(key)) return key;
        }

        return null;
    }

    private static string EnsureSafeName(string? value, string paramName)
    {
        var name = value?.Trim() ?? "";
        if (name.Length == 0)
            throw new ArgumentException("连接器名称不能为空。", paramName);
        if (name is "." or ".." || name.Contains("..", StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"非法名称: '{value}'", paramName);
        }
        return name;
    }
}