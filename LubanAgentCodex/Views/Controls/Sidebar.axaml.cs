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

    /// <summary>
    /// 工作区设置中心打开请求（由底部「⚙ 设置」按钮触发）
    /// </summary>
    public event EventHandler? SettingsRequested;

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
        {
            _ragListBox.AddHandler(Button.ClickEvent, OnRagItemButtonClick);
            // ListBoxItem 内部会标记 PointerPressed 已处理，需用 handledEventsToo 才能收到冒泡事件
            _ragListBox.AddHandler(InputElement.PointerPressedEvent, OnRagListBoxPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        // 底部「⚙ 设置」按钮：打开工作区设置中心（技能 / 规则 / MCP）
        if (this.FindControl<Button>("SettingsBtn") is Button settingsBtn)
            settingsBtn.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
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
    /// 知识库列表指针事件（对齐工作区「会话列表」）：单击 / 双击均切换到该知识库的会话，
    /// 双击显式触发切换，单击同样可快速打开；删除按钮与右键不触发。
    /// </summary>
    private void OnRagListBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 点击删除按钮不触发切换
        if (IsFromDeleteButton(e.Source)) return;

        // 右键不触发切换
        if (e.GetCurrentPoint(_ragListBox).Properties.IsRightButtonPressed) return;

        // 从事件源向上回溯，找到对应的知识库列表项模型
        var model = FindRagRowModelFromSource(e.Source);
        if (model?.Workspace is null) return;

        // 单击或双击都切换到该知识库的会话（双击交互对齐「工作区会话列表」）
        WorkspaceSelected?.Invoke(this, model.Workspace);
    }

    /// <summary>
    /// 从指针事件源向上回溯，找到 DataContext 为 RagRowModel 的控件
    /// </summary>
    private static RagRowModel? FindRagRowModelFromSource(object? source)
    {
        var ctrl = source as Control;
        while (ctrl != null)
        {
            if (ctrl.DataContext is RagRowModel model) return model;
            ctrl = ctrl.Parent as Control;
        }
        return null;
    }

    /// <summary>
    /// 判断指针事件源是否来自「删除知识库」按钮（含其子元素）
    /// </summary>
    private static bool IsFromDeleteButton(object? source)
    {
        var ctrl = source as Control;
        while (ctrl != null)
        {
            if (ctrl.Name == "DeleteRagBtn") return true;
            ctrl = ctrl.Parent as Control;
        }
        return false;
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
            // 新建会话按钮（常显）
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

            // 删除工作区按钮（替代原「⋯」菜单中的删除项）
            var deleteBtn = new Button
            {
                Content = "−",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(4, 2),
                Margin = new Thickness(0, 0, 2, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brush.Parse("#858585"),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(deleteBtn, "删除工作区");
            deleteBtn.Click += async (s, e) =>
            {
                try
                {
                    var owner = TopLevel.GetTopLevel(this) as Window;
                    if (owner == null) return;

                    var display = string.IsNullOrWhiteSpace(ws.Name) ? GetDisplayName(ws.RootPath) : ws.Name;
                    var ok = await Dialogs.ShowConfirmAsync(owner, "确认删除",
                        $"确定要删除工作区 \"{display}\" 吗？",
                        "删除后将逻辑删除该工作区，并级联清理其会话与关联的 RAG 向量索引。",
                        "确定删除", danger: true);
                    if (ok)
                    {
                        // D6：统一走 WorkspaceManager.DeleteWorkspaceAsync（逻辑删 + 级联清会话 + 清 RAG 索引），
                        // 避免此前「物理删除且遗漏 rag_file/rag_chunk 清理」留下的孤儿索引。
                        await _workspaceManager!.DeleteWorkspaceAsync(ws.WorkspaceId);
                        LoadWorkspaces();
                        LoadRagItems();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"删除工作区失败: {ex.Message}");
                }
            };

            Grid.SetColumn(wsIcon, 0);
            Grid.SetColumn(wsName, 1);
            Grid.SetColumn(newSessionBtn, 2);
            Grid.SetColumn(deleteBtn, 3);
            wsGrid.Children.Add(wsIcon);
            wsGrid.Children.Add(wsName);
            wsGrid.Children.Add(newSessionBtn);
            wsGrid.Children.Add(deleteBtn);
            wsRow.Child = wsGrid;

            // 单击切换工作区；双击工作区名进入行内重命名（D2，替代原菜单里的「重命名」弹窗）
            wsRow.PointerPressed += (s, e) =>
            {
                if (e.Source is Button || e.Source is TextBox) return;
                if (e.ClickCount == 2)
                {
                    BeginRenameWorkspace(wsGrid, ws, 1);
                    return;
                }
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
    /// 双击工作区名进入行内重命名：临时用 TextBox 替换名称列，回车保存、Esc 取消、失焦视为保存。
    /// </summary>
    /// <param name="wsGrid">工作区行 Grid（4 列：图标｜名称｜新建会话｜删除）</param>
    /// <param name="ws">待重命名的工作区</param>
    /// <param name="nameColumn">名称所在列索引</param>
    private void BeginRenameWorkspace(Grid wsGrid, WorkspaceInfo ws, int nameColumn)
    {
        // 已在编辑中则忽略，避免重复插入 TextBox
        if (wsGrid.Children.OfType<TextBox>().Any()) return;

        var original = wsGrid.Children
            .OfType<TextBlock>()
            .FirstOrDefault(t => Grid.GetColumn(t) == nameColumn);
        if (original == null) return;

        var index = wsGrid.Children.IndexOf(original);

        var editor = new TextBox
        {
            Text = ws.Name ?? "",
            FontSize = 13,
            Padding = new Thickness(2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush.Parse("#1E1E1E"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#007ACC"),
        };
        Grid.SetColumn(editor, nameColumn);

        var finished = false;

        // 还原为只读文本（Remove 会触发 LostFocus，finished 标志可防止递归）
        void Restore()
        {
            wsGrid.Children.Remove(editor);
            if (!wsGrid.Children.Contains(original))
                wsGrid.Children.Insert(Math.Min(index, wsGrid.Children.Count), original);
        }

        async Task FinishAsync(bool commit)
        {
            if (finished) return;
            finished = true;

            var newName = (editor.Text ?? "").Trim();
            Restore();

            if (!commit) return;
            if (string.IsNullOrWhiteSpace(newName) || newName == ws.Name) return;

            try
            {
                // 统一走 WorkspaceManager（含更新内存中的当前工作区实例，避免 UI 仍显示旧名）
                if (_workspaceManager != null)
                    await _workspaceManager.RenameWorkspaceAsync(ws.WorkspaceId, newName);
                ws.Name = newName;
                LoadWorkspaces();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"重命名工作区失败: {ex.Message}");
                await ShowErrorAsync($"重命名工作区失败：{ex.Message}");
            }
        }

        editor.KeyDown += async (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await FinishAsync(true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                await FinishAsync(false);
            }
        };

        // 失焦视为提交（与常见文件树重命名行为一致）
        editor.LostFocus += async (s, e) => await FinishAsync(true);

        wsGrid.Children.Remove(original);
        wsGrid.Children.Insert(Math.Min(index, wsGrid.Children.Count), editor);
        editor.Focus();
        editor.SelectAll();
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
            .Select(w => new RagRowModel
            {
                WorkspaceId = w.WorkspaceId,
                DisplayName = "知识库",
                Workspace = w,
            })
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
                    Margin = new Thickness(16, 8, 16, 0),
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

            // 统一走 WorkspaceManager.DeleteWorkspaceAsync（逻辑删 + 级联清会话 + 清 rag_file/rag_chunk 索引），
            // 取代此前「物理删工作区 + 逐个软删会话」的写法——后者会残留孤儿向量索引。
            await _workspaceManager!.DeleteWorkspaceAsync(model.WorkspaceId);

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
        public WorkspaceInfo? Workspace { get; set; }
    }

