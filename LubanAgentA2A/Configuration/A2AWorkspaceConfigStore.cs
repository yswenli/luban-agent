/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Configuration
*文件名： A2AWorkspaceConfigStore
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/9
*描述：工作区 A2A 配置文件（server.json + client.<name>.json）的读写助手，供 CLI/Codex 设置页使用
*
*****************************************************************************/
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentA2A.Configuration;

/// <summary>
/// 工作区 A2A 配置（<c>.luban-agent/a2a/*.json</c>）读写助手。
/// 约定：<c>server.json</c> 承载服务端角色；<c>client.&lt;name&gt;.json</c> 每个承载一个远端客户端。
/// 与 <see cref="Agent2AgentConfigResolver"/> 的解析规则保持一致（camelCase，靠文件名归类）。
/// </summary>
public static class A2AWorkspaceConfigStore
{
    /// <summary>服务端配置文件名。</summary>
    public const string ServerFileName = "server.json";

    private const string ClientPrefix = "client.";
    private const string ClientPattern = "client*.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>解析工作区 A2A 配置目录。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <returns>配置目录绝对路径。</returns>
    public static string ResolveDir(string workspaceRoot)
        => Path.Combine(workspaceRoot, ".luban-agent", "a2a");

    /// <summary>构造 <c>client.&lt;name&gt;.json</c> 文件名（非法文件名字符替换为 '-'）。</summary>
    /// <param name="name">客户端逻辑名称。</param>
    /// <returns>文件名。</returns>
    public static string BuildClientFileName(string name)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "remote" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '-');
        return ClientPrefix + safe + ".json";
    }

    /// <summary>服务端配置文件是否存在。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <returns>存在为 true。</returns>
    public static bool ServerExists(string workspaceRoot)
        => File.Exists(Path.Combine(ResolveDir(workspaceRoot), ServerFileName));

    /// <summary>读取服务端配置。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <returns>配置；文件不存在返回 null。</returns>
    public static A2AWorkspaceFile? LoadServer(string workspaceRoot)
    {
        var path = Path.Combine(ResolveDir(workspaceRoot), ServerFileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>枚举客户端配置文件（按文件名排序）。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <returns>(文件名, 配置) 列表。</returns>
    public static List<(string FileName, A2AWorkspaceFile Config)> LoadClients(string workspaceRoot)
    {
        var list = new List<(string, A2AWorkspaceFile)>();
        var dir = ResolveDir(workspaceRoot);
        if (!Directory.Exists(dir)) return list;

        foreach (var path in Directory.EnumerateFiles(dir, ClientPattern).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                list.Add((Path.GetFileName(path), Parse(File.ReadAllText(path))));
            }
            catch
            {
                // 忽略单个无法解析的文件
            }
        }

        return list;
    }

    /// <summary>写入服务端配置文件（camelCase，忽略 null 字段）。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="file">服务端配置。</param>
    public static void SaveServer(string workspaceRoot, A2AWorkspaceFile file)
    {
        var dir = ResolveDir(workspaceRoot);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ServerFileName), Serialize(file));
    }

    /// <summary>写入客户端配置文件。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="fileName">文件名（通常由 <see cref="BuildClientFileName"/> 生成）。</param>
    /// <param name="file">客户端配置。</param>
    public static void SaveClient(string workspaceRoot, string fileName, A2AWorkspaceFile file)
    {
        var dir = ResolveDir(workspaceRoot);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), Serialize(file));
    }

    /// <summary>删除指定配置文件（不存在则忽略）。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="fileName">文件名。</param>
    public static void DeleteFile(string workspaceRoot, string fileName)
    {
        var path = Path.Combine(ResolveDir(workspaceRoot), fileName);
        if (File.Exists(path)) File.Delete(path);
    }

    private static A2AWorkspaceFile Parse(string json)
        => JsonSerializer.Deserialize<A2AWorkspaceFile>(json, ReadOptions) ?? new A2AWorkspaceFile();

    private static string Serialize(A2AWorkspaceFile file)
        => JsonSerializer.Serialize(file, WriteOptions);
}