/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： WorkCommand
*版本号： V1.0.0.0
*唯一标识：工作区管理命令
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：工作区管理命令（list/new/switch/delete/info/authorize）
*
*****************************************************************************/
using LubanAgent.App;

namespace LubanAgent.Commands;

/// <summary>
/// 工作区管理命令
/// </summary>
public class WorkCommand : CommandBase
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly WorkspaceRepository _workspaceRepo;
    private readonly SessionRepository _sessionRepo;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "work";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "工作区管理（-list/-new/-switch/-delete/-info/-authorize）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="workspaceManager">工作区管理器</param>
    /// <param name="workspaceRepo">工作区仓储</param>
    /// <param name="sessionRepo">会话仓储</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public WorkCommand(
        ConfigManager configManager,
        IConfiguration configuration,
        IWorkspaceManager workspaceManager,
        WorkspaceRepository workspaceRepo,
        SessionRepository sessionRepo,
        ITuiOutputWriter writer,
        ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _workspaceManager = workspaceManager;
        _workspaceRepo = workspaceRepo;
        _sessionRepo = sessionRepo;
    }

    /// <summary>
    /// 执行命令（无参数时显示帮助）
    /// </summary>
    public override Task ExecuteAsync()
    {
        ShowHelp();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行带子命令的命令
    /// </summary>
    public override async Task<bool> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return true;
        }

        var subCommand = args[0].ToLower();
        switch (subCommand)
        {
            case "-list":
            case "-l":
                await ListWorkspaces();
                break;
            case "-new":
            case "-n":
                await NewWorkspace(args);
                break;
            case "-switch":
            case "-s":
                await SwitchWorkspace(args);
                break;
            case "-delete":
            case "-d":
                await DeleteWorkspace(args);
                break;
            case "-info":
            case "-i":
                await ShowInfo();
                break;
            case "-authorize":
            case "-a":
            // 注意：-a 在 ConsoleAppService.SubCommandAliases 中被全局映射为 -add，
            // 因此 /work -a 实际到达本命令时已被展开为 -add。这里同时接受 -add 以保证快捷键可用。
            case "-add":
                await Authorize();
                break;
            default:
                Writer.WriteLine($"未知子命令: {subCommand}");
                ShowHelp();
                break;
        }
        return true;
    }

    /// <summary>
    /// 列出所有工作区
    /// </summary>
    private async Task ListWorkspaces()
    {
        var workspaces = (await _workspaceManager.GetUserWorkspacesAsync()).ToList();
        if (workspaces.Count == 0)
        {
            Writer.WriteInfo("暂无工作区");
            return;
        }

        var current = _workspaceManager.CurrentWorkspace;
        var rows = new List<IReadOnlyList<string>>();
        foreach (var ws in workspaces)
        {
            var sessions = await _sessionRepo.GetByWorkspaceAsync(ws.WorkspaceId);
            var isCurrent = current?.WorkspaceId == ws.WorkspaceId;
            var marker = isCurrent ? "* " : "  ";
            rows.Add(new[]
            {
                $"{marker}{ws.Name}",
                ws.Type == "Rag" ? "RAG" : "普通",
                ws.RootPath,
                ws.IsAuthorized ? "✓" : "✗",
                sessions.Count.ToString(),
                ws.LastActiveAt?.ToString("yyyy-MM-dd HH:mm") ?? "-"
            });
        }

        Ui.ShowTable("工作区列表", new[] { "名称", "类型", "根目录", "授权", "会话数", "最后活跃" }, rows);
    }

    /// <summary>
    /// 创建新工作区
    /// </summary>
    private async Task NewWorkspace(string[] args)
    {
        if (args.Length < 2)
        {
            Writer.WriteError("用法: /work -new <路径>");
            return;
        }

        var path = args[1];
        if (!Directory.Exists(path))
        {
            Writer.WriteError($"目录不存在: {path}");
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var existing = await _workspaceRepo.GetByRootPathAsync(fullPath);
        if (existing != null)
        {
            Writer.WriteError($"工作区已存在: {existing.Name} ({existing.RootPath})");
            return;
        }

        var ws = await _workspaceManager.CreateWorkspaceAsync(path, type: "Normal");
        Writer.WriteSuccess($"已创建工作区: {ws.Name} - {ws.RootPath}");
        Writer.WriteInfo($"输入 /work -switch {ws.Name} 切换到此工作区");
    }

    /// <summary>
    /// 切换工作区
    /// </summary>
    private async Task SwitchWorkspace(string[] args)
    {
        if (args.Length < 2)
        {
            Writer.WriteError("用法: /work -switch <名称或路径>");
            return;
        }

        var keyword = args[1];

        // 优先精确匹配名称，避免模糊匹配误切
        DbWorkspace? target = null;
        var allWorkspaces = await _workspaceRepo.GetAllAsync();
        target = allWorkspaces.FirstOrDefault(w =>
            string.Equals(w.Name, keyword, StringComparison.OrdinalIgnoreCase));

        // 精确匹配失败时尝试按路径匹配
        if (target == null)
        {
            try
            {
                var fullPath = Path.GetFullPath(keyword);
                target = allWorkspaces.FirstOrDefault(w =>
                    string.Equals(w.RootPath, fullPath, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // 路径不合法，忽略
            }
        }

        // 仍找不到，最后使用模糊匹配（仅当唯一匹配时才切换）
        if (target == null)
        {
            var fuzzyMatches = await _workspaceRepo.SearchByNameAsync(keyword);
            if (fuzzyMatches.Count == 1)
            {
                target = fuzzyMatches[0];
            }
            else if (fuzzyMatches.Count > 1)
            {
                Writer.WriteError("找到多个匹配的工作区，请更精确指定：");
                foreach (var ws in fuzzyMatches)
                    Writer.WriteInfo($"  - {ws.Name} ({ws.RootPath})");
                return;
            }
        }

        if (target == null)
        {
            Writer.WriteError($"找不到工作区: {keyword}");
            return;
        }

        await _workspaceManager.SwitchWorkspaceAsync(target.WorkspaceId);

        Writer.WriteSuccess($"已切换到工作区: {target.Name}");
        Writer.WriteInfo($"  根目录: {target.RootPath}");
        Writer.WriteInfo($"  授权状态: {(target.IsAuthorized ? "已授权" : "未授权")}");
        Writer.WriteInfo("  输入 /agi 开始工作");
    }

    /// <summary>
    /// 删除工作区
    /// </summary>
    private async Task DeleteWorkspace(string[] args)
    {
        if (args.Length < 2)
        {
            Writer.WriteError("用法: /work -delete <名称或路径>");
            return;
        }

        var keyword = args[1];

        // 优先精确匹配名称，避免模糊匹配误删
        DbWorkspace? target = null;
        var allWorkspaces = await _workspaceRepo.GetAllAsync();
        target = allWorkspaces.FirstOrDefault(w =>
            string.Equals(w.Name, keyword, StringComparison.OrdinalIgnoreCase));

        // 精确匹配失败时尝试按路径匹配
        if (target == null)
        {
            try
            {
                var fullPath = Path.GetFullPath(keyword);
                target = allWorkspaces.FirstOrDefault(w =>
                    string.Equals(w.RootPath, fullPath, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // 路径不合法，忽略
            }
        }

        // 仍找不到，最后使用模糊匹配（仅当唯一匹配时才删除）
        if (target == null)
        {
            var fuzzyMatches = await _workspaceRepo.SearchByNameAsync(keyword);
            if (fuzzyMatches.Count == 1)
            {
                target = fuzzyMatches[0];
            }
            else if (fuzzyMatches.Count > 1)
            {
                Writer.WriteError("找到多个匹配的工作区，请更精确指定：");
                foreach (var ws in fuzzyMatches)
                    Writer.WriteInfo($"  - {ws.Name} ({ws.RootPath})");
                return;
            }
        }

        if (target == null)
        {
            Writer.WriteError($"找不到工作区: {keyword}");
            return;
        }

        var confirm = Ui.Confirm("删除工作区", $"删除工作区将同时删除其下所有会话和索引，确认删除 '{target.Name}'？", defaultValue: false);
        if (!confirm) return;

        // 级联删除会话
        await _sessionRepo.SoftDeleteByWorkspaceAsync(target.WorkspaceId);

        // 物理删除 RAG 索引（文件 + 切块）
        var ragFileRepo = new RagFileRepository();
        var ragChunkRepo = new RagChunkRepository();
        await ragFileRepo.DeleteByWorkspaceAsync(target.WorkspaceId);
        await ragChunkRepo.DeleteByWorkspaceAsync(target.WorkspaceId);

        // 软删除工作区
        await _workspaceRepo.LogicDeleteAsync(w => w.WorkspaceId == target.WorkspaceId);

        // 若删除的是当前工作区，清理授权状态并提示用户切换
        if (_workspaceManager.CurrentWorkspace?.WorkspaceId == target.WorkspaceId)
        {
            Writer.WriteWarning("当前工作区已删除，请使用 /work -switch 切换到其他工作区");
        }

        Writer.WriteSuccess($"已删除工作区: {target.Name}");
    }

    /// <summary>
    /// 显示当前工作区详情
    /// </summary>
    private async Task ShowInfo()
    {
        var ws = _workspaceManager.CurrentWorkspace;
        if (ws == null)
        {
            Writer.WriteError("当前无活动工作区");
            return;
        }

        var sessions = await _sessionRepo.GetByWorkspaceAsync(ws.WorkspaceId);
        Writer.WriteHeader($"工作区: {ws.Name}");
        Writer.WriteInfo($"  类型: {(ws.Type == "Rag" ? "RAG" : "普通")}");
        Writer.WriteInfo($"  根目录: {ws.RootPath}");
        Writer.WriteInfo($"  授权状态: {(ws.IsAuthorized ? "已授权" : "未授权")}");
        Writer.WriteInfo($"  会话数: {sessions.Count}");
        Writer.WriteInfo($"  最后活跃: {ws.LastActiveAt?.ToString("yyyy-MM-dd HH:mm") ?? "-"}");
    }

    /// <summary>
    /// 授权当前工作区
    /// </summary>
    private async Task Authorize()
    {
        var ws = _workspaceManager.CurrentWorkspace;
        if (ws == null)
        {
            Writer.WriteError("当前无活动工作区");
            return;
        }

        if (ws.IsAuthorized)
        {
            Writer.WriteSuccess("工作区已授权");
            return;
        }

        await _workspaceManager.EnsureAuthorizedAsync(ws);
    }

    /// <summary>
    /// 显示帮助
    /// </summary>
    private void ShowHelp()
    {
        Writer.WriteLine();
        Writer.WriteHeader("工作区管理用法：");
        Writer.WriteLine("  /work -list               - 列出所有工作区");
        Writer.WriteLine("  /work -new <路径>         - 创建新工作区");
        Writer.WriteLine("  /work -switch <名称>      - 切换工作区（按名称或路径）");
        Writer.WriteLine("  /work -delete <名称>      - 删除工作区（级联删除会话和索引）");
        Writer.WriteLine("  /work -info               - 显示当前工作区详情");
        Writer.WriteLine("  /work -authorize          - 授权当前工作区");
        Writer.WriteLine("  简写: /w -l, /w -n 路径, /w -s 名称, /w -d 名称, /w -i, /w -a");
    }
}
