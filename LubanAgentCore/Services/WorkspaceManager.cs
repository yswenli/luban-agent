/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
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
using LubanAgentCore.Infrastructure;
using LubanAgentCore.Repositories;

namespace LubanAgentCore.Services;

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
    /// 工作区类型：Normal | LlmWiki
    /// </summary>
    public string Type { get; set; } = WorkspaceTypes.Normal;

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
    /// <param name="name">显示名（可选；空白时普通工作区取目录名，LLM Wiki 取 <see cref="WorkspaceManager.DefaultLlmWikiName"/>）</param>
    /// <param name="type">工作区类型：Normal | LlmWiki</param>
    /// <returns>工作区信息</returns>
    Task<WorkspaceInfo> CreateWorkspaceAsync(string rootPath, string? name = null, string type = WorkspaceTypes.Normal);

    /// <summary>
    /// 切换工作区
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    Task SwitchWorkspaceAsync(string workspaceId);

    /// <summary>
    /// 获取当前用户的全部工作区
    /// </summary>
    Task<IEnumerable<WorkspaceInfo>> GetUserWorkspacesAsync();

    /// <summary>
    /// 按工作区ID获取工作区信息（用于按会话自身绑定的工作区解析，避免依赖进程级“当前工作区”）。
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    /// <returns>工作区信息；不存在或ID为空时返回 null。</returns>
    Task<WorkspaceInfo?> GetWorkspaceAsync(string workspaceId);

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

    /// <summary>
    /// 删除工作区：逻辑删除工作区记录，并级联清理其下所有会话与 LLM Wiki 索引。
    /// </summary>
    /// <remarks>
    /// 统一入口，避免各宿主各自实现导致语义分叉（曾出现宿主 A 走物理删除、宿主 B 走逻辑删除，
    /// 且遗漏清理 llmwiki_file/llmwiki_chunk 而残留孤儿索引的情况）。
    /// </remarks>
    /// <param name="workspaceId">工作区ID</param>
    /// <returns>是否执行了删除；工作区ID 为空时返回 false。</returns>
    Task<bool> DeleteWorkspaceAsync(string workspaceId);

    /// <summary>
    /// 重命名工作区（仅改显示名，不涉及目录移动）。
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    /// <param name="name">新的显示名</param>
    /// <returns>是否执行了重命名；工作区ID 或名称为空白时返回 false。</returns>
    Task<bool> RenameWorkspaceAsync(string workspaceId, string name);

    /// <summary>
    /// 按工作区ID取已加载过的根路径；未知或空ID返回 null（同步，供 wiki 等同步接口使用）。
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    string? GetKnownRootPath(string? workspaceId);

    /// <summary>
    /// 判断路径是否在当前工作区根目录内（供工具确认回调判断是否需要人工确认）。
    /// </summary>
    /// <param name="path">要检查的路径</param>
    bool IsWithinWorkspace(string path);
}

/// <summary>
/// 工作区管理服务，管理工作区生命周期、授权、配置加载
/// </summary>
public class WorkspaceManager : IWorkspaceManager, ISingleton
{
    /// <summary>
    /// LLM Wiki 知识库未显式指定名称时的默认显示名
    /// </summary>
    public const string DefaultLlmWikiName = "llmwiki";

    private readonly WorkspaceRepository _repo;
    private readonly SessionRepository _sessionRepo;
    private readonly ISessionManager _sessionManager;
    private readonly BuiltinSkillCatalog _builtinSkills;
    private readonly BuiltinRuleCatalog _builtinRules;
    private readonly string _userId;

    /// <summary>当前工作区（实例级；由宿主按需切换，不再作为静态全局状态）。</summary>
    /// <remarks>
    /// 进程级「活动工作区」指针：单用户宿主下供 UI/命令标记当前选中工作区，
    /// 不承载跨用户业务数据；可变的 per-user 缓存已由 keyed 服务承载，故保留于单例。
    /// </remarks>
    private WorkspaceInfo? _current;
    private readonly object _currentLock = new();

    /// <summary>
    /// 已知工作区根路径缓存（工作区ID → 根路径），用于同步解析非当前工作区。
    /// 在 <see cref="ToWorkspaceInfo"/>（创建/切换/列举工作区）时写入。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _knownRoots =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按工作区ID取已加载过的根路径；未知或空ID返回 null。
    /// 供 <see cref="Agents.WorkspaceWikiContext.WorkspaceRootFor"/> 这类同步接口使用（避免 sync-over-async）。
    /// </summary>
    public string? GetKnownRootPath(string? workspaceId)
        => string.IsNullOrEmpty(workspaceId) ? null
            : _knownRoots.TryGetValue(workspaceId, out var root) ? root : null;

    /// <summary>
    /// 判断路径是否在当前工作区根目录内。
    /// 供 ToolConfirmationService 回调使用，判断文件操作是否需要确认。
    /// </summary>
    /// <param name="path">要检查的路径。</param>
    /// <returns>若当前存在工作区且路径在其根目录子树内返回 true，否则返回 false。</returns>
    public bool IsWithinWorkspace(string path)
    {
        WorkspaceInfo? ws;
        lock (_currentLock) ws = _current;
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
    public WorkspaceInfo? CurrentWorkspace
    {
        get
        {
            lock (_currentLock) return _current;
        }
    }

    /// <summary>当前工作区变更时触发（参数为新工作区）。</summary>
    public event Action<WorkspaceInfo>? CurrentWorkspaceChanged;

    /// <summary>
    /// 创建 WorkspaceManager 实例（绑定用户）
    /// </summary>
    /// <param name="userId">归属用户标识。</param>
    public WorkspaceManager(
        string userId,
        WorkspaceRepository repo,
        SessionRepository sessionRepo,
        ISessionManager sessionManager,
        BuiltinSkillCatalog builtinSkills,
        BuiltinRuleCatalog builtinRules)
    {
        _userId = string.IsNullOrWhiteSpace(userId) ? LubanAgentCore.Hosting.LocalUserDefaults.DefaultUserId : userId;
        _repo = repo;
        _sessionRepo = sessionRepo;
        _sessionManager = sessionManager;
        _builtinSkills = builtinSkills;
        _builtinRules = builtinRules;
    }

    /// <summary>
    /// 创建工作区（内部含路径唯一性校验，避免 TOCTOU 竞态）
    /// </summary>
    public async Task<WorkspaceInfo> CreateWorkspaceAsync(string rootPath, string? name = null, string type = WorkspaceTypes.Normal)
    {
        // 归一化后再入库：去除末尾分隔符、解析符号链接、统一大小写，
        // 避免同一目录因写法差异（尾斜杠/大小写）被登记为多个工作区
        var fullPath = WorkspaceIdGenerator.Normalize(rootPath);

        // 内部唯一性校验（防止绕过 WorkCommand 的调用方造成重复）
        var existing = await _repo.GetByRootPathAsync(fullPath);
        if (existing != null)
        {
            throw new InvalidOperationException($"工作区已存在: {existing.Name} ({existing.RootPath})");
        }

        var ws = new DbWorkspace
        {
            // 由路径派生而非随机生成：同一目录在 CLI/GUI 等不同宿主数据库中恒得同一ID，
            // 保证共享的长期记忆库能跨宿主命中
            WorkspaceId = WorkspaceIdGenerator.Compute(fullPath),
            // 显示名取原始目录名（保留大小写），避免深链归一化后界面/系统提示词里全是全大写路径；
            // 知识库不指定名称时统一用默认名（多个知识库可同名，按 RootPath 唯一）
            Name = string.IsNullOrWhiteSpace(name)
                ? (type == WorkspaceTypes.LlmWiki ? DefaultLlmWikiName : WorkspaceIdGenerator.SuggestName(rootPath))
                : name,
            RootPath = fullPath,
            Type = type,
            UserId = _userId,
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
        // 1. 加载新工作区（工作区根不再注入进程级 PathGuard/工作目录，
        //    改由每会话的 ISessionContext 承载，避免全局静态状态被并发会话串改）
        var ws = await _repo.GetByWorkspaceIdAsync(workspaceId);
        if (ws == null) throw new InvalidOperationException($"工作区不存在: {workspaceId}");

        var newCurrent = ToWorkspaceInfo(ws);
        lock (_currentLock) _current = newCurrent;
        CurrentWorkspaceChanged?.Invoke(newCurrent);

        // 3. 恢复最近活跃会话；无会话时自动创建“新会话”。
        //    失败兜底：宁可无会话（可见、可恢复），也不能保留上一工作区会话（静默串写数据）。
        try
        {
            var latest = await _sessionRepo.GetLatestSessionAsync(workspaceId);
            if (latest != null)
                await _sessionManager.SetCurrentSessionAsync(latest.SessionId);
            else
                await _sessionManager.CreateSessionAsync(title: "新会话", workspaceId: workspaceId);
        }
        catch (Exception ex)
        {
            Logger.Error("恢复/创建工作区会话失败", ex, workspaceId);
            _sessionManager.ClearCurrentSession();
        }

        // 6. 确保配置目录存在
        await EnsureConfigDirectoryAsync(newCurrent);

        // 7. 更新 LastActiveAt
        await _repo.UpdateLastActiveAtAsync(workspaceId);
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
    /// 获取当前用户的全部工作区
    /// </summary>
    public async Task<IEnumerable<WorkspaceInfo>> GetUserWorkspacesAsync()
    {
        var list = await _repo.GetUserWorkspacesAsync(_userId);
        return list.Select(ToWorkspaceInfo);
    }

    /// <summary>
    /// 按工作区ID获取工作区信息（用于按会话自身绑定的工作区解析，避免依赖进程级“当前工作区”）。
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    /// <returns>工作区信息；不存在或ID为空时返回 null。</returns>
    public async Task<WorkspaceInfo?> GetWorkspaceAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId)) return null;
        var ws = await _repo.GetByWorkspaceIdAsync(workspaceId);
        return ws is null ? null : ToWorkspaceInfo(ws);
    }

    /// <summary>
    /// 删除工作区：逻辑删除工作区记录，并清理其下所有会话与 LLM Wiki 索引。
    /// </summary>
    /// <remarks>
    /// 统一入口，避免各宿主各自实现导致语义分叉（曾出现 Sidebar 走物理删除、管理窗口走逻辑删除，
    /// 且 Sidebar 遗漏清理 llmwiki_file/llmwiki_chunk 而残留孤儿索引的情况）。
    /// 清理顺序固定为：会话软删 → LLM Wiki 文件/切块物理删 → 工作区逻辑删。
    /// </remarks>
    /// <param name="workspaceId">工作区ID</param>
    /// <returns>是否执行了删除；工作区ID 为空时返回 false。</returns>
    public async Task<bool> DeleteWorkspaceAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId)) return false;

        var wikiFileRepo = new LlmWikiFileRepository();
        var wikiChunkRepo = new LlmWikiChunkRepository();

        await _sessionRepo.SoftDeleteByWorkspaceAsync(workspaceId);
        await wikiFileRepo.DeleteByWorkspaceAsync(workspaceId);
        await wikiChunkRepo.DeleteByWorkspaceAsync(workspaceId);
        await _repo.LogicDeleteAsync(w => w.WorkspaceId == workspaceId);

        // 若删除的正是当前工作区：移除其注入的 PathGuard 根目录，并清空当前引用与会话，
        // 避免后续操作仍然指向一个已被逻辑删除的工作区。
        WorkspaceInfo? target;
        lock (_currentLock) target = _current;

        if (target != null && target.WorkspaceId == workspaceId)
        {
            lock (_currentLock) _current = null;
            _sessionManager.ClearCurrentSession();
        }

        return true;
    }

    /// <summary>
    /// 重命名工作区：仅更新显示名，不移动工作区目录。
    /// </summary>
    /// <remarks>
    /// 若重命名的正是当前工作区，同步更新内存中的 <see cref="CurrentWorkspace"/> 实例，
    /// 避免 UI 仍显示旧名称（宿主侧通常直接绑定该实例）。
    /// </remarks>
    /// <param name="workspaceId">工作区ID</param>
    /// <param name="name">新的显示名</param>
    /// <returns>是否执行了重命名；工作区ID 或名称为空白时返回 false。</returns>
    public async Task<bool> RenameWorkspaceAsync(string workspaceId, string name)
    {
        if (string.IsNullOrWhiteSpace(workspaceId)) return false;
        if (string.IsNullOrWhiteSpace(name)) return false;

        await _repo.UpdateNameAsync(workspaceId, name.Trim());

        WorkspaceInfo? target;
        lock (_currentLock) target = _current;

        if (target != null && target.WorkspaceId == workspaceId)
            target.Name = name.Trim();

        return true;
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

        if (type == WorkspaceTypes.LlmWiki)
        {
            var wikiConfigPath = Path.Combine(configDir, "llmwiki-config.json");
            if (!File.Exists(wikiConfigPath))
            {
                // 模板仅包含 LlmWikiAgentProfile.LoadLlmWikiConfig 实际消费的键：
                // agentProfile.{systemPrompt,toolGroups}、wiki.{enabled,schemaPath,topK,includeRaw}、
                // retrieval.{defaultTopK,maxResultChars}。systemPrompt 与 LlmWikiAgentProfile 的默认值保持一致。
                var defaultLlmWikiConfig = @"{
  ""agentProfile"": {
    ""systemPrompt"": ""你是一个 LLM Wiki 知识库问答专家，维护并基于 wiki 知识库回答问题。优先使用 wiki 工具检索与沉淀知识；wiki 无相关内容时再回落原始资料。"",
    ""toolGroups"": [""wiki"", ""retrieval"", ""filesystem"", ""context""],
    ""mcpServers"": []
  },
  ""wiki"": {
    ""enabled"": true,
    ""schemaPath"": ""wiki/SCHEMA.md"",
    ""topK"": 8,
    ""includeRaw"": true
  },
  ""retrieval"": {
    ""defaultTopK"": 5,
    ""maxResultChars"": 8000
  }
}";
                File.WriteAllText(wikiConfigPath, defaultLlmWikiConfig);
            }
        }
    }

    /// <summary>
    /// 转换为 WorkspaceInfo
    /// </summary>
    private WorkspaceInfo ToWorkspaceInfo(DbWorkspace ws)
    {
        if (!string.IsNullOrEmpty(ws.WorkspaceId) && !string.IsNullOrEmpty(ws.RootPath))
            _knownRoots[ws.WorkspaceId] = ws.RootPath;

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
        foreach (var skill in _builtinSkills.All)
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
        foreach (var rule in _builtinRules.All)
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

                var json = JsonSerializer.Serialize(config, ConfigJsonOptions.Pretty.GetTypeInfo(typeof(CustomRuleConfig)));
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
