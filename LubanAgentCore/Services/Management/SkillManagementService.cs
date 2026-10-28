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

            // 合并优先级：硬编码 > 工作区文件 > config.json
            var origin = _registry.IsBuiltin(skill.Id)
                ? ManagedOrigin.Builtin
                : fileIds.Contains(skill.Id) ? ManagedOrigin.File : ManagedOrigin.Config;

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

        if (_registry.IsBuiltin(safeId))
        {
            _config.SetBuiltinSkillEnabled(safeId, enabled);
        }
        else if (_config.CustomSkills.Any(c => c.Id.Equals(safeId, StringComparison.OrdinalIgnoreCase)))
        {
            _config.SetCustomSkillEnabled(safeId, enabled);
        }
        else if (CollectOriginFileIds(_workspaces.CurrentWorkspace?.RootPath).Contains(safeId))
        {
            throw new NotSupportedException(
                $"文件型 Skill '{safeId}' 不支持在此启用/禁用，请直接编辑 .luban-agent/skills 目录");
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
    public SkillFileContent? ReadFile(string id, string? workspaceRoot)
    {
        var safeId = EnsureSafeName(id, nameof(id));
        var path = Path.Combine(ResolveDir(workspaceRoot), safeId, "SKILL.md");
        if (!File.Exists(path)) return null;

        var (name, description, category, triggers, body) = ParseSkillMarkdown(File.ReadAllText(path));
        return new SkillFileContent(name, description, category, triggers, body);
    }

    /// <inheritdoc />
    public void SaveFile(string? originalId, SkillFileContent content, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(content);

        var name = EnsureSafeName(content.Name?.Trim() ?? "", nameof(content));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("技能的 name 不能为空", nameof(content));

        var root = ResolveDir(workspaceRoot);
        Directory.CreateDirectory(root);

        var targetDir = Path.Combine(root, name);

        // 改名：移动目录（与 Codex 设置页原逻辑一致，大小写视为不同）
        if (!string.IsNullOrEmpty(originalId) && originalId != name)
        {
            var oldDir = Path.Combine(root, EnsureSafeName(originalId, nameof(originalId)));
            if (Directory.Exists(oldDir))
            {
                if (Directory.Exists(targetDir))
                    throw new InvalidOperationException($"已存在同名技能 \"{name}\"，请更换 name。");
                Directory.Move(oldDir, targetDir);
            }
        }

        Directory.CreateDirectory(targetDir);
        File.WriteAllText(
            Path.Combine(targetDir, "SKILL.md"),
            BuildSkillMarkdown(name, content.Description ?? "", content.Category ?? "", content.Triggers ?? "", content.Body ?? ""));
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