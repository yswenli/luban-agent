/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.Commands
*文件名： WikiCommand
*版本号： V1.0.0.0
*唯一标识：ba432db1-cbad-46c3-91ce-72886d7bb42f
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/20
*描述：LLM Wiki 管理命令（status/ingest/lint/rebuild/search）
*
 *****************************************************************************/
using LuBan.AIAgent.Wiki;
using LubanAgentCli.App.ViewModels;

namespace LubanAgentCli.Commands;

/// <summary>
/// LLM Wiki 管理命令（-status/-ingest/-lint/-rebuild/-search）。
/// 依赖内部 ConversationViewModel（-ingest 注入附件与指令），故命令本身为 internal。
/// </summary>
internal sealed class WikiCommand : CommandBase
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IWikiService _wiki;
    private readonly ConversationViewModel? _conversation;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "wiki";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "LLM Wiki 管理（-status/-ingest/-lint/-rebuild/-search）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="workspaceManager">工作区管理器</param>
    /// <param name="wiki">wiki 知识库服务</param>
    /// <param name="conversation">会话 ViewModel（可为 null）</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public WikiCommand(
        ConfigManager configManager,
        IConfiguration configuration,
        IWorkspaceManager workspaceManager,
        IWikiService wiki,
        ConversationViewModel? conversation,
        ITuiOutputWriter writer,
        ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _workspaceManager = workspaceManager;
        _wiki = wiki;
        _conversation = conversation;
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

        var subCommand = args[0].ToLowerInvariant();
        switch (subCommand)
        {
            case "-status":
                await StatusAsync();
                break;
            case "-ingest":
                await IngestAsync(args);
                break;
            case "-lint":
                await LintAsync();
                break;
            case "-rebuild":
                await RebuildAsync();
                break;
            case "-search":
                await SearchAsync(args);
                break;
            case "-help":
            case "-h":
                ShowHelp();
                break;
            default:
                Writer.WriteError($"未知子命令: {subCommand}");
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
        Writer.WriteLine();
        Writer.WriteLine("LLM Wiki 命令用法：");
        Writer.WriteLine("  /wiki -status              - 显示 wiki 统计");
        Writer.WriteLine("  /wiki -ingest <路径|glob>  - 让 LLM 读取源文件并写入 wiki");
        Writer.WriteLine("  /wiki -lint                - 检查孤儿页/死链/未收录/来源缺失");
        Writer.WriteLine("  /wiki -rebuild             - 重建 wiki 向量索引（逐页，排除 log.md）");
        Writer.WriteLine("  /wiki -search <查询>       - 在 wiki 中搜索");
    }

    /// <summary>
    /// 校验当前工作区为 RAG 知识库
    /// </summary>
    private bool EnsureRagWorkspace()
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            Writer.WriteError("请先使用 /work -switch 切换工作区");
            return false;
        }

        if (workspace.Type != "Rag")
        {
            Writer.WriteError($"当前工作区 {workspace.Name} 不是 RAG 知识库");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 显示 wiki 统计
    /// </summary>
    private async Task StatusAsync()
    {
        if (!EnsureRagWorkspace()) return;

        try
        {
            var stats = await _wiki.GetStatsAsync();
            Writer.WriteSuccess("wiki 状态");
            Writer.WriteInfo($"  页面数: {stats.PageCount}");
            Writer.WriteInfo($"  最近重建: {stats.LastRebuildSummary ?? "无"}");
        }
        catch (Exception ex)
        {
            Logger.Error("wiki 状态读取失败", ex);
            Writer.WriteError($"读取状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 让 LLM 读取源文件并沉淀为 wiki 页面
    /// </summary>
    private async Task IngestAsync(string[] args)
    {
        if (!EnsureRagWorkspace()) return;

        if (args.Length < 2)
        {
            Writer.WriteError("用法: /wiki -ingest <路径|glob>");
            return;
        }

        if (_conversation == null)
        {
            Writer.WriteError("Agent 尚未初始化，请先输入内容启动 Agent");
            return;
        }

        var target = string.Join(' ', args[1..]);
        var workspace = _workspaceManager.CurrentWorkspace!;
        var attachments = ResolveFiles(workspace.RootPath, target);
        if (attachments.Count == 0)
        {
            Writer.WriteError($"未找到匹配文件: {target}");
            return;
        }

        var instruction = $"请读取以下源文件，按 wiki schema 将其沉淀为 wiki 页面（先读 index，必要时并页），并为相关实体/概念建页互链：{target}";
        Writer.WriteInfo($"开始 ingest：{attachments.Count} 个文件");
        try
        {
            await _conversation.ProcessInputAsync(instruction, attachments);
        }
        catch (Exception ex)
        {
            Logger.Error("wiki ingest 失败", ex);
            Writer.WriteError($"ingest 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析工作区根下的文件：单文件 / glob / 目录
    /// </summary>
    private static List<string> ResolveFiles(string rootPath, string target)
    {
        var full = Path.IsPathRooted(target) ? target : Path.Combine(rootPath, target);
        if (File.Exists(full)) return new List<string> { full };

        var dir = Path.GetDirectoryName(full);
        var pattern = Path.GetFileName(full);
        if (dir != null && Directory.Exists(dir) && (pattern.Contains('*') || pattern.Contains('?')))
            return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).ToList();

        return Directory.Exists(full)
            ? Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).ToList()
            : new List<string>();
    }

    /// <summary>
    /// 检查孤儿页/死链/未收录/来源缺失
    /// </summary>
    private async Task LintAsync()
    {
        if (!EnsureRagWorkspace()) return;

        try
        {
            var report = await _wiki.LintAsync();
            Writer.WriteSuccess($"lint 完成：{report.PageCount} 页，{report.Findings.Count} 项发现");
            foreach (var f in report.Findings.Take(50))
                Writer.WriteInfo($"  [{f.Kind}] {f.Path}：{f.Detail}");
        }
        catch (Exception ex)
        {
            Logger.Error("wiki lint 失败", ex);
            Writer.WriteError($"lint 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 重建 wiki 向量索引（逐页，排除 log.md）
    /// </summary>
    private async Task RebuildAsync()
    {
        if (!EnsureRagWorkspace()) return;

        try
        {
            Writer.WriteInfo("开始重建 wiki 索引（排除 log.md）...");
            var report = await _wiki.RebuildIndexAsync();
            Writer.WriteSuccess($"重建完成：扫描 {report.ScannedFiles}，删除 {report.DeletedFiles}，切块 {report.TotalChunks}");
            if (report.Errors.Count > 0)
            {
                Writer.WriteWarning("  错误信息：");
                foreach (var err in report.Errors.Take(5))
                    Writer.WriteWarning($"  - {err}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("wiki 索引重建失败", ex);
            Writer.WriteError($"重建失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 在 wiki 中搜索
    /// </summary>
    private async Task SearchAsync(string[] args)
    {
        if (!EnsureRagWorkspace()) return;

        if (args.Length < 2)
        {
            Writer.WriteError("用法: /wiki -search <查询>");
            return;
        }

        try
        {
            var results = await _wiki.SearchAsync(string.Join(' ', args[1..]));
            if (results.Count == 0)
            {
                Writer.WriteWarning("未找到相关内容");
                return;
            }

            Writer.WriteSuccess($"找到 {results.Count} 条相关结果：");
            Writer.WriteLine();
            foreach (var r in results)
            {
                Writer.WriteInfo($"文件: {r.FilePath} (L{r.StartLine}-{r.EndLine})");
                Writer.WriteLine(r.Content);
                Writer.WriteLine("---");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("wiki 搜索失败", ex);
            Writer.WriteError($"搜索失败: {ex.Message}");
        }
    }
}