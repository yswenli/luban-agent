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
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LubanAgentCore.Entities;
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
        if (_ragListBox != null)
            _ragListBox.AddHandler(Button.ClickEvent, OnRagItemButtonClick);
    }

    /// <summary>
    /// RAG 知识库列表项内按钮（删除）的路由处理
    /// </summary>
    private void OnRagItemButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button btn && btn.Name == "DeleteRagBtn" && btn.DataContext is RagRowModel model)
        {
            _ = DeleteRagAsync(model);
        }
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
        _workspacePanel.Children.Add(BuildNewWorkspaceButton());

        var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
        var currentWorkspaceId = _workspaceManager.CurrentWorkspace?.WorkspaceId;

        foreach (var ws in workspaces)
        {
            // 知识库（RAG）不显示在工作区列表，仅显示于「RAG 知识库」分区
            if (ws.Type == "Rag") continue;

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
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
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

            // 新建会话按钮（常显于菜单之前）
            var newSessionBtn = new Button
            {
                Content = "➕",
                FontSize = 14,
                Padding = new Thickness(4, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brush.Parse("#4CAF50"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(newSessionBtn, "新建会话");
            newSessionBtn.Click += async (s, e) =>
            {
                await CreateSessionForWorkspaceAsync(ws);
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
                try
                {
                    var parentWindow = TopLevel.GetTopLevel(this) as Window;
                    if (parentWindow == null) return;

                    var dialog = new RenameDialog(ws.Name);
                    var result = await dialog.ShowDialog<string?>(parentWindow);
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
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"重命名工作区失败: {ex.Message}");
                }
            };

            skillItem.Click += (s, e) =>
            {
                try
                {
                    var win = new SkillManageWindow(_services!, ws);
                    win.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"打开技能管理失败: {ex.Message}");
                }
            };

            ruleItem.Click += (s, e) =>
            {
                try
                {
                    var win = new RuleManageWindow(_services!, ws);
                    win.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"打开规则管理失败: {ex.Message}");
                }
            };

            mcpItem.Click += (s, e) =>
            {
                try
                {
                    var win = new MCPManageWindow(_services!, ws);
                    win.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"打开MCP服务管理失败: {ex.Message}");
                }
            };

            deleteItem.Click += async (s, e) =>
            {
                try
                {
                    var parentWindow = TopLevel.GetTopLevel(this) as Window;
                    if (parentWindow == null) return;

                    var result = await Dialogs.ShowConfirmAsync(parentWindow, "确认删除",
                        $"确定要删除工作区 \"{ws.Name}\" 吗？",
                        "删除后将无法恢复，相关的会话和数据也会被删除。",
                        "确定删除", danger: true);
                    if (result == true)
                    {
                        var repo = _services!.GetRequiredService<WorkspaceRepository>();
                        await repo.DeleteAsync(w => w.WorkspaceId == ws.WorkspaceId);
                        LoadWorkspaces();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"删除工作区失败: {ex.Message}");
                }
            };

            flyout.Items.Add(renameItem);
            flyout.Items.Add(skillItem);
            flyout.Items.Add(ruleItem);
            flyout.Items.Add(mcpItem);
            flyout.Items.Add(deleteItem);
            wsMenuBtn.Flyout = flyout;

            Grid.SetColumn(wsIcon, 0);
            Grid.SetColumn(wsName, 1);
            Grid.SetColumn(newSessionBtn, 2);
            Grid.SetColumn(wsMenuBtn, 3);
            wsGrid.Children.Add(wsIcon);
            wsGrid.Children.Add(wsName);
            wsGrid.Children.Add(newSessionBtn);
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

                // 会话右键菜单
                var sessFlyout = new MenuFlyout();
                var deleteSessItem = new Avalonia.Controls.MenuItem { Header = "🗑️ 删除会话" };

                var sessionCopy = session; // 捕获副本
                deleteSessItem.Click += async (s, e) =>
                {
                    try
                    {
                        var parentWindow = TopLevel.GetTopLevel(this) as Window;
                        if (parentWindow == null) return;

                        var result = await Dialogs.ShowConfirmAsync(parentWindow, "确认删除",
                            $"确定要删除会话 \"{sessionCopy.Title ?? "新会话"}\" 吗？",
                            null, "确定删除", danger: true);
                        if (result == true)
                        {
                            var sessionRepo = _services!.GetRequiredService<SessionRepository>();
                            await sessionRepo.SoftDeleteAsync(sessionCopy.SessionId);
                            LoadWorkspaces(); // 刷新列表
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"删除会话失败: {ex.Message}");
                    }
                };

                sessFlyout.Items.Add(deleteSessItem);
                sessRow.ContextFlyout = sessFlyout;

                sessRow.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint(sessRow).Properties.IsRightButtonPressed)
                        return; // 右键不触发选择
                    if (e.ClickCount == 2)
                    {
                        _ = RenameSessionAsync(sessionCopy); // 双击重命名会话
                        return;
                    }
                    SessionSelected?.Invoke(this, new SessionItem
                    {
                        SessionId = sessionCopy.SessionId,
                        Title = sessionCopy.Title ?? "新会话",
                    });
                };

                _workspacePanel.Children.Add(sessRow);
            }
        }
    }

    /// <summary>
    /// 构建「新建工作区」按钮（列表顶部常驻，渐变 + hover 高亮）
    /// </summary>
    private Button BuildNewWorkspaceButton()
    {
        var gradientNormal = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#2563EB"), 0),
                new GradientStop(Color.Parse("#06B6D4"), 1),
            }
        };
        var gradientHover = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#3B82F6"), 0),
                new GradientStop(Color.Parse("#22D3EE"), 1),
            }
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        content.Children.Add(new TextBlock { Text = "➕", FontSize = 14, Foreground = Brush.Parse("#67E8F9") });
        content.Children.Add(new TextBlock { Text = "新建工作区", FontSize = 13, FontWeight = FontWeight.SemiBold });

        var btn = new Button
        {
            Margin = new Thickness(12, 8, 12, 8),
            Height = 38,
            CornerRadius = new CornerRadius(9),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            Background = gradientNormal,
            BorderThickness = new Thickness(0),
            Content = content,
        };

        // hover 高亮
        btn.PointerEntered += (s, e) => btn.Background = gradientHover;
        btn.PointerExited += (s, e) => btn.Background = gradientNormal;

        btn.Click += async (s, e) => await CreateNewWorkspaceAsync();
        return btn;
    }

    /// <summary>
    /// 弹出新建工作区对话框并创建工作区
    /// </summary>
    private async Task CreateNewWorkspaceAsync()
    {
        if (_workspaceManager == null) return;
        try
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null) return;

            var dialog = new NewWorkspaceDialog();
            var ok = await dialog.ShowDialog<bool?>(owner);
            if (ok != true) return;

            var ws = await _workspaceManager.CreateWorkspaceAsync(dialog.WorkspacePath!, dialog.WorkspaceName);
            LoadWorkspaces();
            WorkspaceSelected?.Invoke(this, ws);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"创建工作区失败: {ex.Message}");
            await ShowErrorAsync($"创建工作区失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 内联错误提示弹窗
    /// </summary>
    private async Task ShowErrorAsync(string msg)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        await Dialogs.ShowErrorAsync(owner, msg);
    }

    /// <summary>
    /// 为指定工作区创建新会话：入库后刷新侧边栏并切换到该会话
    /// </summary>
    private async Task CreateSessionForWorkspaceAsync(WorkspaceInfo ws)
    {
        if (_sessionRepo == null) return;
        try
        {
            var newSession = new DbSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                UserId = "default",
                Title = "新对话",
                CreateTime = DateTime.Now,
                UpdateTime = DateTime.Now,
                IsDelete = false,
                WorkspaceId = ws.WorkspaceId,
            };
            await _sessionRepo.InsertAsync(newSession);
            LoadWorkspaces();
            SessionSelected?.Invoke(this, new SessionItem
            {
                SessionId = newSession.SessionId,
                Title = newSession.Title,
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"创建会话失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 双击会话项重命名
    /// </summary>
    private async Task RenameSessionAsync(DbSession session)
    {
        if (_sessionRepo == null) return;
        try
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow == null) return;

            var dialog = new RenameDialog(session.Title ?? "新会话");
            var result = await dialog.ShowDialog<string?>(parentWindow);
            if (!string.IsNullOrWhiteSpace(result) && result != session.Title)
            {
                await _sessionRepo.UpdateTitleAsync(session.SessionId, result);
                LoadWorkspaces();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"重命名会话失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载 RAG 知识库列表
    /// 规则：知识库只能初始化一个；已存在时「初始化知识库」按钮隐藏，需先删除再初始化。
    /// </summary>
    private void LoadRagItems()
    {
        if (_ragListBox == null || _services == null) return;

        var workspaceManager = _services.GetRequiredService<IWorkspaceManager>();
        var workspaces = workspaceManager.GetUserWorkspacesAsync().GetAwaiter().GetResult();
        var ragWorkspaces = workspaces.Where(w => w.Type == "Rag").ToList();

        // 统一显示为「知识库」，不在工作区列表中重复展示真实名称
        _ragListBox.ItemsSource = ragWorkspaces
            .Select(w => new RagRowModel { WorkspaceId = w.WorkspaceId, DisplayName = "知识库" })
            .ToList();
        _ragListBox.IsVisible = ragWorkspaces.Count > 0;

        // 移除旧的初始化按钮
        if (_ragListBox.Parent is StackPanel parent)
        {
            var existingBtn = parent.Children.OfType<Button>().FirstOrDefault(b => b.Name == "InitRagButton");
            if (existingBtn != null) parent.Children.Remove(existingBtn);

            // 仅当尚未初始化知识库时才显示「初始化知识库」按钮
            if (ragWorkspaces.Count == 0)
            {
                var initBtn = new Button
                {
                    Name = "InitRagButton",
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = "➕", Foreground = Brush.Parse("#AB47BC") },
                            new TextBlock { Text = "初始化知识库", Foreground = Brush.Parse("#CCCCCC") },
                        }
                    },
                    FontSize = 12,
                    Background = Brushes.Transparent,
                    BorderBrush = Brush.Parse("#3F3F46"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4),
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
    /// 删除知识库：先确认，再删除工作区及其会话；删除后「初始化知识库」按钮恢复显示
    /// </summary>
    private async Task DeleteRagAsync(RagRowModel model)
    {
        try
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow == null) return;

            var confirmed = await Dialogs.ShowConfirmAsync(parentWindow, "删除知识库",
                "确定要删除知识库吗？",
                "删除后将无法恢复，相关的会话和数据也会被删除。",
                "确定删除", danger: true);
            if (confirmed != true) return;

            var services = _services!;
            var wsRepo = services.GetRequiredService<WorkspaceRepository>();
            await wsRepo.DeleteAsync(w => w.WorkspaceId == model.WorkspaceId);

            // 同时清理其下的会话
            var sessionRepo = services.GetRequiredService<SessionRepository>();
            var sessions = await sessionRepo.GetByWorkspaceAsync(model.WorkspaceId);
            foreach (var s in sessions)
                await sessionRepo.SoftDeleteAsync(s.SessionId);

            LoadRagItems();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"删除知识库失败: {ex.Message}");
            await ShowErrorAsync($"删除知识库失败：{ex.Message}");
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

/// <summary>
/// RAG 知识库列表项（侧边栏「RAG 知识库」分区）
/// </summary>
public class RagRowModel
{
    public string WorkspaceId { get; set; } = "";
    public string DisplayName { get; set; } = "知识库";
}

