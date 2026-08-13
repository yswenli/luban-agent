/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： WorkspaceManager
*版本号： V1.0.0.0
*唯一标识：工作区管理服务
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：工作区管理服务，管理工作区生命周期、授权、配置加载
*
*****************************************************************************/
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Rules;
using LuBan.AIAgent.Skills;
using LuBan.Common.IO;
using LuBan.DI;

namespace LubanAgentCli.Services;

/// <summary>
/// 工作区信息
/// </summary>
public class WorkspaceInfo
{
    /// <summary>
    /// 工作区ID
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// 显示名
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 根目录绝对路径
    /// </summary>
    public string RootPath { get; set; } = "";

    /// <summary>
    /// 工作区类型：Normal | Rag
    /// </summary>
    public string Type { get; set; } = "Normal";

    /// <summary>
    /// 归属用户
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 配置目录相对路径（.luban-agent）
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime? LastActiveAt { get; set; }

    /// <summary>
    /// 是否已授权访问根目录
    /// </summary>
    public bool IsAuthorized { get; set; }
}

/// <summary>
/// 工作区管理接口
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    /// 当前工作区
    /// </summary>
    WorkspaceInfo? CurrentWorkspace { get; }

    /// <summary>
    /// 创建工作区
    /// </summary>
    /// <param name="rootPath">根目录路径</param>
    /// <param name="name">显示名（可选）</param>
    /// <param name="type">工作区类型：Normal | Rag</param>
    /// <returns>工作区信息</returns>
    Task<WorkspaceInfo> CreateWorkspaceAsync(string rootPath, string? name = null, string type = "Normal");

    /// <summary>
    /// 切换工作区
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    Task SwitchWorkspaceAsync(string workspaceId);

    /// <summary>
    /// 获取用户的所有工作区
    /// </summary>
    /// <param name="userId">用户ID，null 表示获取全部</param>
    Task<IEnumerable<WorkspaceInfo>> GetUserWorkspacesAsync(string? userId = null);

    /// <summary>
    /// 确保工作区已授权
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    /// <returns>是否授权成功</returns>
    Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace);

    /// <summary>
    /// 确保工作区配置目录存在（不加载配置到内存，配置由 AgentProfile 按需加载）
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    Task EnsureConfigDirectoryAsync(WorkspaceInfo workspace);

    /// <summary>
    /// 设置当前工作区并恢复最近会话（用于启动时）
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    Task SetCurrentAsync(string workspaceId);
}

/// <summary>
/// 工作区管理服务，管理工作区生命周期、授权、配置加载
/// </summary>
public class WorkspaceManager : IWorkspaceManager, ISingleton
{
    private readonly WorkspaceRepository _repo;
    private readonly SessionRepository _sessionRepo;
    private readonly ISessionManager _sessionManager;
    private readonly IOptions<LuBanAgentOptions> _options;
    private readonly IEnumerable<ISkill> _builtinSkills;
    private readonly IEnumerable<IRule> _builtinRules;

    /// <summary>
    /// 工作区注入的 PathGuard roots（仅记录工作区注入部分，避免误删全局 roots）
    /// </summary>
    private readonly HashSet<string> _injectedRoots = new(StringComparer.OrdinalIgnoreCase);

    private static WorkspaceInfo? _current;
    private static readonly object _currentLock = new();

    /// <summary>
    /// 静态构造：将临时文件统一重定向到当前工作区的 .luban-agent/temp 目录。
    /// 工作区不存在时回退到系统临时目录。
    /// </summary>
    static WorkspaceManager()
    {
        TempDirectory.Resolver = () =>
        {
            var ws = Current;
            if (!string.IsNullOrEmpty(ws?.RootPath) && !string.IsNullOrEmpty(ws?.ConfigPath))
            {
                return Path.Combine(ws.RootPath, ws.ConfigPath, "temp");
            }
            return null;
        };
    }

    /// <summary>
    /// 当前工作区（静态访问，供非 DI 组件如 SqliteVectorStore、SessionManager 使用）
    /// </summary>
    public static WorkspaceInfo? Current
    {
        get
        {
            lock (_currentLock) return _current;
        }
    }

    /// <summary>
    /// 判断路径是否在当前工作区根目录内。
    /// 供 ToolConfirmationService 回调使用，判断文件操作是否需要确认。
    /// </summary>
    /// <param name="path">要检查的路径。</param>
    /// <returns>若当前存在工作区且路径在其根目录子树内返回 true，否则返回 false。</returns>
    public static bool IsWithinWorkspace(string path)
    {
        var ws = Current;
        if (ws == null || string.IsNullOrEmpty(ws.RootPath) || string.IsNullOrEmpty(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var rootPath = Path.GetFullPath(ws.RootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 解析符号链接/junction 到真实目标，防止工作区内的链接逃逸到根目录外
            fullPath = ResolveLinkTarget(fullPath) ?? fullPath;
            rootPath = ResolveLinkTarget(rootPath) ?? rootPath;

            return fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 若路径存在且为符号链接/junction，返回其最终真实目标路径；否则返回 null。
    /// </summary>
    private static string? ResolveLinkTarget(string fullPath)
    {
        try
        {
            FileSystemInfo? info = File.Exists(fullPath)
                ? new FileInfo(fullPath)
                : Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : null;
            if (info?.LinkTarget == null) return null;
            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            return resolved?.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 授权确认委托。由 UI 层设置（启动向导或命令执行时）。
    /// 如果未设置，默认拒绝授权。
    /// 在启动期间设置一次，之后在任何工作区操作之前完成。
    /// </summary>
    public Func<WorkspaceInfo, Task<bool>>? AuthorizationPrompt { get; set; }

    /// <summary>
    /// 当前工作区
    /// </summary>
    public WorkspaceInfo? CurrentWorkspace => Current;

    /// <summary>
    /// 创建 WorkspaceManager 实例
    /// </summary>
    public WorkspaceManager(
        WorkspaceRepository repo,
        SessionRepository sessionRepo,
        ISessionManager sessionManager,
        IOptions<LuBanAgentOptions> options,
        IEnumerable<ISkill> builtinSkills,
        IEnumerable<IRule> builtinRules)
    {
        _repo = repo;
        _sessionRepo = sessionRepo;
        _sessionManager = sessionManager;
        _options = options;
        _builtinSkills = builtinSkills;
        _builtinRules = builtinRules;
    }

    /// <summary>
    /// 创建工作区（内部含路径唯一性校验，避免 TOCTOU 竞态）
    /// </summary>
    public async Task<WorkspaceInfo> CreateWorkspaceAsync(string rootPath, string? name = null, string type = "Normal")
    {
        var fullPath = Path.GetFullPath(rootPath);

        // 内部唯一性校验（防止绕过 WorkCommand 的调用方造成重复）
        var existing = await _repo.GetByRootPathAsync(fullPath);
        if (existing != null)
        {
            throw new InvalidOperationException($"工作区已存在: {existing.Name} ({existing.RootPath})");
        }

        var ws = new DbWorkspace
        {
            WorkspaceId = Guid.NewGuid().ToString("N"),
            Name = name ?? Path.GetFileName(fullPath),
            RootPath = fullPath,
            Type = type,
            IsAuthorized = false,
            ConfigPath = ".luban-agent",
            CreateTime = DateTime.Now,
            IsDelete = false
        };
        await _repo.InsertAsync(ws);
        EnsureConfigDirectory(ws.RootPath, type);
        return ToWorkspaceInfo(ws);
    }

    /// <summary>
    /// 切换工作区
    /// </summary>
    public async Task SwitchWorkspaceAsync(string workspaceId)
    {
        // 1. 移除上一工作区的 RootPath（仅工作区注入部分，保留全局配置）
        WorkspaceInfo? previous;
        lock (_currentLock) previous = _current;

        if (previous != null && previous.IsAuthorized)
            RemoveWorkspaceRootFromPathGuard(previous.RootPath);

        // 2. 加载新工作区
        var ws = await _repo.GetByWorkspaceIdAsync(workspaceId);
        if (ws == null) throw new InvalidOperationException($"工作区不存在: {workspaceId}");

        var newCurrent = ToWorkspaceInfo(ws);
        lock (_currentLock) _current = newCurrent;

        // 3. 注入新工作区的 RootPath（如果已授权）
        if (newCurrent.IsAuthorized)
            AddWorkspaceRootToPathGuard(newCurrent.RootPath);

        // 4. 设置进程当前工作目录为工作区根目录
        //    使 PathGuard 的相对路径解析、脚本工具的默认 workingDirectory 都指向工作区
        SetCurrentDirectory(newCurrent.RootPath);

        // 5. 恢复最近活跃会话
        var latest = await _sessionRepo.GetLatestSessionAsync(workspaceId);
        if (latest != null)
            await _sessionManager.SetCurrentSessionAsync(latest.SessionId);
        else
            _sessionManager.ClearCurrentSession();

        // 6. 确保配置目录存在
        await EnsureConfigDirectoryAsync(newCurrent);

        // 7. 更新 LastActiveAt
        await _repo.UpdateLastActiveAtAsync(workspaceId);
    }

    /// <summary>
    /// 设置进程当前工作目录（使相对路径和脚本默认工作目录指向工作区根目录）
    /// </summary>
    private static void SetCurrentDirectory(string rootPath)
    {
        try
        {
            if (Directory.Exists(rootPath))
                Directory.SetCurrentDirectory(rootPath);
        }
        catch
        {
            // 工作目录设置失败不阻断工作区切换，仅影响相对路径解析
        }
    }

    /// <summary>
    /// 设置当前工作区并恢复最近会话（用于启动时）
    /// </summary>
    public async Task SetCurrentAsync(string workspaceId)
    {
        // 复用 SwitchWorkspaceAsync 的完整流程，确保会话恢复与配置检查
        await SwitchWorkspaceAsync(workspaceId);
    }

    /// <summary>
    /// 确保工作区已授权
    /// </summary>
    public async Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace)
    {
        bool confirmed;
        if (AuthorizationPrompt is not null)
        {
            confirmed = await AuthorizationPrompt(workspace);
        }
        else
        {
            Logger.Warn("AuthorizationPrompt delegate not set, defaulting to denied");
            confirmed = false;
        }

        if (!confirmed)
        {
            return false;
        }

        await _repo.UpdateAuthorizationAsync(workspace.WorkspaceId, true);
        workspace.IsAuthorized = true;

        lock (_currentLock)
        {
            if (_current != null && _current.WorkspaceId == workspace.WorkspaceId)
            {
                _current.IsAuthorized = true;
            }
        }

        AddWorkspaceRootToPathGuard(workspace.RootPath);
        SetCurrentDirectory(workspace.RootPath);
        return true;
    }

    /// <summary>
    /// 确保工作区配置目录存在（不加载配置到内存，配置由 AgentProfile 按需加载）
    /// </summary>
    public Task EnsureConfigDirectoryAsync(WorkspaceInfo workspace)
    {
        if (workspace.ConfigPath != null)
        {
            // 无论目录是否存在，都调用 EnsureConfigDirectory 确保内置内容已初始化
            try
            {
                EnsureConfigDirectory(workspace.RootPath, workspace.Type);
            }
            catch (Exception ex)
            {
                Logger.Error($"无法在工作区目录创建配置文件夹", ex);
            }

            // 清理超过 24 小时的工作区临时文件
            try { TempDirectory.Cleanup(TimeSpan.FromDays(1)); }
            catch { }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取用户的所有工作区（userId 为 null 时获取全部）
    /// </summary>
    public async Task<IEnumerable<WorkspaceInfo>> GetUserWorkspacesAsync(string? userId = null)
    {
        List<DbWorkspace> list;
        if (string.IsNullOrEmpty(userId))
            list = await _repo.GetAllAsync();
        else
            list = await _repo.GetUserWorkspacesAsync(userId);
        return list.Select(ToWorkspaceInfo);
    }

    /// <summary>
    /// 初始化配置目录
    /// </summary>
    private void EnsureConfigDirectory(string rootPath, string type)
    {
        var configDir = Path.Combine(rootPath, ".luban-agent");
        Directory.CreateDirectory(configDir);
        
        var skillsDir = Path.Combine(configDir, "skills");
        var rulesDir = Path.Combine(configDir, "rules");
        var mcpsDir = Path.Combine(configDir, "mcps");
        var tempDir = Path.Combine(configDir, "temp");
        
        Directory.CreateDirectory(skillsDir);
        Directory.CreateDirectory(rulesDir);
        Directory.CreateDirectory(mcpsDir);
        Directory.CreateDirectory(tempDir);

        // 如果 skills 目录为空，写入内置 skills
        if (!Directory.EnumerateFileSystemEntries(skillsDir).Any())
        {
            WriteBuiltinSkills(skillsDir);
        }

        // 如果 rules 目录为空，写入内置 rules
        if (!Directory.EnumerateFileSystemEntries(rulesDir).Any())
        {
            WriteBuiltinRules(rulesDir);
        }

        var configPath = Path.Combine(configDir, "config.json");
        if (!File.Exists(configPath))
            File.WriteAllText(configPath, "{}");

        if (type == "Rag")
        {
            var ragConfigPath = Path.Combine(configDir, "rag-config.json");
            if (!File.Exists(ragConfigPath))
            {
                var defaultRagConfig = @"{
  ""agentProfile"": {
    ""systemPrompt"": ""你是一个知识库问答专家。请基于检索到的文档片段回答问题，不要超出文档范围。如果文档中没有相关信息，请明确告知用户。"",
    ""toolGroups"": [""retrieval"", ""filesystem""],
    ""mcpServers"": []
  },
  ""retrieval"": {
    ""defaultTopK"": 5,
    ""maxResultChars"": 8000,
    ""supportedExtensions"": ["".txt"", "".md""],
    ""chunkSize"": 500,
    ""chunkOverlap"": 100
  }
}";
                File.WriteAllText(ragConfigPath, defaultRagConfig);
            }
        }
    }

    /// <summary>
    /// 将工作区根目录加入 PathGuard 允许列表，并记录到 _injectedRoots
    /// </summary>
    private void AddWorkspaceRootToPathGuard(string rootPath)
    {
        var normalized = NormalizeRoot(rootPath);

        var roots = _options.Value.Tools.FileSystem.AllowedRoots ?? new List<string>();
        if (!roots.Any(r => string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            roots = new List<string>(roots) { normalized };
            _options.Value.Tools.FileSystem.AllowedRoots = roots;
        }
        _injectedRoots.Add(normalized);
    }

    /// <summary>
    /// 从 PathGuard 允许列表移除工作区根目录（仅移除工作区注入部分，保留全局配置的 roots）
    /// </summary>
    private void RemoveWorkspaceRootFromPathGuard(string rootPath)
    {
        var normalized = NormalizeRoot(rootPath);

        if (!_injectedRoots.Contains(normalized))
        {
            // 非工作区注入的 root（来自全局配置），不移除
            return;
        }
        _injectedRoots.Remove(normalized);

        var roots = _options.Value.Tools.FileSystem.AllowedRoots ?? new List<string>();
        roots = roots.Where(r => !string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        _options.Value.Tools.FileSystem.AllowedRoots = roots;
    }

    /// <summary>
    /// 规范化根目录路径（末尾加分隔符）
    /// </summary>
    private static string NormalizeRoot(string rootPath)
    {
        var normalized = Path.GetFullPath(rootPath);
        if (!normalized.EndsWith(Path.DirectorySeparatorChar))
            normalized += Path.DirectorySeparatorChar;
        return normalized;
    }

    /// <summary>
    /// 转换为 WorkspaceInfo
    /// </summary>
    private static WorkspaceInfo ToWorkspaceInfo(DbWorkspace ws)
    {
        return new WorkspaceInfo
        {
            WorkspaceId = ws.WorkspaceId,
            Name = ws.Name,
            RootPath = ws.RootPath,
            Type = ws.Type,
            UserId = ws.UserId,
            ConfigPath = ws.ConfigPath,
            LastActiveAt = ws.LastActiveAt,
            IsAuthorized = ws.IsAuthorized
        };
    }

    /// <summary>
    /// 将内置 Skills 写入目录（SKILL.md 格式）
    /// </summary>
    private void WriteBuiltinSkills(string skillsDir)
    {
        foreach (var skill in _builtinSkills)
        {
            try
            {
                var skillDir = Path.Combine(skillsDir, skill.Id);
                Directory.CreateDirectory(skillDir);
                
                var skillFile = Path.Combine(skillDir, "SKILL.md");
                var content = FormatSkillMd(skill);
                File.WriteAllText(skillFile, content);
            }
            catch (Exception ex)
            {
                Logger.Error($"写入内置 Skill 失败: {skill.Id}", ex);
            }
        }
    }

    /// <summary>
    /// 将内置 Rules 写入目录（JSON 格式）。
    /// 内置规则含硬编码逻辑无法完整序列化，写出的 JSON 仅作参考文档（Enabled=false 不生效），
    /// 避免用户禁用内置规则后工作区副本以通配 allow 语义顶替进 merged。
    /// </summary>
    private void WriteBuiltinRules(string rulesDir)
    {
        foreach (var rule in _builtinRules)
        {
            try
            {
                var ruleFile = Path.Combine(rulesDir, $"{rule.Id}.json");
                var config = new CustomRuleConfig
                {
                    Id = rule.Id,
                    Name = rule.Name,
                    Description = rule.Description,
                    Priority = rule.Priority,
                    Enabled = false,
                    ActionTypePattern = "*",
                    TargetPattern = "*",
                    Action = "allow"
                };

                var json = config.ToJson(hasIndentation: true);
                File.WriteAllText(ruleFile, json);
            }
            catch (Exception ex)
            {
                Logger.Error($"写入内置 Rule 失败: {rule.Id}", ex);
            }
        }
    }

    /// <summary>
    /// 格式化 Skill 为 SKILL.md 格式
    /// </summary>
    private static string FormatSkillMd(ISkill skill)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {skill.Name}");
        sb.AppendLine($"description: {skill.Description}");
        sb.AppendLine($"category: {skill.Category}");
        
        if (skill.TriggerKeywords.Any())
        {
            sb.AppendLine($"triggers: {string.Join(", ", skill.TriggerKeywords)}");
        }
        
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(skill.PromptTemplate);
        
        return sb.ToString();
    }
}
