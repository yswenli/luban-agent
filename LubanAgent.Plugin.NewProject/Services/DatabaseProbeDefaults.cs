/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： DatabaseProbeDefaults
*创建时间：2026/10/21
*描述：Step 4 表单默认值：从已克隆后端仓库的 appsettings.json 连接串推导（只读、绝不回填口令）
*****************************************************************************/

using System.Text.Json;
using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// Step 4 探活表单的默认值来源（纯函数，供单测锁死解析口径）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么从 appsettings.json 取默认值</b>：Step 3 克隆下来的后端模板里已经有一份可用的 MySQL 连接串
/// （<c>DbConnectionOptions:ConnectionConfigs[0]:ConnectionString</c>），把它拆成「服务器 / 端口 / 库名 / 账号」
/// 预填到界面，用户只需按自己项目改库名 + 填口令即可，省掉对照配置文件抄参数的手工步骤。
/// </para>
/// <para>
/// <b>只读且不越界</b>：本类只<b>读</b>该文件，<b>绝不</b>改写它（v0.22 口径：Step 4 不写任何项目文件）；
/// 解析出的口令一律丢弃——<see cref="DatabaseProbeRequest.Password"/> 由用户在界面手工填，不回填、不落档案。
/// </para>
/// <para>
/// <b>解析容错</b>：模板 appsettings.json 里带 <c>//</c> 注释与尾随逗号，故解析时开启
/// <c>CommentHandling.Skip</c> + <c>AllowTrailingCommas</c>；任何异常（文件缺失 / 非法 JSON / 无连接串）
/// 都退化为「没有默认值」，由用户在界面手工填写，绝不因此让本步失败。
/// </para>
/// </remarks>
public static class DatabaseProbeDefaults
{
    /// <summary>每个端模板仓库里连接串的候选相对路径（前者优先）。</summary>
    private static readonly string[] CandidateRelativePaths =
    {
        Path.Combine("WebHost", "appsettings.json"),
        "appsettings.json"
    };

    /// <summary>连接串键名 → 语义的映射（全部忽略大小写；口令键<b>刻意不列</b>）。</summary>
    private static readonly string[] HostKeys = { "server", "data source", "datasource", "host", "address", "addr", "network address" };
    private static readonly string[] PortKeys = { "port" };
    private static readonly string[] DatabaseKeys = { "database", "initial catalog" };
    private static readonly string[] UserKeys = { "user", "user id", "uid", "username", "userid" };

    /// <summary>
    /// 从已克隆的后端仓库推导探活默认值。
    /// </summary>
    /// <param name="backendLocalPath">后端仓库本地目录。</param>
    /// <returns>默认参数（口令恒为空）；推导不出时返回端口为 3306 的空参数。</returns>
    public static DatabaseProbeRequest FromBackendRepository(string backendLocalPath)
    {
        var file = FindAppSettingsFile(backendLocalPath);
        if (file is null)
            return new DatabaseProbeRequest { Port = 3306 };

        return ParseMySqlConnectionString(ReadConnectionString(file))
               ?? new DatabaseProbeRequest { Port = 3306 };
    }

    /// <summary>
    /// 定位后端仓库里的 appsettings.json。
    /// </summary>
    /// <param name="backendLocalPath">后端仓库本地目录。</param>
    /// <returns>文件绝对路径；找不到返回 <c>null</c>。</returns>
    public static string? FindAppSettingsFile(string? backendLocalPath)
    {
        if (string.IsNullOrWhiteSpace(backendLocalPath) || !Directory.Exists(backendLocalPath))
            return null;

        foreach (var relative in CandidateRelativePaths)
        {
            var candidate = Path.Combine(backendLocalPath!, relative);
            if (File.Exists(candidate))
                return candidate;
        }

        try
        {
            // 兜底：仓库下一层目录里的 appsettings.json（宿主工程目录名不一定叫 WebHost）
            foreach (var candidate in Directory.EnumerateDirectories(backendLocalPath!)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                         .Select(path => Path.Combine(path, "appsettings.json")))
            {
                if (!File.Exists(candidate))
                    continue;

                if (ParseMySqlConnectionString(ReadConnectionString(candidate)) is not null)
                    return candidate;
            }
        }
        catch (Exception)
        {
            // 目录不可读：退化为「没有默认值」，由用户手工填写
        }

        return null;
    }

    /// <summary>
    /// 读出 appsettings.json 里第一条 <c>DbConnectionOptions:ConnectionConfigs[*]:ConnectionString</c>。
    /// </summary>
    /// <param name="appSettingsFile">appsettings.json 路径。</param>
    /// <returns>连接串；读不到返回 <c>null</c>。</returns>
    public static string? ReadConnectionString(string? appSettingsFile)
    {
        if (string.IsNullOrWhiteSpace(appSettingsFile) || !File.Exists(appSettingsFile))
            return null;

        try
        {
            using var stream = File.OpenRead(appSettingsFile!);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (!TryGetProperty(document.RootElement, "DbConnectionOptions", out var options))
                return null;

            if (!TryGetProperty(options, "ConnectionConfigs", out var configs) || configs.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var config in configs.EnumerateArray())
            {
                if (config.ValueKind != JsonValueKind.Object)
                    continue;

                if (!TryGetProperty(config, "ConnectionString", out var value) || value.ValueKind != JsonValueKind.String)
                    continue;

                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 把 MySQL 连接串拆成「服务器 / 端口 / 库名 / 账号」（<b>口令一律丢弃</b>）。
    /// </summary>
    /// <param name="connectionString">连接串。</param>
    /// <returns>探活参数；连接串为空或没有服务器时返回 <c>null</c>。</returns>
    public static DatabaseProbeRequest? ParseMySqlConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        var request = new DatabaseProbeRequest { Port = 3306 };

        foreach (var pair in connectionString!.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();
            if (value.Length == 0)
                continue;

            if (Matches(HostKeys, key))
            {
                request.Host = value;
                continue;
            }

            if (Matches(PortKeys, key) && int.TryParse(value, out var port) && port is > 0 and <= 65535)
            {
                request.Port = port;
                continue;
            }

            if (Matches(DatabaseKeys, key))
            {
                request.Database = value;
                continue;
            }

            if (Matches(UserKeys, key))
                request.User = value;

            // 其余键（含 pwd / password）一律忽略：口令绝不从文件回填
        }

        return string.IsNullOrWhiteSpace(request.Host) ? null : request;
    }

    /// <summary>连接串键名是否命中语义（忽略大小写与首尾空白）。</summary>
    /// <param name="keys">语义键名集合。</param>
    /// <param name="key">实际键名。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    private static bool Matches(string[] keys, string key)
        => keys.Any(candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>忽略大小写地取 JSON 属性（appsettings 的键名大小写不保证一致）。</summary>
    /// <param name="element">JSON 对象。</param>
    /// <param name="name">属性名。</param>
    /// <param name="value">属性值。</param>
    /// <returns>取到返回 <c>true</c>。</returns>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}