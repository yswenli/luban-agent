/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebPluginService
*描述：Web 插件管理业务：列表、安装、升级、启停、卸载与页面镜像
*
*****************************************************************************/

using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Lifecycle;
using LuBan.AIAgent.Plugins.Loading;

using System.IO.Compression;

namespace LubanAgentWebHost.Services;

/// <summary>
/// Web 插件管理业务。
/// </summary>
/// <remarks>
/// <para>
/// 框架的安装入口把目标目录写死在宿主程序目录下的 <c>plugins</c>，Web 侧不能直接用；
/// 这里自己完成「解压 → 校验 → 复制到 Web 插件根 → 扫描登记」，启停/升级/卸载仍走框架。
/// </para>
/// <para>
/// 插件页面在启用后镜像到静态根下带 <c>stamp</c> 的目录；停用/卸载时整棵删除。
/// 镜像失败只降级为「页面不可用」，不影响插件本身与技能 / MCP。
/// </para>
/// </remarks>
public static class WebPluginService
{
    /// <summary>ZIP 条目数上限（与技能导入对齐）。</summary>
    private const int MaxEntries = 500;

    /// <summary>单个插件包上限（Base64 后仍低于请求体上限）。</summary>
    private const long MaxPackageBytes = 32L * 1024 * 1024;

    /// <summary>解压后总字节上限（防压缩炸弹）。</summary>
    private const long MaxExtractedBytes = 128L * 1024 * 1024;

    /// <summary>解压后单个文件上限。</summary>
    private const long MaxSingleFileBytes = 32L * 1024 * 1024;

    /// <summary>插件标识 → 当前页面镜像戳。</summary>
    private static readonly ConcurrentDictionary<string, string> Stamps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>插件标识 → 页面镜像失败原因。</summary>
    private static readonly ConcurrentDictionary<string, string> MirrorErrors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>镜像读写锁：重建与删除不能交错。</summary>
    private static readonly object MirrorGate = new();

    /// <summary>列出 Web 插件根下的插件。</summary>
    /// <returns>插件根与插件项。</returns>
    public static Task<ActionResult> ListAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var loader = scope.GetRequiredService<PluginLoader>();
        var manager = scope.GetRequiredService<PluginManager>();
        var root = WebRoot();

        return WebApi.OkJson(new PluginListDto(root, BuildItems(loader, manager, root)));
    });

    /// <summary>安装插件（成功后自动启用）。</summary>
    /// <param name="payload">包装后的 <see cref="PluginInstallRequest"/> JSON。</param>
    /// <returns>插件标识、状态、警告与最新插件项。</returns>
    public static Task<ActionResult> InstallAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PluginInstallRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (!TryDecodePackage(input.ContentBase64, out var bytes, out var decodeError))
        {
            return WebApi.FailJson(400, decodeError);
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var loader = scope.GetRequiredService<PluginLoader>();
        var manager = scope.GetRequiredService<PluginManager>();
        var webRoot = WebRoot();

        PreparedPlugin prepared;
        try
        {
            prepared = Prepare(loader, bytes);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            return WebApi.FailJson(400, ex.Message);
        }

        using (prepared)
        {
            var id = prepared.Manifest.Id;
            var target = Path.Combine(webRoot, id);

            // 桌面端根也在启动扫描范围内，桌面端已有的插件同样会登记；这类插件 Web 端既装不了也升级不了，
            // 因此必须先按目录归属区分，否则会先回「请改用升级」、再被升级接口以「只能管理 Web 插件目录」拒绝
            if (manager.GetPlugin(id) is { } existing
                && !WebPluginPaths.IsUnder(existing.Directory, webRoot))
            {
                return WebApi.FailJson(400, DesktopConflictMessage(id));
            }

            if (Directory.Exists(Path.Combine(DesktopRoot(), id)))
            {
                return WebApi.FailJson(400, DesktopConflictMessage(id));
            }

            if (manager.GetPlugin(id) is not null)
            {
                return WebApi.FailJson(400, $"插件 {id} 已存在，请改用「升级」。");
            }

            if (Directory.Exists(target) && !File.Exists(Path.Combine(target, WebPluginPaths.ManifestFileName)))
            {
                return WebApi.FailJson(
                    400,
                    $"Web 插件目录里有插件 {id} 的上次卸载残留（缺少 plugin.json），"
                    + "请重启宿主让其自动清理后再安装，或手动删除：" + target);
            }

            if (Directory.Exists(target))
            {
                return WebApi.FailJson(400, $"插件 {id} 已存在，请改用「升级」。");
            }

            if (!WebPluginPaths.IsUnder(target, webRoot))
            {
                return WebApi.FailJson(400, "插件标识不能用作目录名，请调整 plugin.json 的 id。");
            }

            try
            {
                Directory.CreateDirectory(webRoot);
                CopyDirectory(prepared.SourceDirectory, target);
            }
            catch
            {
                TryDeleteDirectory(target);
                throw;
            }

            try
            {
                await manager.ScanAsync(PluginRoots(webRoot)).ConfigureAwait(false);
            }
            catch
            {
                // 登记失败时目录还在盘上会挡住下次安装（同 id 提示已存在），这里一并清理
                TryDeleteDirectory(target);
                throw;
            }

            await manager.EnableAsync(id).ConfigureAwait(false);

            RebuildMirror(loader, manager, webRoot, id);
            var item = BuildItem(loader, manager, webRoot, id);

            // 启用失败（入口程序集加载失败、依赖缺失等）不影响安装结果，但必须让用户当场知道
            var warnings = prepared.Warnings;
            if (item is not null && item.State != "enabled")
            {
                warnings = warnings
                    .Append(item.LastError is { Length: > 0 } reason
                        ? $"安装成功但启用失败：{reason}"
                        : "安装成功但启用失败，可在详情页查看原因后重试启用。")
                    .ToList();
            }

            return WebApi.OkJson(new PluginChangeResultDto(
                id,
                item?.State ?? "installed",
                warnings,
                item));
        }
    });

    /// <summary>升级插件（成功后恢复升级前的启用状态）。</summary>
    /// <param name="payload">包装后的 <see cref="PluginUpgradeRequest"/> JSON。</param>
    /// <returns>插件标识、状态、警告与最新插件项。</returns>
    public static Task<ActionResult> UpgradeAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PluginUpgradeRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        var id = input.Id?.Trim() ?? string.Empty;
        if (id.Length == 0)
        {
            return WebApi.FailJson(400, "插件标识不能为空");
        }

        if (!TryDecodePackage(input.ContentBase64, out var bytes, out var decodeError))
        {
            return WebApi.FailJson(400, decodeError);
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var loader = scope.GetRequiredService<PluginLoader>();
        var manager = scope.GetRequiredService<PluginManager>();
        var webRoot = WebRoot();

        if (!TryResolveManagedPlugin(manager, webRoot, id, out var denied))
        {
            return denied!;
        }

        PreparedPlugin prepared;
        try
        {
            prepared = Prepare(loader, bytes);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            return WebApi.FailJson(400, ex.Message);
        }

        using (prepared)
        {
            if (!string.Equals(prepared.Manifest.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return WebApi.FailJson(
                    400,
                    $"包内插件标识（{prepared.Manifest.Id}）与要升级的插件（{id}）不一致。");
            }

            // 框架升级是「先删旧目录、再拷新包」，已加载的程序集文件仍被占用时删除会中途失败，
            // 留下半删的插件目录；因此旧版本未释放时先拒绝，释放后升级也先留一份快照用于回补。
            var before = manager.GetPlugin(id);
            if (before?.RequiresRestart == true)
            {
                return WebApi.FailJson(400, "旧版本程序集尚未释放，请重启宿主后再升级。");
            }

            var snapshot = TrySnapshotDirectory(before?.Directory);
            try
            {
                bool upgraded;
                try
                {
                    upgraded = await manager.UpgradeAsync(id, prepared.SourceDirectory).ConfigureAwait(false);
                }
                catch
                {
                    RestoreMissingFiles(snapshot, before?.Directory);
                    throw;
                }

                if (!upgraded)
                {
                    var restored = RestoreMissingFiles(snapshot, before?.Directory);
                    return manager.GetPlugin(id)?.RequiresRestart == true
                        ? WebApi.FailJson(400, "旧版本程序集尚未释放，请重启宿主后再升级。")
                        : WebApi.FailJson(
                            400,
                            restored > 0
                                ? "升级失败：旧版本文件仍被占用，已恢复插件目录；请重启宿主后再升级。"
                                : "升级失败：新包未通过校验或复制出错，请检查后重试。");
                }

                RebuildMirror(loader, manager, webRoot, id);
                var item = BuildItem(loader, manager, webRoot, id);
                return WebApi.OkJson(new PluginChangeResultDto(
                    id,
                    item?.State ?? "installed",
                    prepared.Warnings,
                    item));
            }
            finally
            {
                TryDeleteDirectory(snapshot);
            }
        }
    });

    /// <summary>启用或停用插件。</summary>
    /// <param name="payload">包装后的 <see cref="PluginIdRequest"/> JSON。</param>
    /// <param name="enabled">true 启用，false 停用。</param>
    /// <returns>操作后的插件项（启用失败时仍回 200，原因在 lastError）。</returns>
    public static Task<ActionResult> SetEnabledAsync(string payload, bool enabled)
        => WebApi.RunGuardedAsync(async () =>
        {
            if (!WebApi.TryReadPayload<PluginIdRequest>(payload, out var input) || input is null)
            {
                return WebApi.FailJson(400, "请求体不是合法的 JSON");
            }

            var id = input.Id?.Trim() ?? string.Empty;
            if (id.Length == 0)
            {
                return WebApi.FailJson(400, "插件标识不能为空");
            }

            await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
            var loader = scope.GetRequiredService<PluginLoader>();
            var manager = scope.GetRequiredService<PluginManager>();
            var webRoot = WebRoot();

            if (!TryResolveManagedPlugin(manager, webRoot, id, out var denied))
            {
                return denied!;
            }

            if (enabled)
            {
                await manager.EnableAsync(id).ConfigureAwait(false);
            }
            else
            {
                await manager.DisableAsync(id).ConfigureAwait(false);
            }

            RebuildMirror(loader, manager, webRoot, id);
            var item = BuildItem(loader, manager, webRoot, id);
            return WebApi.OkJson(new PluginChangeResultDto(
                id,
                item?.State ?? (enabled ? "installed" : "disabled"),
                Array.Empty<string>(),
                item));
        });

    /// <summary>卸载插件（含停用与删除插件目录）。</summary>
    /// <param name="payload">包装后的 <see cref="PluginIdRequest"/> JSON。</param>
    /// <returns>卸载结果（不含插件项）。</returns>
    public static Task<ActionResult> UninstallAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PluginIdRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        var id = input.Id?.Trim() ?? string.Empty;
        if (id.Length == 0)
        {
            return WebApi.FailJson(400, "插件标识不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var manager = scope.GetRequiredService<PluginManager>();
        var webRoot = WebRoot();

        if (!TryResolveManagedPlugin(manager, webRoot, id, out var denied))
        {
            return denied!;
        }

        if (!await manager.UninstallAsync(id).ConfigureAwait(false))
        {
            return WebApi.FailJson(400, "卸载失败：插件可能仍在使用中，请稍后重试。");
        }

        RemoveMirror(id);

        // 已加载的程序集文件在进程内仍被占用，目录常常删不干净；启动时会按「无 plugin.json」清理
        var target = Path.Combine(webRoot, id);
        TryDeleteDirectory(target);
        var warnings = Directory.Exists(target)
            ? new[] { "插件已停用并注销；程序集文件仍被宿主占用，重启宿主后会自动清理残留目录。" }
            : Array.Empty<string>();

        return WebApi.OkJson(new PluginChangeResultDto(id, "uninstalled", warnings, null));
    });

    /// <summary>
    /// 清空并重建全部插件页面镜像（宿主启动时执行一次）。
    /// </summary>
    /// <remarks>
    /// 输出目录可能被重建或由只读部署覆盖，启动重建可自愈；重复执行幂等。
    /// </remarks>
    /// <param name="scope">用户作用域。</param>
    /// <returns>任务。</returns>
    public static Task RebuildMirrorsAsync(DiContainer scope)
    {
        var loader = scope.GetService<PluginLoader>();
        var manager = scope.GetService<PluginManager>();
        if (loader is null || manager is null)
        {
            return Task.CompletedTask;
        }

        var webRoot = WebRoot();

        // 卸载已启用插件时，进程内仍占用的程序集文件删不掉，会留下没有 plugin.json 的空壳目录，
        // 挡住同 id 重新安装；启动时把这类未登记的孤儿目录清掉（删不掉就留到下次启动）。
        CleanupOrphanDirectories(manager, webRoot);

        lock (MirrorGate)
        {
            TryDeleteDirectory(WebPluginPaths.MirrorRoot);
            Stamps.Clear();
            MirrorErrors.Clear();
            try
            {
                Directory.CreateDirectory(WebPluginPaths.MirrorRoot);
            }
            catch (Exception ex)
            {
                AgentWebRuntime.Log($"插件页面镜像根创建失败：{ex.Message}");
            }
        }

        foreach (var descriptor in manager.GetPlugins())
        {
            if (string.IsNullOrWhiteSpace(descriptor.Directory)
                || !WebPluginPaths.IsUnder(descriptor.Directory, webRoot))
            {
                continue;
            }

            RebuildMirror(loader, manager, webRoot, descriptor.Id);
        }

        return Task.CompletedTask;
    }

    // ---------------- 插件项组装 ----------------

    private static IReadOnlyList<PluginItemDto> BuildItems(
        PluginLoader loader, PluginManager manager, string webRoot)
    {
        var items = new List<PluginItemDto>();
        if (!Directory.Exists(webRoot))
        {
            return items;
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(webRoot)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentWebRuntime.Log($"枚举插件目录失败：{ex.Message}");
            return items;
        }

        foreach (var directory in directories)
        {
            var manifest = loader.ReadManifest(directory);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                continue;
            }

            items.Add(BuildItem(manifest, manager.GetPlugin(manifest.Id), directory));
        }

        return items;
    }

    private static PluginItemDto? BuildItem(
        PluginLoader loader, PluginManager manager, string webRoot, string pluginId)
    {
        var descriptor = manager.GetPlugin(pluginId);
        var directory = descriptor?.Directory ?? Path.Combine(webRoot, pluginId);
        var manifest = loader.ReadManifest(directory) ?? descriptor?.Manifest;
        return manifest is null ? null : BuildItem(manifest, descriptor, directory);
    }

    private static PluginItemDto BuildItem(
        PluginManifest manifest, PluginDescriptor? descriptor, string directory)
    {
        var stamp = Stamps.TryGetValue(manifest.Id, out var current) ? current : null;
        var pages = PluginPageManifest.ReadValid(directory)
            .Select(entry => new PluginPageDto(
                entry.EntryId,
                entry.Label,
                entry.Icon,
                entry.WebPage,
                stamp is null ? string.Empty : WebPluginPaths.PageUrl(manifest.Id, stamp, entry.WebPage)))
            .ToList();

        return new PluginItemDto(
            manifest.Id,
            manifest.Name,
            manifest.Version,
            manifest.Author,
            manifest.Description,
            StateText(descriptor?.State ?? PluginState.Installed),
            descriptor?.IsBuiltin ?? false,
            descriptor?.RequiresRestart ?? false,
            descriptor?.LastError,
            manifest.Skills
                .Select(skill => new PluginSkillDto(skill.Id, skill.Name, skill.Description))
                .ToList(),
            manifest.McpServers
                .Select(server => new PluginMcpDto(server.Name, server.Transport))
                .ToList(),
            pages,
            MirrorErrors.TryGetValue(manifest.Id, out var error) ? error : null);
    }

    private static string StateText(PluginState state) => state switch
    {
        PluginState.Enabled => "enabled",
        PluginState.Disabled => "disabled",
        PluginState.Error => "error",
        _ => "installed"
    };

    // ---------------- 安装包处理 ----------------

    /// <summary>解包并校验，返回解压目录与清单；失败抛 <see cref="InvalidOperationException"/>。</summary>
    private static PreparedPlugin Prepare(PluginLoader loader, byte[] bytes)
    {
        var temp = Path.Combine(Path.GetTempPath(), "luban-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            ExtractZip(bytes, temp);

            var source = LocatePluginRoot(temp)
                ?? throw new InvalidOperationException(
                    "插件包里没有找到 plugin.json：请把清单放在压缩包根目录，或只保留一个含 plugin.json 的顶层目录。");

            var manifest = loader.ReadManifest(source)
                ?? throw new InvalidOperationException("plugin.json 解析失败，请检查 JSON 格式与必填字段。");

            var validation = loader.Validate(manifest, source);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException("插件清单校验未通过：" + validation.ToSummary());
            }

            var pageErrors = PluginPageManifest.Validate(source);
            if (pageErrors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(" ", pageErrors));
            }

            if (FindControllerConflict(source) is { } conflict)
            {
                throw new InvalidOperationException(
                    $"web/ 下的目录名「{conflict}」与宿主接口同名，其中的资源会被当成路由，请改名后重新打包。");
            }

            return new PreparedPlugin
            {
                TempDirectory = temp,
                SourceDirectory = source,
                Manifest = manifest,
                Warnings = CollectAssetWarnings(source)
            };
        }
        catch
        {
            TryDeleteDirectory(temp);
            throw;
        }
    }

    /// <summary>解包到目标目录；条目数 / 体积 / 越界路径任一不合规即抛 <see cref="InvalidOperationException"/>。</summary>
    internal static void ExtractZip(byte[] bytes, string targetRoot)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidOperationException($"插件包条目数超过上限 {MaxEntries}，请精简后再上传。");
        }

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            if (entry.Length > MaxSingleFileBytes)
            {
                throw new InvalidOperationException(
                    $"插件包内单个文件超过上限 {MaxSingleFileBytes / 1024 / 1024}MB。");
            }

            var relative = WebPluginPaths.NormalizeRelative(entry.FullName)
                ?? throw new InvalidOperationException($"插件包包含非法路径：{entry.FullName}");

            string target;
            try
            {
                target = Path.GetFullPath(
                    Path.Combine(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidOperationException($"插件包包含无法写入的路径：{entry.FullName}", ex);
            }

            if (!WebPluginPaths.IsUnder(target, targetRoot))
            {
                throw new InvalidOperationException($"插件包包含越界路径：{entry.FullName}");
            }

            // 体积按解压后的实际字节累计：zip 头里的声明长度可被伪造，只能拿它做快速预筛，
            // 真正防压缩炸弹的是下面按实际写入量判定的两道上限
            long fileBytes = 0;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = entry.Open();
                using var destination = File.Create(target);
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fileBytes += read;
                    totalBytes += read;

                    if (fileBytes > MaxSingleFileBytes)
                    {
                        throw new InvalidOperationException(
                            $"插件包内单个文件超过上限 {MaxSingleFileBytes / 1024 / 1024}MB。");
                    }

                    if (totalBytes > MaxExtractedBytes)
                    {
                        throw new InvalidOperationException(
                            $"插件包解压后总大小超过上限 {MaxExtractedBytes / 1024 / 1024}MB，请精简后再上传。");
                    }

                    destination.Write(buffer, 0, read);
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // 保留名（CON/NUL 等）与非法字符在这里才会暴露，转成和其余校验一致的可读拒绝原因
                throw new InvalidOperationException($"插件包内文件写入失败：{entry.FullName}", ex);
            }
        }
    }

    /// <summary>定位插件根：清单在解压根目录，或只存在一个含清单的顶层目录。</summary>
    internal static string? LocatePluginRoot(string extracted)
    {
        if (File.Exists(Path.Combine(extracted, WebPluginPaths.ManifestFileName)))
        {
            return extracted;
        }

        var directories = Directory.EnumerateDirectories(extracted).ToList();
        return directories.Count == 1
            && File.Exists(Path.Combine(directories[0], WebPluginPaths.ManifestFileName))
                ? directories[0]
                : null;
    }

    /// <summary>找出 web/ 下与宿主控制器同名的目录（取第一个）。</summary>
    private static string? FindControllerConflict(string pluginDirectory)
    {
        var webRoot = Path.Combine(pluginDirectory, WebPluginPaths.WebFolderName);
        if (!Directory.Exists(webRoot))
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(webRoot, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(directory);
            if (WebPluginPaths.IsHostControllerDirectoryName(name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>收集白名单外扩展名的提示（不阻断安装）。</summary>
    private static IReadOnlyList<string> CollectAssetWarnings(string pluginDirectory)
    {
        var webRoot = Path.Combine(pluginDirectory, WebPluginPaths.WebFolderName);
        if (!Directory.Exists(webRoot))
        {
            return Array.Empty<string>();
        }

        var warnings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(pluginDirectory, file).Replace('\\', '/');
            if (!WebPluginPaths.IsMirrorAsset(relative))
            {
                warnings.Add($"{relative} 的扩展名不在 Web 端白名单内，浏览器可能拒绝加载。");
            }
        }

        return warnings;
    }

    private static bool TryDecodePackage(string? contentBase64, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(contentBase64))
        {
            error = "ZIP 内容不能为空";
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException)
        {
            error = "ZIP 内容不是合法的 Base64";
            return false;
        }

        if (bytes.Length == 0)
        {
            error = "ZIP 内容不能为空";
            return false;
        }

        if (bytes.Length > MaxPackageBytes)
        {
            error = $"插件包超过 {MaxPackageBytes / 1024 / 1024}MB 上限";
            return false;
        }

        return true;
    }

    // ---------------- 页面镜像 ----------------

    private static void RebuildMirror(
        PluginLoader loader, PluginManager manager, string webRoot, string pluginId)
    {
        lock (MirrorGate)
        {
            RemoveMirrorCore(pluginId);

            var descriptor = manager.GetPlugin(pluginId);
            if (descriptor is null || descriptor.State != PluginState.Enabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(descriptor.Directory)
                || !WebPluginPaths.IsUnder(descriptor.Directory, webRoot))
            {
                return;
            }

            if (PluginPageManifest.ReadValid(descriptor.Directory).Count == 0)
            {
                return;
            }

            var sourceWeb = Path.Combine(descriptor.Directory, WebPluginPaths.WebFolderName);
            if (!Directory.Exists(sourceWeb))
            {
                return;
            }

            var stamp = WebPluginPaths.CreateStamp();
            var target = Path.Combine(WebPluginPaths.MirrorRoot, pluginId, stamp);
            try
            {
                CopyDirectory(sourceWeb, Path.Combine(target, WebPluginPaths.WebFolderName));
                Stamps[pluginId] = stamp;
            }
            catch (Exception ex)
            {
                AgentWebRuntime.Log($"插件页面镜像写入失败（{pluginId}）：{ex.Message}");
                MirrorErrors[pluginId] = WebApi.SafeErrorMessage(ex);
                TryDeleteDirectory(target);
            }
        }
    }

    private static void RemoveMirror(string pluginId)
    {
        lock (MirrorGate)
        {
            RemoveMirrorCore(pluginId);
        }
    }

    private static void RemoveMirrorCore(string pluginId)
    {
        Stamps.TryRemove(pluginId, out _);
        MirrorErrors.TryRemove(pluginId, out _);

        var directory = Path.Combine(WebPluginPaths.MirrorRoot, pluginId);
        if (WebPluginPaths.IsUnder(directory, WebPluginPaths.MirrorRoot))
        {
            TryDeleteDirectory(directory);
        }
    }

    // ---------------- 通用 ----------------

    /// <summary>
    /// 只允许操作 Web 插件根下的非内置插件。
    /// </summary>
    private static bool TryResolveManagedPlugin(
        PluginManager manager, string webRoot, string pluginId, out ActionResult? denied)
    {
        denied = null;
        var descriptor = manager.GetPlugin(pluginId);
        if (descriptor is null)
        {
            denied = WebApi.FailJson(404, "插件未登记，请刷新列表后重试。");
            return false;
        }

        if (descriptor.IsBuiltin)
        {
            denied = WebApi.FailJson(400, "内置插件不支持该操作。");
            return false;
        }

        if (!WebPluginPaths.IsUnder(descriptor.Directory, webRoot))
        {
            denied = WebApi.FailJson(400, "只能管理 Web 插件目录下的插件。");
            return false;
        }

        return true;
    }

    private static string DesktopConflictMessage(string pluginId)
        => $"插件 {pluginId} 已在桌面端（Codex）插件目录中，Web 端不能重复安装同一标识。";

    private static IReadOnlyList<string> PluginRoots(string webRoot)
        => new[] { webRoot, DesktopRoot() }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string WebRoot()
        => WebPluginPaths.ResolveWebPluginsRoot(WebApi.Runtime.Settings.WebPluginsDir);

    private static string DesktopRoot()
        => WebPluginPaths.ResolveDesktopPluginsRoot(WebApi.Runtime.Settings.PluginsDir);

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteDirectory(string? directory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentWebRuntime.Log($"删除插件目录失败（{directory}）：{ex.Message}");
        }
    }

    /// <summary>
    /// 清理 Web 插件根下没有 <c>plugin.json</c>、也未登记的孤儿目录（卸载残留）。
    /// </summary>
    /// <param name="manager">插件管理器（已登记项不算残留）。</param>
    /// <param name="webRoot">Web 插件根。</param>
    private static void CleanupOrphanDirectories(PluginManager manager, string webRoot)
    {
        if (!Directory.Exists(webRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(webRoot))
        {
            var id = Path.GetFileName(directory);
            if (manager.GetPlugin(id) is not null)
            {
                continue;
            }

            if (File.Exists(Path.Combine(directory, WebPluginPaths.ManifestFileName)))
            {
                continue;
            }

            AgentWebRuntime.Log($"清理插件卸载残留目录：{directory}");
            TryDeleteDirectory(directory);
        }
    }

    /// <summary>
    /// 升级前把旧插件目录整体复制到临时目录。
    /// </summary>
    /// <remarks>
    /// 框架升级先删旧目录再拷新包，已加载的程序集文件被占用时删除会中途失败并留下半删目录。
    /// 快照只用于失败后补回缺失文件；复制失败时返回 <c>null</c>，退化为不做回补。
    /// </remarks>
    /// <param name="source">旧插件目录。</param>
    /// <returns>快照目录；无法复制时为 <c>null</c>。</returns>
    private static string? TrySnapshotDirectory(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            return null;
        }

        var snapshot = Path.Combine(
            Path.GetTempPath(),
            "luban-plugin-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(source, snapshot);
            return snapshot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentWebRuntime.Log($"插件升级快照失败（{source}）：{ex.Message}");
            TryDeleteDirectory(snapshot);
            return null;
        }
    }

    /// <summary>
    /// 按快照补回升级失败时被删掉的文件；目标目录里仍存在的文件（被占用、内容完好）不覆盖。
    /// </summary>
    /// <param name="snapshot">升级前快照目录。</param>
    /// <param name="target">插件目录。</param>
    /// <returns>补回的文件数。</returns>
    internal static int RestoreMissingFiles(string? snapshot, string? target)
    {
        if (string.IsNullOrWhiteSpace(snapshot)
            || string.IsNullOrWhiteSpace(target)
            || !Directory.Exists(snapshot))
        {
            return 0;
        }

        var restored = 0;
        foreach (var file in Directory.EnumerateFiles(snapshot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(snapshot, file));
            try
            {
                if (File.Exists(destination))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: false);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentWebRuntime.Log($"插件目录回补失败（{destination}）：{ex.Message}");
            }
        }

        return restored;
    }

    /// <summary>解压到临时目录后的校验结果。</summary>
    private sealed class PreparedPlugin : IDisposable
    {
        public required string TempDirectory { get; init; }

        public required string SourceDirectory { get; init; }

        public required PluginManifest Manifest { get; init; }

        public required IReadOnlyList<string> Warnings { get; init; }

        public void Dispose() => TryDeleteDirectory(TempDirectory);
    }
}