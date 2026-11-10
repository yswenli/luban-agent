/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： A2AContracts
*描述：A2A 协作配置的请求 / 响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

/// <summary>A2A 服务端配置。</summary>
/// <param name="Host">监听地址。</param>
/// <param name="Port">监听端口（0 = 随机）。</param>
/// <param name="Path">端点前缀。</param>
/// <param name="RequireApiKey">是否要求 API Key。</param>
/// <param name="ApiKeys">允许的 API Key 列表。</param>
/// <param name="AgentName">对外名称。</param>
/// <param name="AgentDescription">对外描述。</param>
/// <param name="AgentVersion">对外版本。</param>
public sealed record A2AServerDto(
    string Host,
    int Port,
    string Path,
    bool RequireApiKey,
    IReadOnlyList<string> ApiKeys,
    string? AgentName,
    string? AgentDescription,
    string? AgentVersion);

/// <summary>保存 A2A 服务端配置。</summary>
public sealed record A2AServerSaveRequest
{
    /// <summary>作用域（必须是工作区标识）。</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>监听地址。</summary>
    public string? Host { get; init; }

    /// <summary>监听端口（0 = 随机）。</summary>
    public int? Port { get; init; }

    /// <summary>端点前缀。</summary>
    public string? Path { get; init; }

    /// <summary>是否要求 API Key。</summary>
    public bool RequireApiKey { get; init; }

    /// <summary>允许的 API Key 列表。</summary>
    public IReadOnlyList<string>? ApiKeys { get; init; }

    /// <summary>对外名称。</summary>
    public string? AgentName { get; init; }

    /// <summary>对外描述。</summary>
    public string? AgentDescription { get; init; }

    /// <summary>对外版本。</summary>
    public string? AgentVersion { get; init; }
}

/// <summary>A2A 客户端列表项。</summary>
/// <param name="Key">文件名（条目键）。</param>
/// <param name="Name">客户端名称。</param>
/// <param name="Endpoint">远端地址。</param>
public sealed record A2AClientListItemDto(string Key, string? Name, string? Endpoint);

/// <summary>A2A 客户端配置。</summary>
/// <param name="Key">文件名（条目键）。</param>
/// <param name="Name">客户端名称。</param>
/// <param name="Endpoint">远端地址。</param>
/// <param name="ApiKey">出站 API Key 明文（本机配置）。</param>
/// <param name="RegisterAsSubAgent">是否注册为本地子代理。</param>
/// <param name="RemoteAgentName">远端 Agent 名。</param>
/// <param name="AllowAll">是否声明 AllowAll。</param>
public sealed record A2AClientDto(
    string Key,
    string? Name,
    string? Endpoint,
    string? ApiKey,
    bool RegisterAsSubAgent,
    string? RemoteAgentName,
    bool AllowAll);

/// <summary>保存 A2A 客户端配置。</summary>
public sealed record A2AClientSaveRequest
{
    /// <summary>作用域（必须是工作区标识）。</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>原文件名；新建时为 null。</summary>
    public string? OldKey { get; init; }

    /// <summary>客户端名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>远端地址。</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>出站 API Key。</summary>
    public string? ApiKey { get; init; }

    /// <summary>是否注册为本地子代理。</summary>
    public bool RegisterAsSubAgent { get; init; }

    /// <summary>远端 Agent 名。</summary>
    public string? RemoteAgentName { get; init; }

    /// <summary>是否声明 AllowAll。</summary>
    public bool AllowAll { get; init; }
}