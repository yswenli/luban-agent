/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： RagManageWindow
*版本号： V1.0.0.0
*唯一标识：RAG 知识库管理窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：RAG 知识库管理窗口，用于管理 RAG 知识库
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LuBan.AIAgent.Retrieval;
using LubanAgentCore.Repositories;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

/// <summary>
/// RAG 知识库管理窗口
/// </summary>
public partial class RagManageWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceManager _workspaceManager;
    private ListBox? _ragListBox;
    private ListBox? _resultListBox;
    private Button? _createButton;
    private Button? _indexButton;
    private Button? _searchButton;
    private Button? _deleteButton;
    private Button? _backButton;

    public RagManageWindow(IServiceProvider services)
    {
        _services = services;
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        InitializeComponent();
        LoadRagWorkspaces();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _ragListBox = this.FindControl<ListBox>("RagListBox");
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
        if (_backButton != null) _backButton.Click += OnBack;
        if (_ragListBox != null)
            _ragListBox.SelectionChanged += OnSelectionChanged;
    }

    private async void LoadRagWorkspaces()
    {
        await LoadRagWorkspacesAsync();
    }

    private async void OnCreate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var dlg = new NewWorkspaceDialog();
            var ok = await dlg.ShowDialog<bool?>(this);
            if (ok != true) return;

            var ws = await _workspaceManager.CreateWorkspaceAsync(dlg.WorkspacePath!, dlg.WorkspaceName, "Rag");
            LoadRagWorkspaces();
            await Dialogs.ShowInfoAsync(this, $"已创建 RAG 知识库: {ws.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.OnCreate 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnIndex(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_ragListBox?.SelectedItem is not RagItem item) return;
        var retrieval = _services.GetService<IRetrievalService>();
        if (retrieval == null)
        {
            await Dialogs.ShowInfoAsync(this, "嵌入模型未就绪，无法索引");
            return;
        }

        var previous = _workspaceManager.CurrentWorkspace;
        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            await _workspaceManager.EnsureAuthorizedAsync(ws);
            await _workspaceManager.SetCurrentAsync(item.WorkspaceId);

            var dlg = new RenameDialog("") { DialogTitle = "索引文件匹配模式", Watermark = "留空索引全部文件" };
            var glob = await dlg.ShowDialog<string?>(this);
            if (glob == null) return;

            var report = await retrieval.IndexDirectoryAsync(ws.RootPath, glob == "" ? null : glob, force: false);
            await Dialogs.ShowInfoAsync(this,
                $"索引完成：扫描 {report.ScannedFiles}，新增 {report.NewFiles}，更新 {report.UpdatedFiles}，跳过 {report.SkippedFiles}，切块 {report.TotalChunks}");
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.OnIndex 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
        finally
        {
            if (previous != null) await _workspaceManager.SetCurrentAsync(previous.WorkspaceId);
        }
    }

    private async void OnSearch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_ragListBox?.SelectedItem is not RagItem item) return;
        var retrieval = _services.GetService<IRetrievalService>();
        if (retrieval == null)
        {
            await Dialogs.ShowInfoAsync(this, "嵌入模型未就绪，无法搜索");
            return;
        }

        var previous = _workspaceManager.CurrentWorkspace;
        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            await _workspaceManager.SetCurrentAsync(item.WorkspaceId);

            var dlg = new RenameDialog("") { DialogTitle = "搜索查询", Watermark = "输入检索关键词" };
            var query = await dlg.ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(query)) return;

            var results = await retrieval.SearchAsync(query!, topK: 5);
            ShowSearchResults(results);
            if (results.Count == 0)
                await Dialogs.ShowInfoAsync(this, "未找到相关文档");
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.OnSearch 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
        finally
        {
            if (previous != null) await _workspaceManager.SetCurrentAsync(previous.WorkspaceId);
        }
    }

    private void ShowSearchResults(IReadOnlyList<RetrievalResult> results)
    {
        if (_resultListBox == null || _ragListBox == null || _backButton == null) return;
        _ragListBox.IsVisible = false;
        _resultListBox.IsVisible = true;
        _backButton.IsVisible = true;
        _resultListBox.ItemsSource = results.Select(r => new SearchResultItem
        {
            FilePath = r.FilePath,
            SymbolName = string.IsNullOrEmpty(r.SymbolName) ? "-" : r.SymbolName!,
            LineRange = $"L{r.StartLine}-{r.EndLine}",
            Content = r.Content.Length > 200 ? r.Content[..200] + "…" : r.Content
        }).ToList();
    }

    private async void OnBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_resultListBox == null || _ragListBox == null || _backButton == null) return;
        _resultListBox.IsVisible = false;
        _ragListBox.IsVisible = true;
        _backButton.IsVisible = false;
        await LoadRagWorkspacesAsync();
    }

    private async Task LoadRagWorkspacesAsync()
    {
        if (_ragListBox == null) return;

        try
        {
            var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
            var ragWorkspaces = workspaces.Where(w => w.Type == "Rag").ToList();
            _ragListBox.ItemsSource = ragWorkspaces.Select(w => new RagItem
            {
                WorkspaceId = w.WorkspaceId,
                Name = w.Name,
                RootPath = w.RootPath,
                FileCount = "-",
                ChunkCount = "-",
                Status = "已创建"
            }).ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.LoadRagWorkspaces 异常", ex);
            _ragListBox.ItemsSource = new List<RagItem>
            {
                new() { Name = "加载失败", Status = ex.Message }
            };
        }
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_ragListBox?.SelectedItem is not RagItem item) return;
        try
        {
            var ok = await Dialogs.ShowConfirmAsync(this, "删除 RAG 知识库",
                $"删除 '{item.Name}' 将同时删除其下所有会话和索引，确认？",
                okText: "删除", danger: true);
            if (!ok) return;

            var sessionRepo = _services.GetRequiredService<SessionRepository>();
            var ragFileRepo = new RagFileRepository();
            var ragChunkRepo = new RagChunkRepository();
            var wsRepo = _services.GetRequiredService<WorkspaceRepository>();

            await sessionRepo.SoftDeleteByWorkspaceAsync(item.WorkspaceId);
            await ragFileRepo.DeleteByWorkspaceAsync(item.WorkspaceId);
            await ragChunkRepo.DeleteByWorkspaceAsync(item.WorkspaceId);
            await wsRepo.LogicDeleteAsync(w => w.WorkspaceId == item.WorkspaceId);

            LoadRagWorkspaces();
            await Dialogs.ShowInfoAsync(this, "已删除 RAG 知识库");
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.OnDelete 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = _ragListBox?.SelectedItem != null;
        if (_indexButton != null) _indexButton.IsEnabled = hasSelection;
        if (_searchButton != null) _searchButton.IsEnabled = hasSelection;
        if (_deleteButton != null) _deleteButton.IsEnabled = hasSelection;
    }

    private class RagItem
    {
        public string WorkspaceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string RootPath { get; set; } = "";
        public string FileCount { get; set; } = "";
        public string ChunkCount { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private class SearchResultItem
    {
        public string FilePath { get; set; } = "";
        public string SymbolName { get; set; } = "";
        public string LineRange { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
