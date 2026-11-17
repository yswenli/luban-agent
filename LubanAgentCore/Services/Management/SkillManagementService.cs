/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management
*文件名： SkillManagementService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：Skill 管理实现（内置 + 文件型 + config 自定义）。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：Skill 管理实现（内置 + 文件型 + config 自定义）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management;

/// <summary>
/// Skill 管理实现（内置 + 文件型 + config 自定义）。
/// 业务规则全部委托 <see cref="ConfigManager"/>（持久化）与 <see cref="SkillRegistry"/>（三级合并视图），
/// 文件后端直接操作 <c>SKILL.md</c>（与 Codex 设置页原逻辑逐字一致）。
/// </summary>
public sealed class SkillManagementService : ISkillManagementService
{
    /// <summary>遗留用户级目录（旧版写入位置，<see cref="SkillLoader"/> 仍兼容读取）。</summary>
    private static readonly string LegacyUserSkillsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuBanFramework", "AIAgent", "skills");

    /// <summary>单个 SKILL.md 的落盘上限（字节），对齐框架 SkillManageToolOptions.MaxSkillFileBytes。</summary>
    private const long MaxSkillFileBytes = 256 * 1024;

    /// <summary>一次导入的 ZIP 条目数上限，避免超大包把导入拖死。</summary>
    private const int MaxImportEntries = 500;

    /// <summary>一次导入的解压总字节上限（防压缩炸弹）。</summary>
    private const long MaxImportTotalBytes = 8 * 1024 * 1024;

    private readonly ConfigManager _config;
    private readonly SkillRegistry _registry;
    private readonly IWorkspaceManager _workspaces;

    /// <summary>
    /// 创建 Skill 管理服务
    /// </summary>
    public SkillManagementService(ConfigManager config, SkillRegistry registry, IWorkspaceManager workspaces)
    {
        _config = config;
        _registry = registry;
        _workspaces = workspaces;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillListItem>> ListAsync(CancellationToken ct = default)
    {
        var workspaceRoot = _workspaces.CurrentWorkspace?.RootPath;
        var fileIds = CollectOriginFileIds(workspaceRoot);
        var configSkills = _config.CustomSkills.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var items = new List<SkillListItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in _registry.GetAll())
        {
            seen.Add(skill.Id);

            // 合并优先级：工作区文件 > 硬编码 > config.json；有同名文件时来源记为文件型
            var origin = fileIds.Contains(skill.Id)
                ? ManagedOrigin.File
                : _registry.IsBuiltin(skill.Id) ? ManagedOrigin.Builtin : ManagedOrigin.Config;

            // 进入合并视图的内置 / 文件型项本身即「启用」；config 项另读 Enabled（禁用项不进合并视图）
            var enabled = origin != ManagedOrigin.Config
                          || (configSkills.TryGetValue(skill.Id, out var cfg) && cfg.Enabled);

            items.Add(new SkillListItem(
                skill.Id,
                skill.Name,
                skill.Description,
                skill.Category,
                skill.Examples?.ToList() ?? new List<string>(),
                enabled,
                origin));
        }

        // 已禁用的内置 Skill 不在合并结果中，单独补出（CLI 表格的「已禁用的内置 Skill」分组）
        foreach (var id in _config.DisabledBuiltinSkills)
        {
            if (seen.Contains(id)) continue;
            items.Add(new SkillListItem(id, id, "", "", Array.Empty<string>(), false, ManagedOrigin.Builtin));
        }

        // 已禁用的自定义 Skill 同样不在合并结果中
        foreach (var cfg in _config.CustomSkills)
        {
            if (cfg.Enabled || seen.Contains(cfg.Id)) continue;
            items.Add(new SkillListItem(
                cfg.Id,
                cfg.Name,
                cfg.Description,
                cfg.Category,
                cfg.Examples?.ToList() ?? new List<string>(),
                false,
                ManagedOrigin.Config));
        }

        return Task.FromResult<IReadOnlyList<SkillListItem>>(items);
    }

    /// <inheritdoc />
    public Task AddAsync(CustomSkillConfig skill, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var id = skill.Id?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Skill ID 不能为空", nameof(skill));

        if (_registry.Get(id) != null)
            throw new InvalidOperationException($"ID '{id}' 已存在");

        if (_config.CustomSkills.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"ID '{id}' 已存在于自定义 Skill 中");

        if (string.IsNullOrEmpty(skill.PromptTemplate))
            throw new ArgumentException("提示词模板不能为空", nameof(skill));

        skill.Id = id;
        _config.AddCustomSkill(skill);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(CustomSkillConfig skill, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(skill);
        _config.UpdateCustomSkill(skill);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default)
    {
        var safeId = EnsureSafeName(id, nameof(id));

        switch (origin)
        {
            case ManagedOrigin.Builtin:
                throw new InvalidOperationException($"内置 Skill '{safeId}' 不可删除");

            case ManagedOrigin.Config:
                _config.RemoveCustomSkill(safeId);
                break;

            case ManagedOrigin.File:
                var dir = Path.Combine(ResolveDir(workspaceRoot), safeId);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var safeId = EnsureSafeName(id, nameof(id));

        if (CollectOriginFileIds(_workspaces.CurrentWorkspace?.RootPath).Contains(safeId))
        {
            throw new NotSupportedException(
                $"文件型 Skill '{safeId}' 不支持在此启用/禁用，请直接编辑 .luban-agent/skills 目录");
        }

        if (_registry.IsBuiltin(safeId))
        {
            _config.SetBuiltinSkillEnabled(safeId, enabled);
        }
        else if (_config.CustomSkills.Any(c => c.Id.Equals(safeId, StringComparison.OrdinalIgnoreCase)))
        {
            _config.SetCustomSkillEnabled(safeId, enabled);
        }
        else
        {
            throw new InvalidOperationException($"Skill '{safeId}' 不存在");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFileIds(string? workspaceRoot)
    {
        var dir = ResolveDir(workspaceRoot);
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        return Directory.GetDirectories(dir)
            .Where(d => File.Exists(Path.Combine(d, "SKILL.md")))
            .Select(d => Path.GetFileName(d))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<SkillFileEntry> ListFileEntries(string? workspaceRoot)
    {
        var dir = ResolveDir(workspaceRoot);
        if (!Directory.Exists(dir)) return Array.Empty<SkillFileEntry>();

        var entries = new List<SkillFileEntry>();
        foreach (var skillDir in Directory.GetDirectories(dir))
        {
            var id = Path.GetFileName(skillDir);
            var path = Path.Combine(skillDir, "SKILL.md");
            if (string.IsNullOrWhiteSpace(id) || !File.Exists(path)) continue;

            var (name, _, category, _, _) = ParseSkillMarkdown(File.ReadAllText(path));
            entries.Add(new SkillFileEntry(
                id,
                string.IsNullOrWhiteSpace(name) ? id : name,
                category ?? ""));
        }

        return entries
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public SkillFileContent? ReadFile(string id, string? workspaceRoot)
    {
        var safeId = EnsureSafeName(id, nameof(id));
        var path = Path.Combine(ResolveDir(workspaceRoot), safeId, "SKILL.md");
        if (!File.Exists(path)) return null;

        var (name, description, category, triggers, body) = ParseSkillMarkdown(File.ReadAllText(path));
        return new SkillFileContent(name, description, category, triggers, body);
    }

    /// <inheritdoc />
    public string SaveFile(string? originalId, SkillFileContent content, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(content);

        var displayName = content.Name?.Trim() ?? "";
        // 校验信息直接回给两端 UI（Web 映射为 400），故不带 ArgumentException 的 paramName 后缀
        if (string.IsNullOrEmpty(displayName))
            throw new ArgumentException("技能的 name 不能为空");
        if (displayName.Any(char.IsControl))
            throw new ArgumentException("技能的 name 不能包含换行或控制字符");

        // description 是模型选择技能的依据，缺失即无法被选中：两端 UI 与服务端统一按必填校验
        if (NormalizeFrontmatterText(content.Description).Length == 0)
            throw new ArgumentException("技能的 description 不能为空");

        var root = ResolveDir(workspaceRoot);
        Directory.CreateDirectory(root);

        string dirName;
        if (!string.IsNullOrWhiteSpace(originalId))
        {
            // 已有条目：目录保持不动，只改 frontmatter（显示名不受文件名规则限制）
            dirName = EnsureSafeName(originalId, nameof(originalId));
        }
        else
        {
            // 新建：目录名由名称生成；名称必须同时能作为目录名，同名目录已存在时拒绝
            if (!IsSafeDirName(displayName))
                throw new ArgumentException($"技能的 name 不能作为目录名（含非法字符）: '{displayName}'");
            dirName = displayName;
            if (Directory.Exists(Path.Combine(root, dirName)))
                throw new InvalidOperationException($"已存在同名技能目录 \"{dirName}\"，请更换名称。");
        }

        var targetDir = Path.Combine(root, dirName);
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(
            Path.Combine(targetDir, "SKILL.md"),
            BuildSkillMarkdown(
                displayName,
                NormalizeFrontmatterText(content.Description),
                NormalizeFrontmatterText(content.Category),
                NormalizeFrontmatterText(content.Triggers),
                content.Body ?? ""));

        return dirName;
    }

    /// <summary>
    /// frontmatter 单行值归一：控制字符（含换行 / 制表）与连续空白折叠为单个空格，
    /// 避免把后续行挤出 frontmatter，也避免出现 "a  b" 这类双空格。
    /// </summary>
    private static string NormalizeFrontmatterText(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0) return "";
        if (!text.Any(char.IsControl) && !text.Contains("  ", StringComparison.Ordinal)) return text;

        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            sb.Append(ch);
            lastWasSpace = false;
        }

        return sb.ToString().TrimEnd();
    }

    /// <inheritdoc />
    public SkillImportResult ImportZip(Stream zipStream, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(zipStream);

        var root = ResolveDir(workspaceRoot);
        Directory.CreateDirectory(root);

        var imported = new List<string>();
        var skipped = new List<string>();
        var ignored = new List<string>();

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxImportEntries)
            throw new InvalidOperationException($"ZIP 条目数超过上限 {MaxImportEntries}，请拆分后再导入。");

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;

            var normalized = entry.FullName.Replace('\\', '/').TrimStart('/');
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // 形态约定：<任意前缀>/<目录>/SKILL.md，目录名取 SKILL.md 的上一级
            if (segments.Length < 2 || !segments[^1].Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
            {
                // 伴随文件（脚本、参考文档等）当前不落盘，与非技能文件分开计数，避免混进「跳过」误导用户
                ignored.Add(normalized);
                continue;
            }

            var dirName = segments[^2];
            if (!IsSafeDirName(dirName))
            {
                skipped.Add(dirName);
                continue;
            }

            if (entry.Length > MaxSkillFileBytes)
            {
                skipped.Add($"{dirName}（大于 {MaxSkillFileBytes / 1024}KB）");
                continue;
            }

            totalBytes += entry.Length;
            if (totalBytes > MaxImportTotalBytes)
                throw new InvalidOperationException($"ZIP 解压后总大小超过上限 {MaxImportTotalBytes / 1024 / 1024}MB，请拆分后再导入。");

            var targetDir = Path.Combine(root, dirName);
            Directory.CreateDirectory(targetDir);
            using (var source = entry.Open())
            using (var target = File.Create(Path.Combine(targetDir, "SKILL.md")))
            {
                source.CopyTo(target);
            }

            if (!imported.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                imported.Add(dirName);
        }

        return new SkillImportResult(imported, skipped, ignored);
    }

    // ---------------- 路径与内容解析 ----------------

    private static string ResolveDir(string? workspaceRoot)
        => string.IsNullOrWhiteSpace(workspaceRoot)
            ? GlobalLubanAgentPath.SkillsDir
            : Path.Combine(workspaceRoot, ".luban-agent", "skills");

    /// <summary>合并视图的来源判定：全局文件 + 遗留用户级 + 当前工作区文件。</summary>
    private static HashSet<string> CollectOriginFileIds(string? workspaceRoot)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ScanSkillDir(GlobalLubanAgentPath.SkillsDir, ids);
        ScanSkillDir(LegacyUserSkillsRoot, ids);

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            ScanSkillDir(Path.Combine(workspaceRoot, ".luban-agent", "skills"), ids);

        return ids;
    }

    private static void ScanSkillDir(string dir, HashSet<string> ids)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var skillDir in Directory.GetDirectories(dir))
        {
            if (File.Exists(Path.Combine(skillDir, "SKILL.md")))
                ids.Add(Path.GetFileName(skillDir));
        }
    }

    /// <summary>拒绝空名与路径穿越（Id 即目录名）。</summary>
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

    /// <summary>ZIP 内目录名是否可安全落盘（非空、无分隔符、无上跳）。</summary>
    private static bool IsSafeDirName(string? name)
    {
        var value = name?.Trim() ?? "";
        if (value.Length == 0) return false;
        if (value is "." or ".." || value.Contains("..", StringComparison.Ordinal)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains('/') || value.Contains('\\'))
        {
            return false;
        }
        return true;
    }

    private static string BuildSkillMarkdown(string name, string desc, string category, string triggers, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {name}");
        sb.AppendLine($"description: {desc}");
        if (!string.IsNullOrWhiteSpace(category)) sb.AppendLine($"category: {category}");
        if (!string.IsNullOrWhiteSpace(triggers)) sb.AppendLine($"triggers: {triggers}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(body ?? "");
        return sb.ToString();
    }

    /// <summary>
    /// 解析 SKILL.md：仅识别 SkillMdParser 支持的四个 frontmatter 键
    /// （name / description / category / triggers），其余键忽略。
    /// </summary>
    private static (string name, string desc, string category, string triggers, string body) ParseSkillMarkdown(string md)
    {
        var name = "";
        var desc = "";
        var category = "";
        var triggers = "";

        var lines = md.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            i = 1;
            while (i < lines.Length && lines[i].Trim() != "---")
            {
                var line = lines[i];
                var idx = line.IndexOf(':');
                if (idx > 0)
                {
                    var k = line[..idx].Trim();
                    var v = line[(idx + 1)..].Trim();
                    switch (k)
                    {
                        case "name": name = v; break;
                        case "description": desc = v; break;
                        case "category": category = v; break;
                        case "triggers": triggers = v; break;
                    }
                }
                i++;
            }
            i++; // 跳过结束分隔符
        }

        var body = string.Join("\n", lines.Skip(i));
        return (name, desc, category, triggers, body);
    }
}