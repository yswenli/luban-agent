/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.Commands
*文件名： LlmWikiCommand
*版本号： V1.0.0.0
*唯一标识：LLM Wiki 知识库管理命令
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：LLM Wiki 知识库管理命令（工作区与索引：new/index/search/list/delete；wiki 运维：status/ingest/lint/rebuild）
*
*=================================================
*修改标记
*修改时间：2026/9/21
*修改人： yswenli
*版本号： V1.0.0.1
*描述：由 RagCommand 与 WikiCommand 合并为 LlmWikiCommand，统一 /llmwiki，保留 /rag、/wiki 等别名
*
*****************************************************************************/
using LuBan.AIAgent.Wiki;
using LuBan.AIAgent.Wiki.Extractors;
using LubanAgentCli.App.ViewModels;

namespace LubanAgentCli.Commands;

/// <summary>
/// LLM Wiki 知识库管理命令。
/// 依赖内部 ConversationViewModel（-ingest 注入附件与指令），故命令本身为 internal。
/// 业务（索引/检索/wiki 运维/工作区增删）全部经 <see cref="ILlmWikiManagementService"/>；
/// 本层只保留授权门禁、关键字匹配、进度窗与提示文案。
/// </summary>
internal sealed class LlmWikiCommand : CommandBase
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IWorkspaceManagementService _workspaces;
    private readonly ILlmWikiManagementService _wiki;
    private readonly ConversationViewModel? _conversation;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "llmwiki";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "LLM Wiki 知识库管理（-new/-index/-search/-list/-delete/-status/-ingest/-lint/-rebuild）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="workspaceManager">工作区管理器（当前工作区与授权门禁）</param>
    /// <param name="workspaces">工作区管理服务（-delete 的关键字匹配）</param>
    /// <param name="wiki">LLM Wiki 管理服务</param>
    /// <param name="conversation">会话 ViewModel（可为 null）</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public LlmWikiCommand(
        ConfigManager configManager,
        IConfiguration configuration,
        IWorkspaceManager workspaceManager,
        IWorkspaceManagementService workspaces,
        ILlmWikiManagementService wiki,
        ConversationViewModel? conversation,
        ITuiOutputWriter writer,
        ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _workspaceManager = workspaceManager;
        _workspaces = workspaces;
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
            case "-new":
            case "-n":
                await NewWorkspace(args);
                break;
            case "-index":
            case "-i":
                await IndexFiles(args);
                break;
            case "-search":
            case "-s":
                await SearchAsync(args);
                break;
            case "-list":
            case "-l":
                await ListIndexedFiles();
                break;
            case "-delete":
            case "-d":
                await DeleteWorkspace(args);
                break;
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
                await RebuildAsync(args);
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
        Writer.WriteLine("LLM Wiki 知识库管理命令用法（别名 /rag、/rg、/wiki、/wk）：");
        Writer.WriteLine("  /llmwiki -new <路径> [名称]     - 创建 LLM Wiki 知识库工作区（名称默认 llmwiki）");
        Writer.WriteLine("  /llmwiki -index [glob] [--wait] - 索引当前工作区的原始资料（默认后台执行，--wait 同步等待）");
        Writer.WriteLine("  /llmwiki -search <查询> [--wiki|--raw] - 检索知识库（默认 wiki 优先并回落原始资料；--wiki 仅 wiki，--raw 仅原始资料）");
        Writer.WriteLine("  /llmwiki -list                  - 列出当前工作区已索引的文件");
        Writer.WriteLine("  /llmwiki -delete <名称或路径>   - 删除知识库工作区及其索引");
        Writer.WriteLine("  /llmwiki -status                - 显示 wiki 统计");
        Writer.WriteLine("  /llmwiki -ingest <路径|glob> [--wait] - 提取源文件并交给 LLM 写入 wiki（默认后台提取，--wait 同步）");
        Writer.WriteLine("                               glob 支持 * / ?，递归匹配全部子目录");
        Writer.WriteLine("  /llmwiki -lint                  - 检查孤儿页/死链/未收录/来源缺失");
        Writer.WriteLine("  /llmwiki -rebuild [--wait]      - 重建 wiki 向量索引（逐页，排除 log.md；默认后台，--wait 同步）");
        Writer.WriteLine("  简写: -n/-i/-s/-l/-d");
    }

    /// <summary>
    /// 校验当前工作区为 LLM Wiki 知识库，并返回其信息（非知识库时返回 null）
    /// </summary>
    private WorkspaceInfo? EnsureWikiWorkspace()
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            Writer.WriteError("请先使用 /work -switch 切换工作区");
            return null;
        }

        if (workspace.Type != WorkspaceTypes.LlmWiki)
        {
            Writer.WriteError($"当前工作区 {workspace.Name} 不是 LLM Wiki 知识库");
            return null;
        }

        return workspace;
    }

    /// <summary>
    /// 创建 LLM Wiki 知识库工作区
    /// </summary>
    private async Task NewWorkspace(string[] args)
    {
        string? path = null;
        string? name = null;

        if (args.Length < 2)
        {
            var values = Ui.ShowForm("创建 LLM Wiki 知识库", new[]
            {
                new FormField("知识库路径"),
                new FormField("知识库名称（可选，默认 llmwiki）", Required: false)
            });
            if (values is null) return;

            path = values[0].Trim();
            name = string.IsNullOrWhiteSpace(values[1]) ? null : values[1].Trim();
        }
        else
        {
            path = args[1];
            name = args.Length > 2 ? args[2] : null;
        }

        if (!Directory.Exists(path))
        {
            Writer.WriteError($"目录不存在: {path}");
            return;
        }

        try
        {
            var ws = await _wiki.CreateAsync(path, name);
            Writer.WriteSuccess($"已创建 LLM Wiki 知识库: {ws.Name} - {ws.RootPath}");
            Writer.WriteInfo($"使用 /work -switch {ws.Name} 切换到此知识库");
            Writer.WriteInfo("切换后使用 /llmwiki -index 索引文件，然后 /agi 进行问答");
        }
        catch (InvalidOperationException ex)
        {
            Writer.WriteError(ex.Message);
        }
    }

    /// <summary>
    /// 索引当前知识库工作区的文件
    /// </summary>
    private async Task IndexFiles(string[] args)
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            Writer.WriteError("请先使用 /work -switch 切换到 LLM Wiki 知识库");
            return;
        }

        if (workspace.Type != WorkspaceTypes.LlmWiki)
        {
            Writer.WriteError($"当前工作区 {workspace.Name} 不是 LLM Wiki 知识库，请切换到知识库工作区");
            return;
        }

        if (!workspace.IsAuthorized)
        {
            var authorized = await _workspaceManager.EnsureAuthorizedAsync(workspace);
            if (!authorized) return;
        }

        var wait = args.Any(a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase));
        var glob = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        // 后台模式：必需部分（校验/授权）同步完成即返回，索引进后台并由 Updated 事件反馈
        if (!wait)
        {
            var task = await _wiki.StartIndexAsync(workspace.WorkspaceId, glob);
            if (task != null)
            {
                Writer.WriteInfo($"已转入后台：{task.Label}（任务 {task.Id[..8]}），完成后会在此提示。");
                Writer.WriteInfo("如需同步等待，请使用 /llmwiki -index --wait");
                return;
            }
        }

        Writer.WriteInfo($"开始索引工作区: {workspace.Name}");
        if (!string.IsNullOrEmpty(glob))
            Writer.WriteInfo($"文件匹配模式: {glob}");

        try
        {
            var maxFileSizeBytes = RetrievalIndexPlanner.GetMaxFileSizeBytes(Configuration);
            var fileCount = RetrievalIndexPlanner.Unknown;
            IndexReport? report = null;

            // 两段式提示：先"正在扫描目录"，预扫出文件数后更新为"共 N 个文件，正在生成向量"。
            // 进度窗设为不可关闭：索引属长耗时任务，中途关窗会让用户误判为已完成。
            Ui.RunWithProgress("索引知识库", RetrievalIndexPlanner.IndexScanStatus(workspace.RootPath), async status =>
            {
                fileCount = RetrievalIndexPlanner.CountIndexableFiles(workspace.RootPath, glob, maxFileSizeBytes,
                    scan => status(RetrievalIndexPlanner.IndexScanningStatus(scan.CurrentDirectory, scan.IndexableFiles)));
                status(RetrievalIndexPlanner.IndexEmbedStatus(fileCount));
                if (fileCount == 0) return;

                report = await _wiki.IndexFilesAsync(workspace.WorkspaceId, glob);
            }, closable: false);

            if (fileCount == 0)
            {
                Writer.WriteWarning(RetrievalIndexPlanner.NoIndexableFilesText);
                return;
            }

            var result = report ?? new IndexReport();
            Writer.WriteSuccess("索引完成");
            Writer.WriteInfo($"  扫描文件: {result.ScannedFiles}");
            Writer.WriteInfo($"  新增文件: {result.NewFiles}");
            Writer.WriteInfo($"  更新文件: {result.UpdatedFiles}");
            Writer.WriteInfo($"  跳过文件: {result.SkippedFiles}");
            Writer.WriteInfo($"  总切块数: {result.TotalChunks}");
            if (result.Errors.Count > 0)
            {
                Writer.WriteWarning("  错误信息：");
                foreach (var err in result.Errors.Take(5))
                    Writer.WriteError($"  - {err}");
            }
            if (result.Warnings.Count > 0)
            {
                Writer.WriteWarning("  警告信息：");
                foreach (var warn in result.Warnings.Take(5))
                    Writer.WriteWarning($"  - {warn}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("LLM Wiki 索引失败", ex);
            Writer.WriteError($"索引失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 在知识库中检索：默认 wiki 优先并回落原始资料；--wiki 仅 wiki，--raw 仅原始资料
    /// </summary>
    private async Task SearchAsync(string[] args)
    {
        var workspace = EnsureWikiWorkspace();
        if (workspace == null) return;

        var wikiOnly = args.Any(a => string.Equals(a, "--wiki", StringComparison.OrdinalIgnoreCase));
        var rawOnly = args.Any(a => string.Equals(a, "--raw", StringComparison.OrdinalIgnoreCase));
        var query = string.Join(' ', args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)));

        if (string.IsNullOrWhiteSpace(query))
        {
            var values = Ui.ShowForm("检索知识库", new[]
            {
                new FormField("查询内容")
            });
            if (values is null) return;

            query = values[0].Trim();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            Writer.WriteError("查询内容不能为空");
            return;
        }

        try
        {
            var scope = rawOnly
                ? WikiSearchScope.RawOnly
                : wikiOnly ? WikiSearchScope.WikiOnly : WikiSearchScope.WikiThenRaw;
            var results = await _wiki.SearchAsync(workspace.WorkspaceId, query, scope);

            if (results.Count == 0)
            {
                Writer.WriteWarning("未找到相关文档");
                return;
            }

            Writer.WriteSuccess($"找到 {results.Count} 条相关结果：");
            Writer.WriteLine();
            foreach (var r in results)
            {
                Writer.WriteInfo($"文件: {r.FilePath}");
                if (!string.IsNullOrEmpty(r.SymbolName))
                    Writer.WriteInfo($"符号: {r.SymbolName} (L{r.StartLine}-{r.EndLine})");
                Writer.WriteInfo("内容:");
                Writer.WriteLine(r.Content);
                Writer.WriteLine("---");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("LLM Wiki 检索失败", ex);
            Writer.WriteError($"检索失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 列出当前知识库工作区已索引的文件
    /// </summary>
    private async Task ListIndexedFiles()
    {
        var workspace = EnsureWikiWorkspace();
        if (workspace == null) return;

        var files = await _wiki.ListFilesAsync(workspace.WorkspaceId);
        if (files.Count == 0)
        {
            Writer.WriteInfo("当前知识库尚未索引任何文件，请使用 /llmwiki -index 索引文件");
            return;
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var f in files)
        {
            rows.Add(new[]
            {
                f.FilePath,
                f.Language ?? "-",
                f.ChunkCount.ToString(),
                f.IndexedTime.ToString("yyyy-MM-dd HH:mm")
            });
        }

        Ui.ShowTable("已索引文件", new[] { "文件路径", "语言", "切块数", "索引时间" }, rows);
    }

    /// <summary>
    /// 删除 LLM Wiki 知识库工作区
    /// </summary>
    private async Task DeleteWorkspace(string[] args)
    {
        string? keyword = null;
        if (args.Length < 2)
        {
            var allWorkspaces = await _workspaces.ListAsync();
            var wikiWorkspaces = allWorkspaces.Where(w => w.Type == WorkspaceTypes.LlmWiki).ToList();
            if (wikiWorkspaces.Count == 0)
            {
                Writer.WriteError("暂无 LLM Wiki 知识库");
                return;
            }

            var chosen = Ui.Choose("选择要删除的 LLM Wiki 知识库",
                wikiWorkspaces.Select(w => w.Name).ToList());
            if (chosen is null) return;

            keyword = wikiWorkspaces[chosen.Value].Name;
        }
        else
        {
            keyword = args[1];
        }

        var allWorkspacesList = await _workspaces.ListAsync();

        // 优先精确匹配名称
        var target = allWorkspacesList.FirstOrDefault(w =>
            string.Equals(w.Name, keyword, StringComparison.OrdinalIgnoreCase) && w.Type == WorkspaceTypes.LlmWiki);

        // 精确匹配失败时尝试按路径匹配
        if (target == null)
        {
            try
            {
                var fullPath = Path.GetFullPath(keyword);
                target = allWorkspacesList.FirstOrDefault(w =>
                    string.Equals(w.RootPath, fullPath, StringComparison.OrdinalIgnoreCase) && w.Type == WorkspaceTypes.LlmWiki);
            }
            catch
            {
                // 路径不合法，忽略
            }
        }

        if (target == null)
        {
            Writer.WriteError($"找不到 LLM Wiki 知识库: {keyword}");
            return;
        }

        var confirm = Ui.Confirm("删除 LLM Wiki 知识库", $"删除 LLM Wiki 知识库将同时删除其下所有会话和索引，确认删除 '{target.Name}'？", defaultValue: false);
        if (!confirm) return;

        // 统一删除入口（与 /work -delete 一致）：会话软删 → LLM Wiki 索引物理删 → 工作区逻辑删；
        // 删除当前工作区时还会移除 PathGuard 根目录、清空当前工作区与会话引用。
        await _wiki.DeleteAsync(target.WorkspaceId);

        Writer.WriteSuccess($"已删除 LLM Wiki 知识库: {target.Name}");
    }

    /// <summary>
    /// 显示 wiki 统计
    /// </summary>
    private async Task StatusAsync()
    {
        var workspace = EnsureWikiWorkspace();
        if (workspace == null) return;

        try
        {
            var stats = await _wiki.GetStatusAsync(workspace.WorkspaceId);
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
        var workspace = EnsureWikiWorkspace();
        if (workspace == null) return;

        if (args.Length < 2)
        {
            Writer.WriteError("用法: /llmwiki -ingest <工作区内路径|glob> [--wait]");
            return;
        }

        var wait = args.Any(a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase));
        var target = string.Join(' ', args[1..].Where(a => !string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase)));

        // 后台模式：提取阶段转后台（提取是耗时大头），完成后由 Updated 事件继续交给 Agent 整理
        if (!wait)
        {
            var task = await _wiki.StartIngestAsync(workspace.WorkspaceId, target);
            if (task != null)
            {
                Writer.WriteInfo($"已转入后台：{task.Label}（任务 {task.Id[..8]}），提取完成后将自动交给 Agent 整理。");
                Writer.WriteInfo("如需同步等待，请使用 /llmwiki -ingest <路径> --wait");
                return;
            }
        }

        if (_conversation == null)
        {
            Writer.WriteError("Agent 尚未初始化，请先输入内容启动 Agent");
            return;
        }

        // 提取阶段逐文件更新进度（不可关闭）：大文件/Office 转换可能明显耗时，
        // 提取完成后关闭进度窗，再交回 Agent 正常流式展现。
        WikiIngestDraft? draft = null;
        try
        {
            Ui.RunWithProgress("提取源文件", "正在提取源文件…", async status =>
            {
                draft = await _wiki.PrepareIngestAsync(
                    workspace.WorkspaceId, target, new SynchronousProgress<string>(status));
            }, closable: false);
        }
        catch (InvalidOperationException ex)
        {
            // 未找到匹配文件：与既有文案一致
            Writer.WriteError(ex.Message);
            return;
        }

        if (draft is null) return;

        foreach (var skipped in draft.Skipped ?? Array.Empty<string>())
            Writer.WriteWarning(skipped);

        if (draft.MergedFiles == 0)
        {
            Writer.WriteError($"所有源文件均提取失败: {target}");
            return;
        }

        if (draft.Truncated)
            Writer.WriteWarning($"汇总内容超过 {TextExtractor.MaxChars} 字符，已截断");

        var instruction = $"请读取附件中的源文件汇总（{draft.RelativePath}），按 wiki schema 将其沉淀为 wiki 页面（先读 index，必要时并页），并为相关实体/概念建页互链。";
        Writer.WriteInfo($"开始 ingest：汇总 {draft.MergedFiles} 个源文件 → {draft.RelativePath}");
        try
        {
            await _conversation.ProcessInputAsync(instruction,
                new[] { new AttachmentDraft(draft.TempPath, Path.GetFileName(draft.TempPath)) });
        }
        catch (Exception ex)
        {
            Logger.Error("wiki ingest 失败", ex);
            Writer.WriteError($"ingest 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 同步转发的进度适配器：服务内的提取回调直接驱动 TUI 进度窗文案。
    /// </summary>
    /// <remarks>
    /// 不用 <see cref="Progress{T}"/>：它会把回调投递到同步上下文，进度窗关闭后仍可能触发并抛异常。
    /// </remarks>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>
    /// 检查孤儿页/死链/未收录/来源缺失
    /// </summary>
    private async Task LintAsync()
    {
        var workspace = EnsureWikiWorkspace();
        if (workspace == null) return;

        try
        {
            var pageCount = RetrievalIndexPlanner.Unknown;
            WikiLintReport? report = null;

            // 两段式提示 + 不可关闭进度窗：lint 需逐页解析，避免用户误以为命令卡住。
            Ui.RunWithProgress("wiki lint", RetrievalIndexPlanner.WikiScanStatus, async status =>
            {
                pageCount = RetrievalIndexPlanner.CountWikiPages(workspace.RootPath);
                status(RetrievalIndexPlanner.WikiPagesStatus(pageCount, "正在检查孤儿页/死链/未收录"));
                if (pageCount == 0) return;

                report = await _wiki.LintAsync(workspace.WorkspaceId);
            }, closable: false);

            if (pageCount == 0)
            {
                Writer.WriteWarning(RetrievalIndexPlanner.NoWikiPagesText);
                return;
            }

            var result = report ?? new WikiLintReport();
            Writer.WriteSuccess($"lint 完成：{result.PageCount} 页，{result.Findings.Count} 项发现");
            foreach (var f in result.Findings.Take(50))
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
    private async Task RebuildAsync(string[] args)
    {
        var workspace = EnsureWikiWorkspace();
        if (workspace == null) return;

        var wait = args.Any(a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase));

        if (!wait)
        {
            var task = await _wiki.StartRebuildAsync(workspace.WorkspaceId);
            if (task != null)
            {
                Writer.WriteInfo($"已转入后台：{task.Label}（任务 {task.Id[..8]}），完成后会在此提示。");
                Writer.WriteInfo("如需同步等待，请使用 /llmwiki -rebuild --wait");
                return;
            }
        }

        try
        {
            Writer.WriteInfo("开始重建 wiki 索引（排除 log.md）...");
            var pageCount = RetrievalIndexPlanner.Unknown;
            IndexReport? report = null;

            // 两段式提示 + 不可关闭进度窗：重建需逐页重新嵌入，避免用户误以为命令卡住。
            Ui.RunWithProgress("重建 wiki 索引", RetrievalIndexPlanner.WikiScanStatus, async status =>
            {
                pageCount = RetrievalIndexPlanner.CountWikiPages(workspace.RootPath);
                status(RetrievalIndexPlanner.WikiPagesStatus(pageCount, "正在重建向量索引"));
                if (pageCount == 0) return;

                report = await _wiki.RebuildAsync(workspace.WorkspaceId);
            }, closable: false);

            if (pageCount == 0)
            {
                Writer.WriteWarning(RetrievalIndexPlanner.NoWikiPagesText);
                return;
            }

            var result = report ?? new IndexReport();
            Writer.WriteSuccess($"重建完成：扫描 {result.ScannedFiles}，删除 {result.DeletedFiles}，切块 {result.TotalChunks}");
            if (result.Errors.Count > 0)
            {
                Writer.WriteWarning("  错误信息：");
                foreach (var err in result.Errors.Take(5))
                    Writer.WriteWarning($"  - {err}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("wiki 索引重建失败", ex);
            Writer.WriteError($"重建失败: {ex.Message}");
        }
    }
}