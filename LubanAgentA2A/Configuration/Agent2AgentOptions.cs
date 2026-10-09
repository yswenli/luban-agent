/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Configuration
*文件名： Agent2AgentOptions
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A agent 侧有效配置（工作区并集解析结果）与工作区文件模型/解析器
*
*****************************************************************************/
using System.Text.Json;

using LuBan.AIAgent.A2A;

namespace LubanAgentA2A.Configuration;

/// <summary>
/// A2A agent 侧有效配置。由 <see cref="Agent2AgentConfigResolver"/> 把 appsettings 基线
/// 与工作区 <c>.luban-agent/a2a/*.json</c> 并集后得出，承载 SDK 实现所需选项。
/// </summary>
public sealed class Agent2AgentOptions
{
    /// <summary>总开关（仅由 appsettings 决定，工作区不能开启被关闭的 A2A）。</summary>
    public bool Enabled { get; set; }

    /// <summary>服务端角色参数；为 null 表示不启用 server 角色。</summary>
    public A2AServerOptions? Server { get; set; }

    /// <summary>客户端（远端连接）角色列表。 </summary>
    public List<A2AClientOptions> Clients { get; set; } = new();

    /// <summary>内部子代理是否全放行（A3 语义）。</summary>
    public bool SubAgentAllowAll { get; set; }

    /// <summary>是否启用 server 角色。</summary>
    public bool HasServerRole => Server is not null;
}

/// <summary>
/// 工作区 A2A 配置文件模型（<c>.luban-agent/a2a/*.json</c>，camelCase）。
/// server.json 与 client*.json 字段并集于同一类型，按 <see cref="Mode"/> 或文件名归类。
/// </summary>
public sealed class A2AWorkspaceFile
{
    /// <summary>角色模式：server | client。</summary>
    public string? Mode { get; set; }

    // ---- server 字段 ----
    /// <summary>监听地址（仅回环）。</summary>
    public string? Host { get; set; }

    /// <summary>监听端口（0 = 随机）。</summary>
    public int? Port { get; set; }

    /// <summary>端点路径前缀。</summary>
    public string? Path { get; set; }

    /// <summary>是否要求 API Key。</summary>
    public bool? RequireApiKey { get; set; }

    /// <summary>允许的 API Key 列表。</summary>
    public List<string>? ApiKeys { get; set; }

    /// <summary>最大并发任务数。</summary>
    public int? MaxConcurrentTasks { get; set; }

    /// <summary>任务保留时长（分钟）。</summary>
    public int? TaskRetentionMinutes { get; set; }

    /// <summary>允许的 webhook 域名白名单。</summary>
    public List<string>? AllowedWebhookDomains { get; set; }

    /// <summary>对外 Agent 名称。</summary>
    public string? AgentName { get; set; }

    /// <summary>对外 Agent 描述。</summary>
    public string? AgentDescription { get; set; }

    /// <summary>对外 Agent 版本。</summary>
    public string? AgentVersion { get; set; }

    // ---- client 字段 ----
    /// <summary>客户端逻辑名称（唯一）。</summary>
    public string? Name { get; set; }

    /// <summary>远端端点基址。</summary>
    public string? Endpoint { get; set; }

    /// <summary>出站 API Key（明文）。</summary>
    public string? ApiKey { get; set; }

    /// <summary>是否注册为本地 SubAgent 角色。</summary>
    public bool? RegisterAsSubAgent { get; set; }

    /// <summary>远端 Agent 名称。</summary>
    public string? RemoteAgentName { get; set; }

    /// <summary>是否全放行（对外身份声明）。</summary>
    public bool? AllowAll { get; set; }
}

/// <summary>
/// A2A 配置解析器。按 Rev.7 §4.7 合并算法把 appsettings 基线与工作区文件并集，
/// 得出有效 <see cref="Agent2AgentOptions"/>。
/// </summary>
public static class Agent2AgentConfigResolver
{
    /// <summary>工作区 A2A 目录（相对工作区根）。</summary>
    public const string RelativeDir = ".luban-agent/a2a";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读取单个工作区文件文本为模型。</summary>
    public static A2AWorkspaceFile Parse(string json)
        => JsonSerializer.Deserialize<A2AWorkspaceFile>(json, ReadOptions) ?? new A2AWorkspaceFile();

    /// <summary>按文件名解析角色模式；显式 <c>mode</c> 优先。</summary>
    public static string ResolveMode(string fileName, string? explicitMode)
    {
        if (!string.IsNullOrWhiteSpace(explicitMode))
        {
            return explicitMode.Trim().ToLowerInvariant();
        }

        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        if (name == "server" || name.StartsWith("server.", StringComparison.Ordinal))
        {
            return "server";
        }
        if (name == "client" || name.StartsWith("client.", StringComparison.Ordinal))
        {
            return "client";
        }
        return "";
    }

    /// <summary>
    /// 解析有效配置。
    /// </summary>
    /// <param name="baseline">appsettings 绑定的框架基线与总开关。</param>
    /// <param name="workspaceRoot">工作区根路径；为空则不扫描工作区文件。</param>
    /// <returns>有效配置。</returns>
    public static Agent2AgentOptions Resolve(A2AOptions baseline, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        var result = new Agent2AgentOptions
        {
            Enabled = baseline.Enabled,
            SubAgentAllowAll = baseline.SubAgentAllowAll,
        };

        if (!baseline.Enabled)
        {
            return result;
        }

        var files = EnumerateWorkspaceFiles(workspaceRoot);
        if (files.Count == 0)
        {
            ApplyModeDefault(result, baseline);
            return result;
        }

        var server = baseline.Server is null ? null : CloneServer(baseline.Server);
        var clients = baseline.Clients.Select(CloneClient).ToList();

        foreach (var file in files)
        {
            var mode = ResolveMode(Path.GetFileName(file.Name), file.Config.Mode);
            switch (mode)
            {
                case "server":
                    server ??= new A2AServerOptions();
                    ApplyServerFile(server, file.Config);
                    break;
                case "client":
                    clients.Add(BuildClient(file.Config, file.Name));
                    break;
            }
        }

        result.Server = server;
        result.Clients = clients;
        return result;
    }

    /// <summary>
    /// 枚举并加载工作区 A2A 文件（按文件名排序）。单个文件失败不影响其他文件。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <returns>(文件名, 配置) 列表。</returns>
    public static List<(string Name, A2AWorkspaceFile Config)> EnumerateWorkspaceFiles(string? workspaceRoot)
    {
        var list = new List<(string, A2AWorkspaceFile)>();
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return list;
        }

        var dir = Path.Combine(workspaceRoot!, ".luban-agent", "a2a");
        if (!Directory.Exists(dir))
        {
            return list;
        }

        foreach (var path in Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var config = Parse(File.ReadAllText(path));
                list.Add((Path.GetFileName(path), config));
            }
            catch
            {
                // 忽略单个无法解析的文件
            }
        }

        return list;
    }

    private static void ApplyModeDefault(Agent2AgentOptions result, A2AOptions baseline)
    {
        switch ((baseline.Mode ?? "off").Trim().ToLowerInvariant())
        {
            case "server":
                result.Server = baseline.Server is null ? new A2AServerOptions() : CloneServer(baseline.Server);
                break;
            case "client":
                result.Clients = baseline.Clients.Select(CloneClient).ToList();
                break;
            default:
                break;
        }
    }

    private static void ApplyServerFile(A2AServerOptions server, A2AWorkspaceFile file)
    {
        if (file.Host is not null) server.Host = file.Host;
        if (file.Port.HasValue) server.Port = file.Port.Value;
        if (file.Path is not null) server.Path = file.Path;
        if (file.RequireApiKey.HasValue) server.RequireApiKey = file.RequireApiKey.Value;
        if (file.ApiKeys is not null) server.ApiKeys = file.ApiKeys;
        if (file.MaxConcurrentTasks.HasValue) server.MaxConcurrentTasks = file.MaxConcurrentTasks.Value;
        if (file.TaskRetentionMinutes.HasValue) server.TaskRetentionMinutes = file.TaskRetentionMinutes.Value;
        if (file.AllowedWebhookDomains is not null) server.AllowedWebhookDomains = file.AllowedWebhookDomains;
        if (file.AgentName is not null) server.AgentName = file.AgentName;
        if (file.AgentDescription is not null) server.AgentDescription = file.AgentDescription;
        if (file.AgentVersion is not null) server.AgentVersion = file.AgentVersion;
    }

    private static A2AClientOptions BuildClient(A2AWorkspaceFile file, string fallbackName)
    {
        var name = file.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(fallbackName);
        }

        return new A2AClientOptions
        {
            Name = name ?? "",
            Endpoint = file.Endpoint ?? "",
            ApiKey = file.ApiKey,
            RegisterAsSubAgent = file.RegisterAsSubAgent ?? false,
            RemoteAgentName = file.RemoteAgentName,
            AllowAll = file.AllowAll ?? false,
        };
    }

    /// <summary>深拷贝服务端选项。</summary>
    public static A2AServerOptions CloneServer(A2AServerOptions source)
        => new()
        {
            Host = source.Host,
            Port = source.Port,
            Path = source.Path,
            RequireApiKey = source.RequireApiKey,
            ApiKeys = new List<string>(source.ApiKeys),
            MaxConcurrentTasks = source.MaxConcurrentTasks,
            TaskRetentionMinutes = source.TaskRetentionMinutes,
            AllowedWebhookDomains = new List<string>(source.AllowedWebhookDomains),
            AgentName = source.AgentName,
            AgentDescription = source.AgentDescription,
            AgentVersion = source.AgentVersion,
        };

    /// <summary>深拷贝客户端选项。</summary>
    public static A2AClientOptions CloneClient(A2AClientOptions source)
        => new()
        {
            Name = source.Name,
            Endpoint = source.Endpoint,
            ApiKey = source.ApiKey,
            RegisterAsSubAgent = source.RegisterAsSubAgent,
            RemoteAgentName = source.RemoteAgentName,
            AllowAll = source.AllowAll,
        };
}