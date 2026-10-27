namespace LubanAgentCore.Services.Management;

/// <summary>
/// 规则管理实现（内置 + 文件型 + config 自定义）。
/// 业务规则全部委托 <see cref="ConfigManager"/>（持久化）与 <see cref="RuleEngine"/>（三级合并视图），
/// 文件后端直接操作 <c>rules/&lt;id&gt;.json</c>（与 Codex 设置页原逻辑一致）。
/// </summary>
/// <remarks>
/// 框架 <see cref="RuleEngine"/> 未公开「是否内置」，故来源按规则实例类型 + 文件/config 存在性反推：
/// 非 <see cref="CustomRule"/> 即硬编码内置；是 <see cref="CustomRule"/> 再看文件与 config 谁命中。
/// </remarks>
public sealed class RuleManagementService : IRuleManagementService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ConfigManager _config;
    private readonly RuleEngine _engine;
    private readonly IWorkspaceManager _workspaces;

    /// <summary>
    /// 创建规则管理服务
    /// </summary>
    public RuleManagementService(ConfigManager config, RuleEngine engine, IWorkspaceManager workspaces)
    {
        _config = config;
        _engine = engine;
        _workspaces = workspaces;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RuleInfo>> ListAsync(CancellationToken ct = default)
    {
        var workspaceRoot = _workspaces.CurrentWorkspace?.RootPath;
        var files = ScanRuleFiles(workspaceRoot);
        var configRules = _config.CustomRules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var items = new List<RuleInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in _engine.GetAllRules())
        {
            seen.Add(rule.Id);

            // 合并优先级：硬编码 > 文件型 > config.json
            var origin = rule is not CustomRule
                ? ManagedOrigin.Builtin
                : files.ContainsKey(rule.Id) ? ManagedOrigin.File : ManagedOrigin.Config;

            // 进入合并视图的非内置项本身即「启用」；config 项另读 Enabled（禁用项也进合并视图）
            var enabled = origin != ManagedOrigin.Config
                          || (configRules.TryGetValue(rule.Id, out var c) && c.Enabled);

            var source = origin == ManagedOrigin.File
                ? files.GetValueOrDefault(rule.Id)
                : origin == ManagedOrigin.Config ? configRules.GetValueOrDefault(rule.Id) : null;

            items.Add(new RuleInfo(
                rule.Id,
                rule.Name,
                rule.Description,
                enabled,
                origin,
                rule.Priority,
                source?.ActionTypePattern ?? "*",
                source?.TargetPattern ?? "*",
                source?.Action ?? "deny"));
        }

        // 被禁用的内置规则不在合并结果中，单独补出（CLI「已禁用的内置规则」分组）
        foreach (var id in _config.DisabledBuiltinRules)
        {
            if (seen.Contains(id)) continue;
            items.Add(new RuleInfo(id, id, "", false, ManagedOrigin.Builtin));
        }

        // 禁用的文件型规则（RuleEngine 加载时直接跳过）单独补出
        foreach (var (id, cfg) in files)
        {
            if (seen.Contains(id)) continue;
            items.Add(BuildFileItem(cfg, id, enabled: false));
        }

        // 禁用的 config 规则
        foreach (var cfg in _config.CustomRules)
        {
            if (cfg.Enabled || seen.Contains(cfg.Id)) continue;
            items.Add(BuildFileItem(cfg, cfg.Id, enabled: false) with { Origin = ManagedOrigin.Config });
        }

        return Task.FromResult<IReadOnlyList<RuleInfo>>(items);

        static RuleInfo BuildFileItem(CustomRuleConfig cfg, string id, bool enabled)
            => new(id,
                string.IsNullOrWhiteSpace(cfg.Name) ? id : cfg.Name,
                cfg.Description ?? "",
                enabled,
                ManagedOrigin.File,
                cfg.Priority,
                string.IsNullOrWhiteSpace(cfg.ActionTypePattern) ? "*" : cfg.ActionTypePattern,
                string.IsNullOrWhiteSpace(cfg.TargetPattern) ? "*" : cfg.TargetPattern,
                string.IsNullOrWhiteSpace(cfg.Action) ? "deny" : cfg.Action);
    }

    /// <inheritdoc />
    public Task AddAsync(CustomRuleConfig rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var id = rule.Id?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("规则 ID 不能为空", nameof(rule));

        if (string.IsNullOrWhiteSpace(rule.Name))
            throw new ArgumentException("规则名称不能为空", nameof(rule));

        if (_engine.GetRule(id) != null)
            throw new InvalidOperationException($"ID '{id}' 已存在");

        if (_config.CustomRules.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"ID '{id}' 已存在于自定义规则中");

        var action = NormalizeAction(rule.Action, nameof(rule));
        rule.Id = id;
        rule.Action = action;
        rule.ActionTypePattern = string.IsNullOrWhiteSpace(rule.ActionTypePattern) ? "*" : rule.ActionTypePattern;
        rule.TargetPattern = string.IsNullOrWhiteSpace(rule.TargetPattern) ? "*" : rule.TargetPattern;

        _config.AddCustomRule(rule);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(CustomRuleConfig rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Action = NormalizeAction(rule.Action, nameof(rule));
        _config.UpdateCustomRule(rule);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default)
    {
        var safeId = EnsureSafeName(id, nameof(id));

        switch (origin)
        {
            case ManagedOrigin.Builtin:
                throw new InvalidOperationException($"内置规则 '{safeId}' 不可删除");

            case ManagedOrigin.Config:
                _config.RemoveCustomRule(safeId);
                break;

            case ManagedOrigin.File:
                DeleteFiles(safeId, workspaceRoot);
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

        if (_config.CustomRules.Any(r => r.Id.Equals(safeId, StringComparison.OrdinalIgnoreCase)))
        {
            _config.SetCustomRuleEnabled(safeId, enabled);
        }
        else if (ScanRuleFiles(_workspaces.CurrentWorkspace?.RootPath).ContainsKey(safeId))
        {
            throw new NotSupportedException(
                $"文件型规则 '{safeId}' 不支持在此启用/禁用，请在该规则文件里修改 Enabled 字段");
        }
        else if (_engine.Contains(safeId)
                 || _config.DisabledBuiltinRules.Contains(safeId, StringComparer.OrdinalIgnoreCase))
        {
            _config.SetBuiltinRuleEnabled(safeId, enabled);
        }
        else
        {
            throw new InvalidOperationException($"规则 '{safeId}' 不存在");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFileIds(string? workspaceRoot)
    {
        var dir = ResolveDir(workspaceRoot);
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        return Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public CustomRuleConfig? ReadFile(string id, string? workspaceRoot)
    {
        var safeId = EnsureSafeName(id, nameof(id));
        var path = Path.Combine(ResolveDir(workspaceRoot), safeId + ".json");
        if (!File.Exists(path)) return null;

        var cfg = Deserialize(File.ReadAllText(path));
        if (cfg == null) return null;
        if (string.IsNullOrWhiteSpace(cfg.Id)) cfg.Id = safeId;
        return cfg;
    }

    /// <inheritdoc />
    public void SaveFile(string? originalId, CustomRuleConfig rule, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var id = EnsureSafeName(rule.Id, nameof(rule));
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("规则的 Id 不能为空", nameof(rule));

        rule.Id = id;
        rule.Action = NormalizeAction(rule.Action, nameof(rule));

        var dir = ResolveDir(workspaceRoot);
        Directory.CreateDirectory(dir);

        // 改名：删除旧文件
        if (!string.IsNullOrEmpty(originalId) && !string.Equals(originalId, id, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(dir, EnsureSafeName(originalId, nameof(originalId)) + ".json");
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }

        File.WriteAllText(Path.Combine(dir, id + ".json"), JsonSerializer.Serialize(rule, JsonOpts));
    }

    // ---------------- 路径与序列化 ----------------

    private static string ResolveDir(string? workspaceRoot)
        => string.IsNullOrWhiteSpace(workspaceRoot)
            ? GlobalLubanAgentPath.RulesDir
            : Path.Combine(workspaceRoot, ".luban-agent", "rules");

    /// <summary>
    /// 扫描全局 + 当前工作区的规则文件，按规则 Id 去重（工作区覆盖全局）。
    /// 同时收录「json 内容 Id」与「文件名」两种键，兼容两者不一致的历史文件。
    /// </summary>
    private static Dictionary<string, CustomRuleConfig> ScanRuleFiles(string? workspaceRoot)
    {
        var result = new Dictionary<string, CustomRuleConfig>(StringComparer.OrdinalIgnoreCase);
        ScanRuleDir(GlobalLubanAgentPath.RulesDir, result);

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            ScanRuleDir(Path.Combine(workspaceRoot, ".luban-agent", "rules"), result);

        return result;
    }

    private static void ScanRuleDir(string dir, Dictionary<string, CustomRuleConfig> result)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var cfg = Deserialize(File.ReadAllText(file));
                if (cfg == null) continue;

                var fileKey = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(cfg.Id)) result[cfg.Id] = cfg;
                if (!string.IsNullOrWhiteSpace(fileKey)) result[fileKey] = cfg;
            }
            catch (Exception ex)
            {
                Logger.Error($"扫描规则文件失败: {file}", ex);
            }
        }
    }

    private static void DeleteFiles(string id, string? workspaceRoot)
    {
        var dir = ResolveDir(workspaceRoot);
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var fileKey = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(fileKey, id, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                continue;
            }

            // json 内容 Id 与文件名不一致的历史文件
            try
            {
                if (string.Equals(Deserialize(File.ReadAllText(file))?.Id, id, StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                Logger.Error($"删除规则文件失败: {file}", ex);
            }
        }
    }

    private static CustomRuleConfig? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CustomRuleConfig>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeAction(string? action, string paramName)
    {
        var value = action?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(value)) return "deny";
        if (value != "allow" && value != "deny")
            throw new ArgumentException("Action 只能是 allow 或 deny", paramName);
        return value;
    }

    /// <summary>拒绝空名与路径穿越（Id 即文件名）。</summary>
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
}