using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace LubanAgentCodex.Views;

public partial class WorkspacePickerWindow : Window
{
    private ListBox? _workspaceListBox;
    private Button? _openFolderButton;
    private Button? _selectButton;
    private Button? _cancelButton;
    private IWorkspaceManager? _workspaceManager;

    public ObservableCollection<WorkspaceItem> Workspaces { get; } = new();
    public WorkspaceInfo? SelectedWorkspace { get; private set; }

    public WorkspacePickerWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _workspaceListBox = this.FindControl<ListBox>("WorkspaceListBox");
        _openFolderButton = this.FindControl<Button>("OpenFolderButton");
        _selectButton = this.FindControl<Button>("SelectButton");
        _cancelButton = this.FindControl<Button>("CancelButton");

        if (_workspaceListBox != null)
        {
            _workspaceListBox.ItemsSource = Workspaces;
            _workspaceListBox.SelectionChanged += OnWorkspaceSelected;
        }

        if (_openFolderButton != null)
            _openFolderButton.Click += OnOpenFolderClicked;

        if (_selectButton != null)
            _selectButton.Click += OnSelectClicked;

        if (_cancelButton != null)
            _cancelButton.Click += (s, e) => Close(null);
    }

    public void SetServiceProvider(IServiceProvider services)
    {
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        LoadWorkspaces();
    }

    private async void LoadWorkspaces()
    {
        if (_workspaceManager == null) return;

        var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
        Workspaces.Clear();

        foreach (var ws in workspaces.OrderByDescending(w => w.LastActiveAt))
        {
            Workspaces.Add(new WorkspaceItem
            {
                WorkspaceId = ws.WorkspaceId,
                Name = ws.Name,
                RootPath = ws.RootPath,
                LastActiveText = ws.LastActiveAt?.ToString("yyyy-MM-dd HH:mm") ?? "从未使用",
                Workspace = ws
            });
        }
    }

    private void OnWorkspaceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_selectButton != null)
            _selectButton.IsEnabled = e.AddedItems.Count > 0;
    }

    private async void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择工作区文件夹",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var result = folders[0].Path.LocalPath;
            if (_workspaceManager != null)
            {
                // 检查工作区是否已存在
                var allWorkspaces = await _workspaceManager.GetUserWorkspacesAsync();
                var existing = allWorkspaces.FirstOrDefault(w => 
                    string.Equals(w.RootPath, result, StringComparison.OrdinalIgnoreCase));
                
                if (existing != null)
                {
                    SelectedWorkspace = existing;
                    Close(existing);
                }
                else
                {
                    // 创建新工作区
                    var ws = await _workspaceManager.CreateWorkspaceAsync(result);
                    SelectedWorkspace = ws;
                    Close(ws);
                }
            }
        }
    }

    private void OnSelectClicked(object? sender, RoutedEventArgs e)
    {
        if (_workspaceListBox?.SelectedItem is WorkspaceItem item)
        {
            SelectedWorkspace = item.Workspace;
            Close(item.Workspace);
        }
    }
}

public class WorkspaceItem
{
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string LastActiveText { get; set; } = "";
    public WorkspaceInfo Workspace { get; set; } = null!;

    /// <summary>
    /// 显示名称：如果 Name 为空则用路径最后目录名
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            if (string.IsNullOrWhiteSpace(RootPath)) return "未命名工作区";
            var trimmed = RootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? "未命名工作区" : name;
        }
    }
}
