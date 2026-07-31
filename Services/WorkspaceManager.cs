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
using LuBan.DI;

namespace LubanAgent.Services;

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
    /// 获取所有工作区
    /// </summary>
    Task<IEnumerable<WorkspaceInfo>> GetAllWorkspacesAsync();

    /// <summary>
    /// 确保工作区已授权
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    /// <returns>是否授权成功</returns>
    Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace);

    /// <summary>
    /// 加载工作区配置
    /// </summary>
    /// <param name="workspace">工作区信息</param>
    Task LoadWorkspaceConfigAsync(WorkspaceInfo workspace);

    /// <summary>
    /// 轻量设置当前工作区（不执行完整切换流程，用于启动时）
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

    private static WorkspaceInfo? _current;

    /// <summary>
    /// 当前工作区（静态访问，供非 DI 组件如 SqliteVectorStore、SessionManager 使用）
    /// </summary>
    public static WorkspaceInfo? Current => _current;

    /// <summary>
    /// 当前工作区
    /// </summary>
    public WorkspaceInfo? CurrentWorkspace => _current;

    /// <summary>
    /// 创建 WorkspaceManager 实例
    /// </summary>
    public WorkspaceManager(
        WorkspaceRepository repo,
        SessionRepository sessionRepo,
        ISessionManager sessionManager,
        IOptions<LuBanAgentOptions> options)
    {
        _repo = repo;
        _sessionRepo = sessionRepo;
        _sessionManager = sessionManager;
        _options = options;
    }

    /// <summary>
    /// 创建工作区
    /// </summary>
    public async Task<WorkspaceInfo> CreateWorkspaceAsync(string rootPath, string? name = null, string type = "Normal")
    {
        var ws = new DbWorkspace
        {
            WorkspaceId = Guid.NewGuid().ToString("N"),
            Name = name ?? Path.GetFileName(rootPath),
            RootPath = Path.GetFullPath(rootPath),
            Type = type,
            IsAuthorized = false,
            ConfigPath = ".luban-agent",
            CreateTime = DateTime.Now,
            IsDelete = false
        };
        await _repo.InsertAsync(ws);
        InitializeConfigDirectory(ws.RootPath, type);
        return ToWorkspaceInfo(ws);
    }

    /// <summary>
    /// 切换工作区
    /// </summary>
    public async Task SwitchWorkspaceAsync(string workspaceId)
    {
        if (_current != null && _current.IsAuthorized)
            RemoveWorkspaceRootFromPathGuard(_current.RootPath);

        var ws = await _repo.GetByWorkspaceIdAsync(workspaceId);
        if (ws == null) throw new InvalidOperationException($"工作区不存在: {workspaceId}");

        _current = ToWorkspaceInfo(ws);

        if (_current.IsAuthorized)
            AddWorkspaceRootToPathGuard(_current.RootPath);

        var latest = await _sessionRepo.GetLatestSessionAsync(workspaceId);
        if (latest != null)
            await _sessionManager.SetCurrentSessionAsync(latest.SessionId);
        else
            _sessionManager.ClearCurrentSession();

        await LoadWorkspaceConfigAsync(_current);
        await _repo.UpdateLastActiveAtAsync(workspaceId);
    }

    /// <summary>
    /// 轻量设置当前工作区（不执行完整切换流程，用于启动时）
    /// </summary>
    public async Task SetCurrentAsync(string workspaceId)
    {
        var ws = await _repo.GetByWorkspaceIdAsync(workspaceId);
        if (ws == null) return;
        _current = ToWorkspaceInfo(ws);
        if (_current.IsAuthorized)
            AddWorkspaceRootToPathGuard(_current.RootPath);
    }

    /// <summary>
    /// 确保工作区已授权
    /// </summary>
    public async Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]═══ 工作区授权确认 ═══[/]");
        AnsiConsole.MarkupLine($"[grey]工作区: {Markup.Escape(workspace.Name)}[/]");
        AnsiConsole.MarkupLine($"[grey]根目录: {Markup.Escape(workspace.RootPath)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]⚠️  AI Agent 将被授权访问此目录及其子目录：[/]");
        AnsiConsole.MarkupLine("[yellow]  - 读取文件[/]");
        AnsiConsole.MarkupLine("[yellow]  - 写入/修改文件（需二次确认）[/]");
        AnsiConsole.MarkupLine("[yellow]  - 执行脚本（需二次确认）[/]");
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm("[yellow]是否授权？[/]", defaultValue: true);
        if (!confirm)
        {
            AnsiConsole.MarkupLine("[red]✗ 工作区未授权，操作失败[/]");
            return false;
        }

        await _repo.UpdateAuthorizationAsync(workspace.WorkspaceId, true);
        workspace.IsAuthorized = true;
        AddWorkspaceRootToPathGuard(workspace.RootPath);
        AnsiConsole.MarkupLine("[green]✓ 已授权工作区[/]");
        return true;
    }

    /// <summary>
    /// 加载工作区配置
    /// </summary>
    public Task LoadWorkspaceConfigAsync(WorkspaceInfo workspace)
    {
        if (workspace.ConfigPath != null)
        {
            var configDir = Path.Combine(workspace.RootPath, workspace.ConfigPath);
            if (!Directory.Exists(configDir))
            {
                try { InitializeConfigDirectory(workspace.RootPath, workspace.Type); }
                catch { }
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取所有工作区
    /// </summary>
    public async Task<IEnumerable<WorkspaceInfo>> GetAllWorkspacesAsync()
    {
        var list = await _repo.GetAllAsync();
        return list.Select(ToWorkspaceInfo);
    }

    /// <summary>
    /// 初始化配置目录
    /// </summary>
    private void InitializeConfigDirectory(string rootPath, string type)
    {
        var configDir = Path.Combine(rootPath, ".luban-agent");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(Path.Combine(configDir, "skills"));
        Directory.CreateDirectory(Path.Combine(configDir, "rules"));
        Directory.CreateDirectory(Path.Combine(configDir, "mcps"));

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
    /// 将工作区根目录加入 PathGuard 允许列表
    /// </summary>
    private void AddWorkspaceRootToPathGuard(string rootPath)
    {
        var normalized = Path.GetFullPath(rootPath);
        if (!normalized.EndsWith(Path.DirectorySeparatorChar))
            normalized += Path.DirectorySeparatorChar;

        var roots = _options.Value.Tools.FileSystem.AllowedRoots ?? new List<string>();
        if (!roots.Any(r => string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            roots = new List<string>(roots) { normalized };
            _options.Value.Tools.FileSystem.AllowedRoots = roots;
        }
    }

    /// <summary>
    /// 将工作区根目录从 PathGuard 允许列表移除
    /// </summary>
    private void RemoveWorkspaceRootFromPathGuard(string rootPath)
    {
        var normalized = Path.GetFullPath(rootPath);
        if (!normalized.EndsWith(Path.DirectorySeparatorChar))
            normalized += Path.DirectorySeparatorChar;

        var roots = _options.Value.Tools.FileSystem.AllowedRoots ?? new List<string>();
        roots = roots.Where(r => !string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        _options.Value.Tools.FileSystem.AllowedRoots = roots;
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
}
