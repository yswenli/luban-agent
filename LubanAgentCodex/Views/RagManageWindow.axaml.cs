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
    private Button? _createButton;
    private Button? _indexButton;
    private Button? _searchButton;
    private Button? _deleteButton;

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
        _createButton = this.FindControl<Button>("CreateButton");
        _indexButton = this.FindControl<Button>("IndexButton");
        _searchButton = this.FindControl<Button>("SearchButton");
        _deleteButton = this.FindControl<Button>("DeleteButton");

        if (_createButton != null) _createButton.Click += OnCreate;
        if (_indexButton != null) _indexButton.Click += OnIndex;
        if (_searchButton != null) _searchButton.Click += OnSearch;
        if (_deleteButton != null) _deleteButton.Click += OnDelete;

        if (_ragListBox != null)
        {
            _ragListBox.SelectionChanged += OnSelectionChanged;
        }
    }

    private async void LoadRagWorkspaces()
    {
        if (_ragListBox == null) return;

        var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
        var ragWorkspaces = workspaces
            .Where(w => w.Type == "Rag")
            .Select(w => new RagItem
            {
                Name = w.Name,
                FileCount = "-",
                ChunkCount = "-",
                Status = "已创建"
            })
            .ToList();

        _ragListBox.ItemsSource = ragWorkspaces;
    }

    private void OnCreate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 创建 RAG 知识库
    }

    private void OnIndex(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 索引文件
    }

    private void OnSearch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 搜索
    }

    private void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 删除
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
        public string Name { get; set; } = "";
        public string FileCount { get; set; } = "";
        public string ChunkCount { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
