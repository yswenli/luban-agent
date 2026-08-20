/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： WorkManageWindow
*版本号： V1.0.0.0
*唯一标识：工作区管理窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：工作区管理窗口，用于管理工作区
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

/// <summary>
/// 工作区管理窗口
/// </summary>
public partial class WorkManageWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceManager _workspaceManager;
    private ListBox? _workspaceListBox;
    private Button? _addButton;
    private Button? _switchButton;
    private Button? _deleteButton;
    private Button? _authorizeButton;

    public WorkManageWindow(IServiceProvider services)
    {
        _services = services;
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        InitializeComponent();
        LoadWorkspaces();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _workspaceListBox = this.FindControl<ListBox>("WorkspaceListBox");
        _addButton = this.FindControl<Button>("AddButton");
        _switchButton = this.FindControl<Button>("SwitchButton");
        _deleteButton = this.FindControl<Button>("DeleteButton");
        _authorizeButton = this.FindControl<Button>("AuthorizeButton");

        if (_addButton != null) _addButton.Click += OnAdd;
        if (_switchButton != null) _switchButton.Click += OnSwitch;
        if (_deleteButton != null) _deleteButton.Click += OnDelete;
        if (_authorizeButton != null) _authorizeButton.Click += OnAuthorize;

        if (_workspaceListBox != null)
        {
            _workspaceListBox.SelectionChanged += OnSelectionChanged;
        }
    }

    private async void LoadWorkspaces()
    {
        if (_workspaceListBox == null) return;

        var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
        var currentId = _workspaceManager.CurrentWorkspace?.WorkspaceId;

        var items = workspaces.Select(w => new WorkspaceItem
        {
            WorkspaceId = w.WorkspaceId,
            TypeIcon = w.Type == "Rag" ? "📚" : "📁",
            Name = w.Name,
            RootPath = w.RootPath,
            Status = w.WorkspaceId == currentId ? "✓ 当前" : ""
        }).ToList();

        _workspaceListBox.ItemsSource = items;
    }

    private void OnAdd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 新建工作区
    }

    private async void OnSwitch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_workspaceListBox?.SelectedItem is WorkspaceItem item)
        {
            await _workspaceManager.SetCurrentAsync(item.WorkspaceId);
            LoadWorkspaces();
        }
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 删除工作区
    }

    private async void OnAuthorize(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 授权工作区
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = _workspaceListBox?.SelectedItem != null;
        if (_switchButton != null) _switchButton.IsEnabled = hasSelection;
        if (_deleteButton != null) _deleteButton.IsEnabled = hasSelection;
        if (_authorizeButton != null) _authorizeButton.IsEnabled = hasSelection;
    }

    private class WorkspaceItem
    {
        public string WorkspaceId { get; set; } = "";
        public string TypeIcon { get; set; } = "";
        public string Name { get; set; } = "";
        public string RootPath { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
