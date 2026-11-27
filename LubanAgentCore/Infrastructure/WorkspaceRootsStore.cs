/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Infrastructure
*文件名： WorkspaceRootsStore
*描述：Web 宿主机级别的「工作区根目录」白名单
*
*****************************************************************************/

namespace LubanAgentCore.Infrastructure;

/// <summary>
/// 工作区根目录白名单（<c>~/.luban-agent/workspace-roots.json</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 机器级配置：不按用户隔离，Web 宿主的所有用户共用同一份允许创建目录。
/// 默认值为 <see cref="AgentHome.Root"/>（<c>~/.luban-agent</c>），因此从 git 克隆的项目
/// 默认落在白名单内；环境变量 <c>LUBAN_AGENT_HOME</c> 会一并改变默认值。
/// </para>
/// <para>
/// 只约束「在哪里新建工作区」，不约束已登记工作区的使用与删除：
/// 从白名单移除某个根目录后，既有工作区仍可切换与访问。
/// </para>
/// </remarks>
public static class WorkspaceRootsStore
{
    /// <summary>配置文件名。</summary>
    public const string FileName = "workspace-roots.json";

    private static readonly object Sync = new();

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

    /// <summary>配置文件的绝对路径。</summary>
    public static string FilePath => Path.Combine(AgentHome.Root, FileName);

    /// <summary>默认根目录：用户数据根 <c>~/.luban-agent</c>。</summary>
    public static string DefaultRoot => AgentHome.Root;

    /// <summary>
    /// 读取根目录列表；文件缺失或无法解析时返回默认根目录。
    /// </summary>
    /// <returns>规范化后的根目录列表（至少一项）。</returns>
    public static IReadOnlyList<string> Load()
    {
        lock (Sync)
        {
            return LoadCore();
        }
    }

    /// <summary>
    /// 保存根目录列表（覆盖写入，原子替换）。
    /// </summary>
    /// <param name="roots">根目录；空白项忽略，至少保留一项。</param>
    /// <returns>实际写入的规范化列表。</returns>
    /// <exception cref="ArgumentException">某个值不是绝对路径、指向文件，或列表为空。</exception>
    public static IReadOnlyList<string> Save(IEnumerable<string> roots)
    {
        var normalized = NormalizeForSave(roots);

        lock (Sync)
        {
            AgentHome.EnsureRoot();
            var temp = FilePath + ".tmp";
            var payload = new WorkspaceRootsFile { Roots = normalized.ToList() };
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, WriteOptions));
            File.Move(temp, FilePath, overwrite: true);
        }

        return normalized;
    }

    /// <summary>
    /// 判断路径是否位于某个根目录之下（含根目录本身）。
    /// </summary>
    /// <param name="path">待判断路径（绝对路径）。</param>
    /// <param name="root">根目录。</param>
    /// <returns>位于其下为 true。</returns>
    public static bool IsWithin(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;

        string normalizedPath;
        string normalizedRoot;
        try
        {
            normalizedPath = WorkspaceIdGenerator.Normalize(path);
            normalizedRoot = WorkspaceIdGenerator.Normalize(root);
        }
        catch
        {
            return false;
        }

        if (normalizedPath.Length == 0 || normalizedRoot.Length == 0) return false;
        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal)) return true;

        // 带上分隔符再比较，避免 D:\data 命中 D:\data-evil
        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断路径是否被任一允许根目录覆盖。
    /// </summary>
    /// <param name="path">待判断路径。</param>
    /// <param name="roots">允许的根目录列表。</param>
    /// <returns>命中任一为 true。</returns>
    public static bool IsAllowed(string path, IReadOnlyList<string> roots)
        => roots.Any(root => IsWithin(path, root));

    /// <summary>读取并规范化根目录（文件缺失/损坏时回落到默认根）。</summary>
    private static IReadOnlyList<string> LoadCore()
    {
        if (!File.Exists(FilePath))
        {
            return [WorkspaceIdGenerator.Normalize(DefaultRoot)];
        }

        try
        {
            var file = JsonSerializer.Deserialize<WorkspaceRootsFile>(File.ReadAllText(FilePath), ReadOptions);
            var list = NormalizeForRead(file?.Roots);
            return list.Count > 0 ? list : [WorkspaceIdGenerator.Normalize(DefaultRoot)];
        }
        catch (Exception ex)
        {
            Logger.Warn("读取工作区根目录配置失败，回落默认根目录", ex, FilePath);
            return [WorkspaceIdGenerator.Normalize(DefaultRoot)];
        }
    }

    /// <summary>写入前校验：绝对路径、非文件、去重；空列表拒绝。</summary>
    private static List<string> NormalizeForSave(IEnumerable<string> roots)
    {
        var list = new List<string>();
        foreach (var raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var value = raw.Trim();
            if (!Path.IsPathFullyQualified(value))
            {
                throw new ArgumentException($"根目录必须是绝对路径：{value}", nameof(roots));
            }

            if (File.Exists(value))
            {
                throw new ArgumentException($"根目录不能是文件：{value}", nameof(roots));
            }

            var normalized = WorkspaceIdGenerator.Normalize(value);
            if (normalized.Length == 0) continue;
            if (!list.Contains(normalized, StringComparer.Ordinal))
            {
                list.Add(normalized);
            }
        }

        if (list.Count == 0)
        {
            throw new ArgumentException("至少保留一个工作区根目录", nameof(roots));
        }

        return list;
    }

    /// <summary>读取时宽松处理：非法项跳过，不因单个坏值让整份配置失效。</summary>
    private static List<string> NormalizeForRead(IEnumerable<string>? roots)
    {
        var list = new List<string>();
        if (roots is null) return list;

        foreach (var raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            try
            {
                var normalized = WorkspaceIdGenerator.Normalize(raw.Trim());
                if (normalized.Length > 0 && !list.Contains(normalized, StringComparer.Ordinal))
                {
                    list.Add(normalized);
                }
            }
            catch
            {
                // 单个坏值只跳过
            }
        }

        return list;
    }

    /// <summary>配置文件形状。</summary>
    private sealed class WorkspaceRootsFile
    {
        /// <summary>允许的工作区根目录。</summary>
        public List<string> Roots { get; set; } = [];
    }
}