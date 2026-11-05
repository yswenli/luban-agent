/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： LlmWikiManageWindow
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：LLM Wiki 知识库管理窗口，用于管理 LLM Wiki 知识库
*
*****************************************************************************/

namespace LubanAgentCodex.Views;

/// <summary>
/// LLM Wiki 知识库管理窗口
/// </summary>
public partial class LlmWikiManageWindow : Window
{
    private readonly DiContainer _services;
    private readonly IWorkspaceManager _workspaceManager;
    // 窗口级作用域：管理服务为 scoped，窗口生命周期内复用一份，关闭时释放
    private readonly DiContainer _scope;
    private readonly ILlmWikiManagementService _wiki;
    private ListBox? _llmWikiListBox;
    private ListBox? _resultListBox;
    private Button? _createButton;
    private Button? _indexButton;
    private Button? _searchButton;
    private Button? _deleteButton;
    private Button? _backButton;
    private Button? _wikiRebuildButton;
    private Button? _wikiLintButton;
    private BusyOverlay? _busyOverlay;
    private StackPanel? _toolBar;
    private TextBlock? _viewTitleText;
    private TextBlock? _emptyHint;

    /// <summary>
    /// 初始化 LLM Wiki 知识库管理窗口并加载 XAML 定义：创建窗口级用户作用域，
    /// 随后列出可管理（索引、检索、重建、lint）的知识库工作区。
    /// </summary>
    /// <param name="services">应用服务容器，用于创建工作区隔离的作用域并关闭窗口时释放。</param>
    public LlmWikiManageWindow(DiContainer services)
    {
        _services = services;
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        _scope = services.CreateUserScope(services.ResolveUserKey());
        _wiki = _scope.GetRequiredService<ILlmWikiManagementService>();
        Closed += (_, _) => _scope.Dispose();
        InitializeComponent();
        LoadLlmWikiWorkspaces();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _llmWikiListBox = this.FindControl<ListBox>("LlmWikiListBox");
        _resultListBox = this.FindControl<ListBox>("ResultListBox");
        _createButton = this.FindControl<Button>("CreateButton");
        _indexButton = this.FindControl<Button>("IndexButton");
        _searchButton = this.FindControl<Button>("SearchButton");
        _deleteButton = this.FindControl<Button>("DeleteButton");
        _backButton = this.FindControl<Button>("BackButton");

        if (_createButton != null) _createButton.Click += OnCreate;
        if (_indexButton != null) _indexButton.Click += OnIndex;
        if (_searchButton != null) _searchButton.Click += OnSearch;
        if (_deleteButton != null) _deleteButton.Click += OnDelete;
        _wikiRebuildButton = this.FindControl<Button>("WikiRebuildButton");
        if (_wikiRebuildButton != null) _wikiRebuildButton.Click += OnWikiRebuildClick;
        _wikiLintButton = this.FindControl<Button>("WikiLintButton");
        if (_wikiLintButton != null) _wikiLintButton.Click += OnWikiLintClick;
        if (_backButton != null) _backButton.Click += OnBack;
        if (_llmWikiListBox != null)
            _llmWikiListBox.SelectionChanged += OnSelectionChanged;

        _busyOverlay = this.FindControl<BusyOverlay>("BusyOverlay");
        _toolBar = this.FindControl<StackPanel>("ToolBar");
        _viewTitleText = this.FindControl<TextBlock>("ViewTitleText");
        _emptyHint = this.FindControl<TextBlock>("EmptyHint");
    }

    /// <summary>
    /// 显示忙碌遮罩并禁用窗口内容：索引/重建/lint 属长耗时任务，全程覆盖避免误判为卡死
    /// </summary>
    private void ShowBusy(string text)
    {
        SetContentEnabled(false);
        _busyOverlay?.Show(text);
    }

    /// <summary>
    /// 更新忙碌遮罩状态文本
    /// </summary>
    private void UpdateBusy(string text) => _busyOverlay?.UpdateStatus(text);

    /// <summary>
    /// 隐藏忙碌遮罩并恢复窗口内容
    /// </summary>
    private void HideBusy()
    {
        _busyOverlay?.Hide();
        SetContentEnabled(true);
    }

    private void SetContentEnabled(bool enabled)
    {
        if (_toolBar != null) _toolBar.IsEnabled = enabled;
        if (_llmWikiListBox != null) _llmWikiListBox.IsEnabled = enabled;
        if (_resultListBox != null) _resultListBox.IsEnabled = enabled;
    }

    /// <summary>
    /// 当前配置下单个可索引文件的大小上限（与 framework 索引语义保持一致）
    /// </summary>
    private long GetMaxFileSizeBytes() =>
        _services.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.Tools.Retrieval.MaxFileSizeKB * 1024L;

    private async void LoadLlmWikiWorkspaces()
    {
        await LoadLlmWikiWorkspacesAsync();
    }

    private async void OnCreate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var dlg = new NewWorkspaceDialog(WorkspaceManager.DefaultLlmWikiName);
            var ok = await dlg.ShowDialog<bool?>(this);
            if (ok != true) return;

            var ws = await _workspaceManager.CreateWorkspaceAsync(dlg.WorkspacePath!, dlg.WorkspaceName, WorkspaceTypes.LlmWiki);
            LoadLlmWikiWorkspaces();
            await Dialogs.ShowInfoAsync(this, $"已创建 LLM Wiki 知识库: {ws.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error("LlmWikiManageWindow.OnCreate 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnIndex(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_llmWikiListBox?.SelectedItem is not LlmWikiItem item) return;

        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            await _workspaceManager.EnsureAuthorizedAsync(ws);

            var dlg = new RenameDialog("") { DialogTitle = "索引文件匹配模式", Watermark = "留空索引全部文件" };
            var glob = await dlg.ShowDialog<string?>(this);
            if (glob == null) return;

            // 两段式提示：先"正在扫描目录"，预扫出文件数后更新为"共 N 个文件，正在生成向量"。
            // 预扫与嵌入都放后台线程，UI 线程只负责更新遮罩文案。
            var globPattern = glob == "" ? null : glob;
            ShowBusy(RetrievalIndexPlanner.IndexScanStatus(ws.RootPath));
            var fileCount = await Task.Run(() =>
                RetrievalIndexPlanner.CountIndexableFiles(ws.RootPath, globPattern, GetMaxFileSizeBytes(),
                    scan => Avalonia.Threading.Dispatcher.UIThread.Post(
                        () => UpdateBusy(RetrievalIndexPlanner.IndexScanningStatus(scan.CurrentDirectory, scan.IndexableFiles)))));

            if (fileCount == 0)
            {
                HideBusy();
                await Dialogs.ShowInfoAsync(this, RetrievalIndexPlanner.NoIndexableFilesText);
                return;
            }

            UpdateBusy(RetrievalIndexPlanner.IndexEmbedStatus(fileCount));
            var report = await _wiki.IndexFilesAsync(item.WorkspaceId, globPattern);
            HideBusy();
            var summary = $"索引完成：扫描 {report.ScannedFiles}，新增 {report.NewFiles}，更新 {report.UpdatedFiles}，跳过 {report.SkippedFiles}，切块 {report.TotalChunks}";
            if (report.Errors.Count > 0) summary += $"；{report.Errors.Count} 个文件失败（{string.Join("；", report.Errors.Take(3))}）";
            if (report.Warnings.Count > 0) summary += $"；{report.Warnings.Count} 个文件内容被截断";
            await Dialogs.ShowInfoAsync(this, summary);
            _ = RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            HideBusy();
            Logger.Error("LlmWikiManageWindow.OnIndex 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
        finally
        {
            HideBusy();
        }
    }

    private async void OnSearch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_llmWikiListBox?.SelectedItem is not LlmWikiItem item) return;

        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            var dlg = new RenameDialog("") { DialogTitle = "搜索查询", Watermark = "输入检索关键词" };
            var query = await dlg.ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(query)) return;

            // 与 /llmwiki -search 一致：wiki 优先，无相关内容时回落原始资料
            var results = await _wiki.SearchAsync(item.WorkspaceId, query!, WikiSearchScope.WikiThenRaw);
            ShowSearchResults(item, results);
            if (results.Count == 0)
                await Dialogs.ShowInfoAsync(this, "未找到相关文档");
        }
        catch (Exception ex)
        {
            Logger.Error("LlmWikiManageWindow.OnSearch 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnWikiRebuildClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_llmWikiListBox?.SelectedItem is not LlmWikiItem item) return;

        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            await _workspaceManager.EnsureAuthorizedAsync(ws);

            // 两段式提示：先统计 wiki 页面数，再提示正在重建向量索引；全程遮罩覆盖。
            ShowBusy(RetrievalIndexPlanner.WikiScanStatus);
            var pageCount = await Task.Run(() => RetrievalIndexPlanner.CountWikiPages(ws.RootPath));

            if (pageCount == 0)
            {
                HideBusy();
                await Dialogs.ShowInfoAsync(this, RetrievalIndexPlanner.NoWikiPagesText);
                return;
            }

            UpdateBusy(RetrievalIndexPlanner.WikiPagesStatus(pageCount, "正在重建向量索引"));
            var report = await _wiki.RebuildAsync(item.WorkspaceId);
            HideBusy();
            await Dialogs.ShowInfoAsync(this,
                $"重建完成（{item.Name}）：扫描 {report.ScannedFiles}，删除 {report.DeletedFiles}，切块 {report.TotalChunks}");
            _ = RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            HideBusy();
            Logger.Error("LlmWikiManageWindow.OnWikiRebuildClick 异常", ex);
            await Dialogs.ShowErrorAsync(this, $"重建失败: {ex.Message}");
        }
        finally
        {
            HideBusy();
        }
    }

    private async void OnWikiLintClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_llmWikiListBox?.SelectedItem is not LlmWikiItem item) return;

        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            await _workspaceManager.EnsureAuthorizedAsync(ws);

            // 两段式提示：先统计 wiki 页面数，再提示正在执行 lint；全程遮罩覆盖。
            ShowBusy(RetrievalIndexPlanner.WikiScanStatus);
            var pageCount = await Task.Run(() => RetrievalIndexPlanner.CountWikiPages(ws.RootPath));

            if (pageCount == 0)
            {
                HideBusy();
                await Dialogs.ShowInfoAsync(this, RetrievalIndexPlanner.NoWikiPagesText);
                return;
            }

            UpdateBusy(RetrievalIndexPlanner.WikiPagesStatus(pageCount, "正在执行 lint（孤儿页/死链/未收录）"));
            var report = await _wiki.LintAsync(item.WorkspaceId);
            HideBusy();
            var text = report.Findings.Count == 0
                ? $"（{item.Name}）共 {report.PageCount} 页，未发现问题。"
                : $"（{item.Name}）共 {report.PageCount} 页，{report.Findings.Count} 项：\n" +
                  string.Join("\n", report.Findings.Take(50).Select(f => $"[{f.Kind}] {f.Path}：{f.Detail}"));
            await Dialogs.ShowInfoAsync(this, text);
        }
        catch (Exception ex)
        {
            HideBusy();
            Logger.Error("LlmWikiManageWindow.OnWikiLintClick 异常", ex);
            await Dialogs.ShowErrorAsync(this, $"lint 失败: {ex.Message}");
        }
        finally
        {
            HideBusy();
        }
    }

    private void ShowSearchResults(LlmWikiItem item, IReadOnlyList<RetrievalResult> results)
    {
        if (_resultListBox == null || _llmWikiListBox == null || _backButton == null) return;

        if (_toolBar != null) _toolBar.IsVisible = false;
        _llmWikiListBox.IsVisible = false;
        _resultListBox.IsVisible = results.Count > 0;
        _backButton.IsVisible = true;
        if (_viewTitleText != null) _viewTitleText.Text = $"搜索结果 · {results.Count} 条（{item.Name}）";

        _resultListBox.ItemsSource = results.Select(r => new SearchResultItem
        {
            FilePath = r.FilePath,
            SymbolName = string.IsNullOrEmpty(r.SymbolName) ? "-" : r.SymbolName!,
            LineRange = $"L{r.StartLine}-{r.EndLine}",
            ScoreText = r.Score.ToString("F2"),
            Content = r.Content.Length > 200 ? r.Content[..200] + "…" : r.Content
        }).ToList();

        ShowEmptyHint(results.Count == 0, "未找到相关文档");
    }

    private void ShowEmptyHint(bool show, string text)
    {
        if (_emptyHint == null) return;
        if (show) _emptyHint.Text = text;
        _emptyHint.IsVisible = show;
    }

    private async void OnBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ShowListViewAsync();
    }

    /// <summary>
    /// 切回知识库列表视图：隐藏搜索结果与返回按钮、恢复工具栏，并刷新列表与统计。
    /// </summary>
    private async Task ShowListViewAsync()
    {
        if (_resultListBox == null || _llmWikiListBox == null || _backButton == null) return;
        if (_toolBar != null) _toolBar.IsVisible = true;
        _resultListBox.IsVisible = false;
        _llmWikiListBox.IsVisible = true;
        _backButton.IsVisible = false;
        if (_viewTitleText != null) _viewTitleText.Text = "知识库";
        await LoadLlmWikiWorkspacesAsync();
    }

    private async Task LoadLlmWikiWorkspacesAsync()
    {
        if (_llmWikiListBox == null) return;

        try
        {
            var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
            var llmWikiWorkspaces = workspaces.Where(w => w.Type == WorkspaceTypes.LlmWiki).ToList();
            var currentId = _workspaceManager.CurrentWorkspace?.WorkspaceId;
            var items = new ObservableCollection<LlmWikiItem>(llmWikiWorkspaces.Select(w => new LlmWikiItem
            {
                WorkspaceId = w.WorkspaceId,
                Name = w.Name,
                RootPath = w.RootPath,
                Status = w.IsAuthorized ? "已授权" : "未授权",
                IsCurrent = string.Equals(w.WorkspaceId, currentId, StringComparison.OrdinalIgnoreCase)
            }));
            _llmWikiListBox.ItemsSource = items;
            ShowEmptyHint(items.Count == 0, "暂无知识库，点击「+ 创建知识库」新建");
            await LoadStatsAsync(items);
        }
        catch (Exception ex)
        {
            Logger.Error("LlmWikiManageWindow.LoadLlmWikiWorkspaces 异常", ex);
            _llmWikiListBox.ItemsSource = new List<LlmWikiItem>
            {
                new() { Name = "加载失败", Status = ex.Message }
            };
        }
    }

    /// <summary>
    /// 后台逐个查询每个知识库的文件/切块数，回填列表项（列表先显示占位「…」，避免打开窗口卡顿）。
    /// </summary>
    private async Task LoadStatsAsync(IEnumerable<LlmWikiItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        foreach (var it in list)
        {
            try
            {
                var status = await _wiki.GetStatusAsync(it.WorkspaceId);
                it.CountText = status.IndexedFiles == 0
                    ? "未索引"
                    : $"已索引 {status.IndexedFiles} 文件 · {status.IndexedChunks} 切块";
            }
            catch (Exception ex)
            {
                Logger.Error($"LlmWiki 统计查询失败（{it.Name}）", ex);
                it.CountText = "统计失败";
            }
        }
    }

    /// <summary>
    /// 索引/重建后刷新当前列表的统计数字。
    /// </summary>
    private async Task RefreshStatsAsync()
    {
        if (_llmWikiListBox?.ItemsSource is ObservableCollection<LlmWikiItem> items)
            await LoadStatsAsync(items);
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_llmWikiListBox?.SelectedItem is not LlmWikiItem item) return;
        try
        {
            var ok = await Dialogs.ShowConfirmAsync(this, "删除 LLM Wiki 知识库",
                $"删除 '{item.Name}' 将同时删除其下所有会话和索引，确认？",
                okText: "删除", danger: true);
            if (!ok) return;

            // 统一走 Core 级联删除（会话软删 → LLM Wiki 索引清理 → 工作区逻辑删）。
            await _workspaceManager.DeleteWorkspaceAsync(item.WorkspaceId);

            await ShowListViewAsync();
            await Dialogs.ShowInfoAsync(this, "已删除 LLM Wiki 知识库");
        }
        catch (Exception ex)
        {
            Logger.Error("LlmWikiManageWindow.OnDelete 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = _llmWikiListBox?.SelectedItem != null;
        if (_indexButton != null) _indexButton.IsEnabled = hasSelection;
        if (_searchButton != null) _searchButton.IsEnabled = hasSelection;
        if (_deleteButton != null) _deleteButton.IsEnabled = hasSelection;
        if (_wikiRebuildButton != null) _wikiRebuildButton.IsEnabled = hasSelection;
        if (_wikiLintButton != null) _wikiLintButton.IsEnabled = hasSelection;
    }
}

/// <summary>
/// LLM Wiki 列表项
/// </summary>
public class LlmWikiItem : INotifyPropertyChanged
{
    private string _countText = "…";

    /// <summary>
    /// 该知识库对应的工作区标识。
    /// </summary>
    public string WorkspaceId { get; set; } = "";
    /// <summary>
    /// 知识库显示名称。
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// 知识库根目录的绝对路径。
    /// </summary>
    public string RootPath { get; set; } = "";
    /// <summary>
    /// 知识库状态文案（索引就绪度等），空串表示无附加状态。
    /// </summary>
    public string Status { get; set; } = "";
    /// <summary>
    /// 该知识库是否属于当前活动工作区。
    /// </summary>
    public bool IsCurrent { get; set; }
    /// <summary>
    /// 控制列表项「当前」标记的可见性，直接暴露 <c>IsCurrent</c> 供绑定使用。
    /// </summary>
    public bool IsCurrentVisible => IsCurrent;

    /// <summary>文件/切块统计文案，异步加载后回填。</summary>
    public string CountText
    {
        get => _countText;
        set
        {
            if (_countText == value) return;
            _countText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 搜索结果列表项
/// </summary>
public class SearchResultItem
{
    /// <summary>
    /// 命中切块所在文件的路径。
    /// </summary>
    public string FilePath { get; set; } = "";
    /// <summary>
    /// 命中切块所属的符号名（类、方法或页面标题）。
    /// </summary>
    public string SymbolName { get; set; } = "";
    /// <summary>
    /// 命中内容所在的行范围文案。
    /// </summary>
    public string LineRange { get; set; } = "";
    /// <summary>
    /// 相关性得分的显示文案。
    /// </summary>
    public string ScoreText { get; set; } = "";
    /// <summary>
    /// 命中切块的正文内容（用于结果列表预览）。
    /// </summary>
    public string Content { get; set; } = "";
}
