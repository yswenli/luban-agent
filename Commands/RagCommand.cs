/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： RagCommand
*版本号： V1.0.0.0
*唯一标识：RAG 知识库管理命令
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：RAG 知识库管理命令（new/index/search/list/delete）
*
*****************************************************************************/
using LuBan.AIAgent.Retrieval;
using LubanAgent.App;

namespace LubanAgent.Commands;

/// <summary>
/// RAG 知识库管理命令（new/index/search/list/delete）
/// </summary>
public class RagCommand : CommandBase
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly WorkspaceRepository _workspaceRepo;
    private readonly IRetrievalService _retrievalService;
    private readonly RagFileRepository _ragFileRepo;
    private readonly RagChunkRepository _ragChunkRepo;
    private readonly SessionRepository _sessionRepo;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "rag";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "知识库管理（-new/-index/-search/-list/-delete）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="workspaceManager">工作区管理器</param>
    /// <param name="workspaceRepo">工作区仓储</param>
    /// <param name="retrievalService">检索服务</param>
    /// <param name="ragFileRepo">RAG 文件仓储</param>
    /// <param name="ragChunkRepo">RAG 分块仓储</param>
    /// <param name="sessionRepo">会话仓储</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public RagCommand(
        ConfigManager configManager,
        IConfiguration configuration,
        IWorkspaceManager workspaceManager,
        WorkspaceRepository workspaceRepo,
        IRetrievalService retrievalService,
        RagFileRepository ragFileRepo,
        RagChunkRepository ragChunkRepo,
        SessionRepository sessionRepo,
        ITuiOutputWriter writer,
        ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _workspaceManager = workspaceManager;
        _workspaceRepo = workspaceRepo;
        _retrievalService = retrievalService;
        _ragFileRepo = ragFileRepo;
        _ragChunkRepo = ragChunkRepo;
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
            case "-new":
            case "-n":
                await NewRagWorkspace(args);
                break;
            case "-index":
            case "-i":
                await IndexFiles(args);
                break;
            case "-search":
            case "-s":
                await Search(args);
                break;
            case "-list":
            case "-l":
                await ListIndexedFiles();
                break;
            case "-delete":
            case "-d":
                await DeleteWorkspace(args);
                break;
            case "-help":
            case "-h":
                ShowHelp();
                break;
            default:
                Console.WriteLine($"未知子命令: {subCommand}");
                ShowHelp();
                break;
        }
        return true;
    }

    /// <summary>
    /// 显示帮助
    /// </summary>
    private void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("RAG 知识库管理命令用法：");
        Console.WriteLine("  /rag -new <路径> [名称]     - 创建 RAG 知识库工作区");
        Console.WriteLine("  /rag -index [glob]          - 索引当前 RAG 工作区的文件（glob 如 *.md）");
        Console.WriteLine("  /rag -search <查询>         - 在当前 RAG 工作区中检索");
        Console.WriteLine("  /rag -list                  - 列出当前 RAG 工作区已索引的文件");
        Console.WriteLine("  /rag -delete <名称或路径>   - 删除 RAG 知识库工作区及其索引");
        Console.WriteLine("  简写: /rag -n, /rag -i, /rag -s, /rag -l, /rag -d");
    }

    /// <summary>
    /// 创建 RAG 知识库工作区
    /// </summary>
    private async Task NewRagWorkspace(string[] args)
    {
        if (args.Length < 2)
        {
            WriteError("用法: /rag -new <路径> [名称]");
            return;
        }

        var path = args[1];
        if (!Directory.Exists(path))
        {
            WriteError($"目录不存在: {path}");
            return;
        }

        var name = args.Length > 2 ? args[2] : null;

        try
        {
            var ws = await _workspaceManager.CreateWorkspaceAsync(path, name, type: "Rag");
            AnsiConsole.MarkupLine($"[green]✓ 已创建 RAG 知识库: {Markup.Escape(ws.Name)} - {Markup.Escape(ws.RootPath)}[/]");
            AnsiConsole.MarkupLine($"[grey]使用 /work -switch {Markup.Escape(ws.Name)} 切换到此知识库[/]");
            AnsiConsole.MarkupLine("[grey]切换后使用 /rag -index 索引文件，然后 /agi 进行问答[/]");
        }
        catch (InvalidOperationException ex)
        {
            WriteError(ex.Message);
        }
    }

    /// <summary>
    /// 索引当前 RAG 工作区的文件
    /// </summary>
    private async Task IndexFiles(string[] args)
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            WriteError("请先使用 /work -switch 切换到 RAG 工作区");
            return;
        }

        if (workspace.Type != "Rag")
        {
            WriteError($"当前工作区 {Markup.Escape(workspace.Name)} 不是 RAG 知识库，请切换到 RAG 工作区");
            return;
        }

        if (!workspace.IsAuthorized)
        {
            var authorized = await _workspaceManager.EnsureAuthorizedAsync(workspace);
            if (!authorized) return;
        }

        var glob = args.Length > 1 ? args[1] : null;
        AnsiConsole.MarkupLine($"[cyan]开始索引工作区: {Markup.Escape(workspace.Name)}[/]");
        if (!string.IsNullOrEmpty(glob))
            AnsiConsole.MarkupLine($"[grey]文件匹配模式: {Markup.Escape(glob)}[/]");

        try
        {
            var report = await _retrievalService.IndexDirectoryAsync(workspace.RootPath, glob, force: false);
            AnsiConsole.MarkupLine($"[green]✓ 索引完成[/]");
            AnsiConsole.MarkupLine($"[grey]  扫描文件: {report.ScannedFiles}[/]");
            AnsiConsole.MarkupLine($"[grey]  新增文件: {report.NewFiles}[/]");
            AnsiConsole.MarkupLine($"[grey]  更新文件: {report.UpdatedFiles}[/]");
            AnsiConsole.MarkupLine($"[grey]  跳过文件: {report.SkippedFiles}[/]");
            AnsiConsole.MarkupLine($"[grey]  总切块数: {report.TotalChunks}[/]");
            if (report.Errors.Count > 0)
            {
                AnsiConsole.MarkupLine("[yellow]  错误信息：[/]");
                foreach (var err in report.Errors.Take(5))
                    AnsiConsole.MarkupLine($"  [red]- {Markup.Escape(err)}[/]");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("RAG 索引失败", ex);
            WriteError($"索引失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 在当前 RAG 工作区中检索
    /// </summary>
    private async Task Search(string[] args)
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            WriteError("请先使用 /work -switch 切换到 RAG 工作区");
            return;
        }

        if (workspace.Type != "Rag")
        {
            WriteError($"当前工作区 {Markup.Escape(workspace.Name)} 不是 RAG 知识库");
            return;
        }

        if (args.Length < 2)
        {
            WriteError("用法: /rag -search <查询内容>");
            return;
        }

        var query = string.Join(' ', args[1..]);

        try
        {
            var results = await _retrievalService.SearchAsync(query, topK: 5);
            if (results.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]未找到相关文档[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[green]找到 {results.Count} 条相关结果：[/]");
            AnsiConsole.WriteLine();
            foreach (var r in results)
            {
                AnsiConsole.MarkupLine($"[cyan]文件: {Markup.Escape(r.FilePath)}[/]");
                if (!string.IsNullOrEmpty(r.SymbolName))
                    AnsiConsole.MarkupLine($"[grey]符号: {Markup.Escape(r.SymbolName)} (L{r.StartLine}-{r.EndLine})[/]");
                AnsiConsole.MarkupLine($"[grey]内容:[/]");
                Console.WriteLine(r.Content);
                AnsiConsole.WriteLine("---");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("RAG 检索失败", ex);
            WriteError($"检索失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 列出当前 RAG 工作区已索引的文件
    /// </summary>
    private async Task ListIndexedFiles()
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            WriteError("请先使用 /work -switch 切换到 RAG 工作区");
            return;
        }

        if (workspace.Type != "Rag")
        {
            WriteError($"当前工作区 {Markup.Escape(workspace.Name)} 不是 RAG 知识库");
            return;
        }

        var files = await _ragFileRepo.GetByWorkspaceAsync(workspace.WorkspaceId);
        if (files.Count == 0)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Green;
                AnsiConsole.MarkupLine("[grey]当前知识库尚未索引任何文件，请使用 /rag -index 索引文件[/]");
            }
            finally
            {
                Console.ResetColor();
            }
            return;
        }

        var table = new Table();
        table.AddColumn("文件路径");
        table.AddColumn("语言");
        table.AddColumn("切块数");
        table.AddColumn("索引时间");

        foreach (var f in files)
        {
            table.AddRow(
                Markup.Escape(f.FilePath),
                f.Language ?? "-",
                f.ChunkCount.ToString(),
                f.IndexedTime.ToString("yyyy-MM-dd HH:mm"));
        }

        try
        {
            Console.ForegroundColor = ConsoleColor.Green;
            AnsiConsole.Write(table);
        }
        finally
        {
            Console.ResetColor();
        }
    }

    /// <summary>
    /// 删除 RAG 知识库工作区（复用 WorkCommand 的删除逻辑）
    /// </summary>
    private async Task DeleteWorkspace(string[] args)
    {
        if (args.Length < 2)
        {
            WriteError("用法: /rag -delete <名称或路径>");
            return;
        }

        var keyword = args[1];
        var allWorkspaces = await _workspaceRepo.GetAllAsync();

        // 优先精确匹配名称
        var target = allWorkspaces.FirstOrDefault(w =>
            string.Equals(w.Name, keyword, StringComparison.OrdinalIgnoreCase) && w.Type == "Rag");

        // 精确匹配失败时尝试按路径匹配
        if (target == null)
        {
            try
            {
                var fullPath = Path.GetFullPath(keyword);
                target = allWorkspaces.FirstOrDefault(w =>
                    string.Equals(w.RootPath, fullPath, StringComparison.OrdinalIgnoreCase) && w.Type == "Rag");
            }
            catch
            {
                // 路径不合法，忽略
            }
        }

        if (target == null)
        {
            WriteError($"找不到 RAG 知识库: {keyword}");
            return;
        }

        var confirm = AnsiConsole.Confirm($"[yellow]删除 RAG 知识库将同时删除其下所有会话和索引，确认删除 '{Markup.Escape(target.Name)}'？[/]", defaultValue: false);
        if (!confirm) return;

        // 级联删除（与 WorkCommand 保持一致，使用 DI 注入的仓储）
        await _sessionRepo.SoftDeleteByWorkspaceAsync(target.WorkspaceId);
        await _ragFileRepo.DeleteByWorkspaceAsync(target.WorkspaceId);
        await _ragChunkRepo.DeleteByWorkspaceAsync(target.WorkspaceId);
        await _workspaceRepo.LogicDeleteAsync(w => w.WorkspaceId == target.WorkspaceId);

        AnsiConsole.MarkupLine($"[green]✓ 已删除 RAG 知识库: {Markup.Escape(target.Name)}[/]");
    }
}
