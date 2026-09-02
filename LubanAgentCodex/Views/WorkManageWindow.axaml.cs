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
using LubanAgentCore.Repositories;
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
            _workspaceListBox.SelectionChanged += OnSelectionChanged;
    }

    private async void LoadWorkspaces()
    {
        if (_workspaceListBox == null) return;

        var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
        var currentId = _workspaceManager.CurrentWorkspace?.WorkspaceId;

        _workspaceListBox.ItemsSource = workspaces.Select(w => new WorkspaceItem
        {
            WorkspaceId = w.WorkspaceId,
            TypeIcon = w.Type == "Rag" ? "📚" : "📁",
            Name = w.Name,
            RootPath = w.RootPath,
            Status = w.WorkspaceId == currentId ? "✓ 当前" : "",
            IsAuthorized = w.IsAuthorized ? "✓" : "✗"
        }).ToList();
    }

    private async void OnAdd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var dlg = new NewWorkspaceDialog();
            var ok = await dlg.ShowDialog<bool?>(this);
            if (ok != true) return;

            var ws = await _workspaceManager.CreateWorkspaceAsync(dlg.WorkspacePath!, dlg.WorkspaceName, "Normal");
            LoadWorkspaces();
            await Dialogs.ShowInfoAsync(this, $"已创建工作区: {ws.Name}，可点切换使用");
        }
        catch (Exception ex)
        {
            Logger.Error("WorkManageWindow.OnAdd 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnSwitch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_workspaceListBox?.SelectedItem is not WorkspaceItem item) return;
        try
        {
            await _workspaceManager.SetCurrentAsync(item.WorkspaceId);
            LoadWorkspaces();
        }
        catch (Exception ex)
        {
            Logger.Error("WorkManageWindow.OnSwitch 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_workspaceListBox?.SelectedItem is not WorkspaceItem item) return;
        try
        {
            var ok = await Dialogs.ShowConfirmAsync(this, "删除工作区",
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

            if (_workspaceManager.CurrentWorkspace?.WorkspaceId == item.WorkspaceId)
                await Dialogs.ShowInfoAsync(this, "当前工作区已删除，请切换到其他工作区");

            LoadWorkspaces();
            await Dialogs.ShowInfoAsync(this, "已删除工作区");
        }
        catch (Exception ex)
        {
            Logger.Error("WorkManageWindow.OnDelete 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnAuthorize(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_workspaceListBox?.SelectedItem is not WorkspaceItem item) return;
        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            if (ws.IsAuthorized)
            {
                await Dialogs.ShowInfoAsync(this, "工作区已授权");
                return;
            }

            var switched = false;
            if (_workspaceManager.CurrentWorkspace?.WorkspaceId != item.WorkspaceId)
            {
                await _workspaceManager.SetCurrentAsync(item.WorkspaceId);
                switched = true;
            }

            var ok = await _workspaceManager.EnsureAuthorizedAsync(ws);
            LoadWorkspaces();
            if (ok)
                await Dialogs.ShowInfoAsync(this, switched ? "已授权并切换为该工作区" : "工作区已授权");
            else
                await Dialogs.ShowErrorAsync(this, "授权失败");
        }
        catch (Exception ex)
        {
            Logger.Error("WorkManageWindow.OnAuthorize 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
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
        public string IsAuthorized { get; set; } = "";
    }
}
