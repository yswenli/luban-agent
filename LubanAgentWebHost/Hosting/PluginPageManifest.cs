/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： PluginPageManifest
*描述：从原始 plugin.json 读取页面声明（框架清单模型不含 webPage）
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 插件页面声明（<c>uiContributions[].webPage</c>）。
/// </summary>
/// <param name="EntryId">条目标识。</param>
/// <param name="Label">显示名。</param>
/// <param name="Icon">图标标识。</param>
/// <param name="WebPage">已归一化的包内相对路径（如 <c>web/index.html</c>）。</param>
public sealed record PluginPageEntry(string EntryId, string Label, string? Icon, string WebPage);

/// <summary>
/// 读取并校验 <c>plugin.json</c> 里的页面声明。
/// </summary>
/// <remarks>
/// 框架的清单模型只保留 <c>id/label/icon/panelType</c>，<c>webPage</c> 会在反序列化时被丢弃，
/// 因此页面声明只能从插件目录里的原始 JSON 再读一次。
/// </remarks>
public static class PluginPageManifest
{
    /// <summary>
    /// 读取全部页面声明（只保留声明了非空 <c>webPage</c> 的条目，路径已归一化）。
    /// </summary>
    /// <param name="pluginDirectory">插件目录。</param>
    /// <returns>页面声明；清单缺失或解析失败时返回空列表。</returns>
    public static IReadOnlyList<PluginPageEntry> Read(string pluginDirectory)
    {
        var entries = new List<PluginPageEntry>();
        foreach (var raw in ReadRaw(pluginDirectory))
        {
            if (WebPluginPaths.NormalizeRelative(raw.WebPage) is not { } normalized)
            {
                continue;
            }

            entries.Add(new PluginPageEntry(raw.EntryId, raw.Label, raw.Icon, normalized));
        }

        return entries;
    }

    /// <summary>
    /// 读取页面声明并只保留校验通过（位于 <c>web/</c> 下、是 HTML 且文件存在）的条目。
    /// </summary>
    /// <param name="pluginDirectory">插件目录。</param>
    /// <returns>可用的页面声明。</returns>
    public static IReadOnlyList<PluginPageEntry> ReadValid(string pluginDirectory)
        => Read(pluginDirectory)
            .Where(entry => IsUsable(pluginDirectory, entry.WebPage))
            .ToList();

    /// <summary>
    /// 校验全部页面声明，返回面向用户的拒绝原因（不含本机路径）。
    /// </summary>
    /// <param name="pluginDirectory">插件目录。</param>
    /// <returns>错误列表；全部通过时为空。</returns>
    public static IReadOnlyList<string> Validate(string pluginDirectory)
    {
        var errors = new List<string>();
        foreach (var raw in ReadRaw(pluginDirectory))
        {
            var label = string.IsNullOrWhiteSpace(raw.Label) ? raw.EntryId : raw.Label;
            var normalized = WebPluginPaths.NormalizeRelative(raw.WebPage);

            if (normalized is null)
            {
                errors.Add($"页面入口「{label}」的 webPage 非法：必须是包内相对路径，不能包含 ..、盘符或绝对路径。");
                continue;
            }

            if (!WebPluginPaths.IsUnderWebFolder(normalized))
            {
                errors.Add($"页面入口「{label}」的 webPage 必须位于 web/ 目录下（当前为 {normalized}）。");
                continue;
            }

            if (!WebPluginPaths.IsHtmlPage(normalized))
            {
                errors.Add($"页面入口「{label}」的 webPage 必须指向 .html 或 .htm 文件（当前为 {normalized}）。");
                continue;
            }

            var fullPath = ToPhysicalPath(pluginDirectory, normalized);
            if (fullPath is null || !WebPluginPaths.IsUnder(fullPath, pluginDirectory))
            {
                errors.Add($"页面入口「{label}」的 webPage 越出了插件目录。");
                continue;
            }

            if (!File.Exists(fullPath))
            {
                errors.Add($"页面入口「{label}」指向的文件不存在：{normalized}。");
            }
        }

        return errors;
    }

    /// <summary>
    /// 判断单条页面声明能否在 Web 端使用。
    /// </summary>
    /// <param name="pluginDirectory">插件目录。</param>
    /// <param name="normalized">已归一化的相对路径。</param>
    /// <returns>可用返回 true。</returns>
    private static bool IsUsable(string pluginDirectory, string normalized)
    {
        if (!WebPluginPaths.IsUnderWebFolder(normalized) || !WebPluginPaths.IsHtmlPage(normalized))
        {
            return false;
        }

        var fullPath = ToPhysicalPath(pluginDirectory, normalized);
        return fullPath is not null
            && WebPluginPaths.IsUnder(fullPath, pluginDirectory)
            && File.Exists(fullPath);
    }

    /// <summary>
    /// 把包内相对路径拼成物理路径。
    /// </summary>
    /// <param name="pluginDirectory">插件目录。</param>
    /// <param name="normalized">已归一化的相对路径。</param>
    /// <returns>物理路径；拼接失败返回 <c>null</c>。</returns>
    private static string? ToPhysicalPath(string pluginDirectory, string normalized)
    {
        try
        {
            return Path.GetFullPath(
                Path.Combine(pluginDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// 只读原始 JSON 里的 <c>uiContributions</c>，不做路径校验。
    /// </summary>
    /// <param name="pluginDirectory">插件目录。</param>
    /// <returns>声明条目（<see cref="RawPageEntry.WebPage"/> 为原始字符串）。</returns>
    private static IReadOnlyList<RawPageEntry> ReadRaw(string pluginDirectory)
    {
        var result = new List<RawPageEntry>();
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            return result;
        }

        var manifestPath = Path.Combine(pluginDirectory, WebPluginPaths.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return result;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(manifestPath),
                new System.Text.Json.JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip
                });

            if (!TryGetProperty(document.RootElement, "uiContributions", out var contributions)
                || contributions.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in contributions.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    continue;
                }

                var entryId = GetString(item, "id") ?? string.Empty;
                var webPage = GetString(item, "webPage");
                if (string.IsNullOrWhiteSpace(webPage))
                {
                    continue;
                }

                result.Add(new RawPageEntry(
                    entryId,
                    GetString(item, "label") ?? entryId,
                    GetString(item, "icon"),
                    webPage!));
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            AgentWebRuntime.Log($"读取插件页面声明失败（{manifestPath}）：{ex.Message}");
        }

        return result;
    }

    /// <summary>按名称取属性（大小写不敏感）。</summary>
    private static bool TryGetProperty(
        System.Text.Json.JsonElement element, string name, out System.Text.Json.JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>取字符串属性；缺失或类型不符返回 <c>null</c>。</summary>
    private static string? GetString(System.Text.Json.JsonElement element, string name)
        => TryGetProperty(element, name, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;

    /// <summary>原始声明（未校验路径）。</summary>
    private sealed record RawPageEntry(string EntryId, string Label, string? Icon, string WebPage);
}