/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： Sidebar
*版本号： V1.0.0.0
*唯一标识：左侧边栏控件
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：左侧边栏，包含工作区列表、会话树、RAG 知识库和项目信息
*
*****************************************************************************/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LubanAgentCore.Repositories;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 左侧边栏控件
/// </summary>
public partial class Sidebar : UserControl
{
    private StackPanel? _workspacePanel;
    private ListBox? _ragListBox;
    private IWorkspaceManager? _workspaceManager;
    private SessionRepository? _sessionRepo;
    private IServiceProvider? _services;

    /// <summary>
    /// 工作区选择事件
    /// </summary>
    public event EventHandler<WorkspaceInfo>? WorkspaceSelected;

    /// <summary>
    /// 会话选择事件
    /// </summary>
    public event EventHandler<SessionItem>? SessionSelected;

    /// <summary>
    /// RAG 初始化请求事件（参数：选择的工作区）
    /// </summary>
    public event EventHandler? RagInitRequested;

    public Sidebar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _workspacePanel = this.FindControl<StackPanel>("WorkspacePanel");
        _ragListBox = this.FindControl<ListBox>("RagListBox");
    }

    /// <summary>
    /// 设置服务提供者并加载数据
    /// </summary>
    public void SetServiceProvider(IServiceProvider services)
    {
        _services = services;
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        _sessionRepo = services.GetRequiredService<SessionRepository>();
        LoadWorkspaces();
        LoadRagItems();
    }

    /// <summary>
    /// 从路径获取显示名称（处理末尾分隔符）
    /// </summary>
    private static string GetDisplayName(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return "未命名工作区";
        var trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? "未命名工作区" : name;
    }

    /// <summary>
    /// 加载工作区列表
    /// </summary>
    private async void LoadWorkspaces()
    {
        if (_workspacePanel == null || _workspaceManager == null || _sessionRepo == null)
            return;

        _workspacePanel.Children.Clear();
        var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
        var currentWorkspaceId = _workspaceManager.CurrentWorkspace?.WorkspaceId;

        foreach (var ws in workspaces)
        {
            var isActive = ws.WorkspaceId == currentWorkspaceId;

            // 工作区行
            var wsRow = new Border
            {
                Padding = new Thickness(8, 4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = isActive ? Brush.Parse("#2D2D30") : Brushes.Transparent,
                BorderThickness = isActive ? new Thickness(2, 0, 0, 0) : new Thickness(0),
                BorderBrush = isActive ? Brush.Parse("#007ACC") : null,
            };

            var wsGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
            };

            var wsIcon = new TextBlock { Text = "📁", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var wsName = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(ws.Name) ? GetDisplayName(ws.RootPath) : ws.Name,
                FontSize = 13,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var wsMenuBtn = new Button
            {
                Content = "⋯",
                FontSize = 14,
                Padding = new Thickness(4, 2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brush.Parse("#858585"),
                IsVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // 创建 Flyout 菜单
            var flyout = new MenuFlyout();
            var renameItem = new Avalonia.Controls.MenuItem { Header = "✏️ 重命名工作区" };
            var skillItem = new Avalonia.Controls.MenuItem { Header = "⚡ 技能管理" };
            var ruleItem = new Avalonia.Controls.MenuItem { Header = "📏 规则管理" };
            var mcpItem = new Avalonia.Controls.MenuItem { Header = "🔌 MCP 服务" };
            var deleteItem = new Avalonia.Controls.MenuItem { Header = "🗑️ 删除工作区" };

            renameItem.Click += async (s, e) =>
            {
                var dialog = new RenameDialog(ws.Name);
                var result = await dialog.ShowDialog<string?>(this.VisualRoot as Window);
                if (!string.IsNullOrWhiteSpace(result) && result != ws.Name)
                {
                    var repo = _services!.GetRequiredService<WorkspaceRepository>();
                    var dbWs = await repo.GetByIdAsync(ws.WorkspaceId);
                    if (dbWs != null)
                    {
                        dbWs.Name = result;
                        await repo.UpdateAsync(dbWs);
                        ws.Name = result;
                        LoadWorkspaces();
                    }
                }
            };

            skillItem.Click += (s, e) =>
            {
                var win = new SkillManageWindow(_services!, ws);
                win.Show();
            };

            ruleItem.Click += (s, e) =>
            {
                var win = new RuleManageWindow(_services!, ws);
                win.Show();
            };

            mcpItem.Click += (s, e) =>
            {
                var win = new MCPManageWindow(_services!, ws);
                win.Show();
            };

            deleteItem.Click += async (s, e) =>
            {
                var repo = _services!.GetRequiredService<WorkspaceRepository>();
                await repo.DeleteAsync(w => w.WorkspaceId == ws.WorkspaceId);
                LoadWorkspaces();
            };

            flyout.Items.Add(renameItem);
            flyout.Items.Add(skillItem);
            flyout.Items.Add(ruleItem);
            flyout.Items.Add(mcpItem);
            flyout.Items.Add(deleteItem);
            wsMenuBtn.Flyout = flyout;

            Grid.SetColumn(wsIcon, 0);
            Grid.SetColumn(wsName, 1);
            Grid.SetColumn(wsMenuBtn, 2);
            wsGrid.Children.Add(wsIcon);
            wsGrid.Children.Add(wsName);
            wsGrid.Children.Add(wsMenuBtn);
            wsRow.Child = wsGrid;

            // Hover 显示菜单按钮
            wsRow.PointerEntered += (s, e) => wsMenuBtn.IsVisible = true;
            wsRow.PointerExited += (s, e) => wsMenuBtn.IsVisible = false;

            // 点击工作区切换
            wsRow.PointerPressed += (s, e) =>
            {
                if (e.Source is Button) return;
                WorkspaceSelected?.Invoke(this, ws);
            };

            _workspacePanel.Children.Add(wsRow);

            // 加载该工作区的会话
            var sessions = await _sessionRepo.GetByWorkspaceAsync(ws.WorkspaceId);
            foreach (var session in sessions.OrderByDescending(s => s.UpdateTime))
            {
                var sessRow = new Border
                {
                    Padding = new Thickness(6, 4),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    CornerRadius = new CornerRadius(4),
                };

                var sessStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 0, 0, 0) };
                var sessIcon = new TextBlock { Text = "💬", Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
                var sessName = new TextBlock
                {
                    Text = session.Title ?? "新会话",
                    FontSize = 12,
                    Foreground = Brush.Parse("#CCCCCC"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 200,
                };

                sessStack.Children.Add(sessIcon);
                sessStack.Children.Add(sessName);
                sessRow.Child = sessStack;

                sessRow.PointerPressed += (s, e) =>
                {
                    SessionSelected?.Invoke(this, new SessionItem
                    {
                        SessionId = session.SessionId,
                        Title = session.Title ?? "新会话",
                    });
                };

                _workspacePanel.Children.Add(sessRow);
            }
        }
    }

    /// <summary>
    /// 加载 RAG 知识库列表
    /// </summary>
    private void LoadRagItems()
    {
        if (_ragListBox == null || _services == null) return;

        var workspaceManager = _services.GetRequiredService<IWorkspaceManager>();
        var workspaces = workspaceManager.GetUserWorkspacesAsync().GetAwaiter().GetResult();
        var ragWorkspaces = workspaces.Where(w => w.Type == "Rag").ToList();

        _ragListBox.ItemsSource = ragWorkspaces.Select(w => w.Name).ToList();

        // 添加"初始化 RAG"按钮
        if (_ragListBox.Parent is StackPanel parent)
        {
            // 移除旧的初始化按钮
            var existingBtn = parent.Children.OfType<Button>().FirstOrDefault(b => b.Name == "InitRagButton");
            if (existingBtn != null) parent.Children.Remove(existingBtn);

            var initBtn = new Button
            {
                Name = "InitRagButton",
                Content = "➕ 初始化知识库",
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderBrush = Brush.Parse("#3F3F46"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4),
                Foreground = Brush.Parse("#CCCCCC"),
                Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(32, 8, 16, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            initBtn.Click += async (s, e) =>
            {
                RagInitRequested?.Invoke(this, EventArgs.Empty);
            };

            parent.Children.Add(initBtn);
        }
    }
}

/// <summary>
/// 会话列表项（简化版）
/// </summary>
public class SessionItem
{
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "";
}

