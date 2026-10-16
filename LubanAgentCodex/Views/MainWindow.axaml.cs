/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： MainWindow
*版本号： V1.0.0.0
*唯一标识：主窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：主窗口，包含侧边栏、消息流、输入框和页脚状态栏
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LubanAgentCodex.Plugins;
using LubanAgentCodex.Services;
using LubanAgentCodex.ViewModels;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCodex.Views.Controls;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Plugins;
using LubanAgentCore.Configuration;
using LubanAgentCore.Entities;
using LubanAgentCore.Retrieval;
using LubanAgentCore.Services;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Specialized;
using System.Linq;

namespace LubanAgentCodex.Views;

/// <summary>
/// 主窗口
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private MessageStream? _messageStream;
    private System.Collections.ObjectModel.ObservableCollection<MessageItemBase>? _boundMessages;
    private InputBox? _inputBox;
    private Sidebar? _sidebar;
    private FooterBar? _footerBar;
    private BusyOverlay? _busyOverlay;
    private Grid? _contentRoot;
    private Grid? _chatArea;
    private Grid? _pluginContentHost;
    private ContentControl? _pluginPanelContent;
    private TextBlock? _pluginPanelTitle;
    private string? _mountedPluginEntryId;
    private IServiceProvider? _services;
    private CodexPluginHost? _pluginHost;
    private LubanAgentCodex.Services.FooterDataProvider? _footerDataProvider;
    private bool _confirmedClose;
    private bool _closing;
    private KnowledgeTaskService? _knowledgeTasks;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _messageStream = this.FindControl<MessageStream>("MessageStream");
        _inputBox = this.FindControl<InputBox>("InputBox");
        _sidebar = this.FindControl<Sidebar>("Sidebar");
        _footerBar = this.FindControl<FooterBar>("FooterBar");
        _busyOverlay = this.FindControl<BusyOverlay>("BusyOverlay");
        _contentRoot = this.FindControl<Grid>("ContentRoot");
        _chatArea = this.FindControl<Grid>("ChatArea");
        _pluginContentHost = this.FindControl<Grid>("PluginContentHost");
        _pluginPanelContent = this.FindControl<ContentControl>("PluginPanelContent");
        _pluginPanelTitle = this.FindControl<TextBlock>("PluginPanelTitle");

        // 插件面板「返回对话」：仅隐藏挂载区，面板实例仍由插件持有（真正解绑发生在插件注销时）
        if (this.FindControl<Button>("PluginBackBtn") is Button backBtn)
            backBtn.Click += (s, e) => ShowChatArea();

        // 订阅键盘事件
        this.KeyDown += OnKeyDown;
    }

    /// <summary>
    /// 设置服务提供者并初始化各组件
    /// </summary>
    public void SetServiceProvider(IServiceProvider services)
    {
        _services = services;

        // 完整初始化仅执行一次：避免重复创建 ViewModel、重复订阅事件
        // （App 会在窗口创建与工作区选择完成后各调用一次，重复订阅会导致模型切换等事件被触发多次）
        if (_viewModel == null)
        {
            _viewModel = new MainWindowViewModel(services);
            DataContext = _viewModel;

            // /exit、/quit：请求退出 → 调用 Close，复用已有的 OnClosing 确认流程弹出退出确认框
            _viewModel.ExitRequested += () => Close();

            // 初始化页脚数据提供者
            _footerDataProvider = new LubanAgentCodex.Services.FooterDataProvider(services);

            // 绑定消息流（会话切换时 Messages 引用变化，经 PropertyChanged 重绑）
            BindMessageStream();

            // 绑定输入框
            if (_inputBox != null)
            {
                _inputBox.SetServiceProvider(services);

                // 附件预览条：绑定 ViewModel 的待发送附件并在变化时刷新
                _viewModel.AttachmentsChanged += (s, e) =>
                {
                    UpdateAttachmentBar();
                    UpdateAttachButtonState();
                };
                UpdateAttachmentBar();
                UpdateAttachButtonState();

                _inputBox.SendRequested += async (s, e) =>
                {
                    var text = _inputBox.Text;
                    if (string.IsNullOrWhiteSpace(text) && _viewModel.PendingAttachments.Count == 0) return;

                    // 立即清空输入框：InputTextBox 未绑定 InputText，且 SendAsync 内部只清空
                    // VM 的 InputText 属性，不会清文本框；原实现把 _inputBox.Text = "" 放在 await
                    // 之后，导致整轮对话（含 AI 流式响应）期间文本一直残留在框内，回车/点发送都如此。
                    _viewModel.InputText = text;
                    _inputBox.Text = "";

                    await _viewModel.SendCommand.ExecuteAsync(null);

                    // 早退路径（非 vision 图片 / 初始化失败）不会消费输入：回填文本框避免文本丢失。
                    // 成功发送或 / 命令会把 VM.InputText 置空，且若期间用户已输入新文本则不回填。
                    if (!string.IsNullOrEmpty(_viewModel.InputText) && string.IsNullOrEmpty(_inputBox.Text))
                        _inputBox.Text = _viewModel.InputText;
                };

                // 附件入口：📎 选择文件 / Ctrl+V 粘贴 / 拖拽
                _inputBox.AttachRequested += async (s, e) => await PickAttachmentsAsync();
                _inputBox.ImagePasted += (s, path) => _viewModel?.AddAttachments(new[] { path });
                _inputBox.AttachmentRemoveRequested += (s, fileName) => _viewModel?.RemoveAttachment(fileName);

                // 拖拽落点：窗口级接管，提取本地文件路径
                AddHandler(DragDrop.DragOverEvent, OnDragOver);
                AddHandler(DragDrop.DropEvent, OnDrop);

                // 处理模型切换事件：重置 Agent，下次发送消息时用新模型重建
                _inputBox.ModelChanged += (s, model) =>
                {
                    _viewModel?.ResetAgent();
                    _viewModel?.Messages.Add(new SystemMessageItem { Content = $"已切换模型: {model}" });
                    UpdateAttachButtonState();
                };

                _viewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.IsRunning))
                    {
                        _inputBox.IsRunning = _viewModel.IsRunning;
                    }
                    else if (e.PropertyName == nameof(MainWindowViewModel.Messages))
                    {
                        // 切换会话：Messages 引用变化，重新绑定消息流并订阅新集合
                        BindMessageStream();
                    }
                    else if (e.PropertyName == nameof(MainWindowViewModel.IsSwitchingSession)
                        || e.PropertyName == nameof(MainWindowViewModel.BusyOverlayVisible)
                        || e.PropertyName == nameof(MainWindowViewModel.BusyOverlayText))
                    {
                        UpdateBusyOverlay();
                    }
                    else if (e.PropertyName == nameof(MainWindowViewModel.PermissionMode))
                    {
                        // /mode 命令等任意路径改动模式都要同步页脚，避免显示与实际模式不一致
                        UpdateFooter();
                    }
                };
            }

            // 绑定侧边栏并订阅其事件（仅首次）
            if (_sidebar != null)
            {
                // 先注入运行状态查询，确保随后的 LoadWorkspaces 重建列表时能恢复运行徽标
                _sidebar.SessionRunningProvider = _viewModel.IsSessionRunning;
                _sidebar.SessionPendingConfirmProvider = _viewModel.IsSessionConfirmationPending;
                _sidebar.SetServiceProvider(services);
                _sidebar.WorkspaceSelected += OnWorkspaceSelected;
                _sidebar.SessionSelected += OnSessionSelected;
                _sidebar.LlmWikiInitRequested += OnLlmWikiInitRequested;
                _sidebar.AgentsMdInitRequested += OnAgentsMdInitRequested;
                _sidebar.SettingsRequested += OnSettingsRequested;
                _sidebar.PluginManageRequested += OnPluginManageRequested;
                _sidebar.PluginEntrySelected += OnPluginEntrySelected;

                // 会话运行状态变化（含后台会话）实时更新侧栏徽标
                _viewModel.SessionRunningChanged += (s, e) => _sidebar?.UpdateSessionRunning(e.SessionId, e.IsRunning);
                // 会话待确认状态变化实时更新侧栏徽标
                _viewModel.SessionConfirmationPendingChanged += (s, e) => _sidebar?.UpdateSessionPendingConfirm(e.SessionId, e.Pending);
            }

            // 订阅后台知识库任务：终态时向显示区插系统消息（仅首次订阅）
            _knowledgeTasks = services.GetService<KnowledgeTaskService>();
            if (_knowledgeTasks != null) _knowledgeTasks.Updated += OnKnowledgeTaskUpdated;

            // 插件宿主：面板注销时必须由宿主 UI 同步解绑内容区（PRD §3.12 第 2 步），
            // 否则插件程序集被视觉树钉住，卸载会降级为 RequiresRestart
            _pluginHost = services.GetService<CodexPluginHost>();
            if (_pluginHost != null) _pluginHost.PanelDetaching += OnPluginPanelDetaching;
        }
        else
        {
            // 后续调用（如工作区切换后）仅刷新侧边栏数据，不再重建 VM / 重复订阅
            _sidebar?.SetServiceProvider(services);
        }

        // 更新页脚
        UpdateFooter();
    }

    /// <summary>
    /// 绑定/重绑消息流到 ViewModel 当前的 Messages 集合。
    /// 切换会话时 Messages 引用变化，需退订旧集合并订阅新集合，避免消息投递到已切走的会话。
    /// </summary>
    private void BindMessageStream()
    {
        if (_messageStream == null) return;

        var messages = _viewModel?.Messages;
        if (ReferenceEquals(_boundMessages, messages)) return;

        if (_boundMessages != null)
            _boundMessages.CollectionChanged -= OnMessagesChanged;

        _boundMessages = messages;

        if (_boundMessages == null) return;

        _messageStream.SetMessages(_boundMessages);
        _boundMessages.CollectionChanged += OnMessagesChanged;
    }

    /// <summary>
    /// 窗口关闭拦截：先弹确认，避免误操作退出
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_confirmedClose) return;

        // 先阻止关闭，再弹确认框
        e.Cancel = true;

        // 确认框 await 期间再次触发关闭：已有对话框在等待，直接拦截，避免重复弹窗
        if (_closing) return;
        _closing = true;

        var confirm = await ConfirmExitAsync();
        _closing = false;
        if (confirm)
        {
            _confirmedClose = true;
            Close(); // 再次触发 Closing，此时 _confirmedClose 已置位，直接放行
        }
    }

    /// <summary>
    /// 退出确认对话框（统一美观样式）
    /// </summary>
    private async Task<bool> ConfirmExitAsync()
    {
        return await Dialogs.ShowConfirmAsync(
            this,
            "退出确认",
            "确定要退出 Luban Agent 吗？",
            "退出后当前会话将关闭，未发送的内容不会保存。",
            "退出",
            danger: true);
    }

    /// <summary>
    /// 跳过退出确认直接关闭（初始化流程中用户取消工作区选择时调用）
    /// </summary>
    public void ForceClose()
    {
        _confirmedClose = true;
        Close();
    }

    /// <summary>
    /// 键盘事件处理
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // 知识库索引等长耗时任务期间屏蔽全局快捷键：避免用户以为已可交互
        if (_viewModel?.BusyOverlayVisible == true) return;

        // Esc 取消当前对话
        if (e.Key == Key.Escape)
        {
            _viewModel?.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Shift+Tab 切换权限模式
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift)
        {
            CyclePermissionMode();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 刷新忙碌遮罩：切换会话与知识库索引共用同一遮罩；
    /// 索引期间同时禁用主内容区（侧边栏/消息流/输入框），确保提示全程可见且不可误操作。
    /// </summary>
    private void UpdateBusyOverlay()
    {
        if (_busyOverlay == null || _viewModel == null) return;

        var busy = _viewModel.BusyOverlayVisible;
        _busyOverlay.Text = busy ? _viewModel.BusyOverlayText : "正在切换会话…";
        _busyOverlay.IsVisible = busy || _viewModel.IsSwitchingSession;

        if (_contentRoot != null) _contentRoot.IsEnabled = !busy;
    }

    /// <summary>
    /// 循环切换权限模式
    /// </summary>
    private void CyclePermissionMode()
    {
        if (_viewModel == null) return;

        // 与 CLI 行为对齐：in-flight 回合的 context.Mode 在流开始时已固定，
        // 运行中切换只会让页脚与实际生效模式不一致，故直接拒绝并提示
        if (_viewModel.IsRunning)
        {
            _viewModel.Messages.Add(new SystemMessageItem { Content = "Agent 运行中无法切换权限模式" });
            return;
        }

        _viewModel.PermissionMode = PermissionModeRules.Next(_viewModel.PermissionMode);

        UpdateFooter();

        // 如果切换到 Bypass，显示二次确认
        if (_viewModel.PermissionMode == ToolPermissionMode.BypassPermissions)
        {
            _ = ConfirmBypassModeAsync();
        }
    }

    /// <summary>
    /// BypassPermissions 模式二次确认
    /// </summary>
    private async Task ConfirmBypassModeAsync()
    {
        var dialog = new Window
        {
            Title = "⚠️ 安全确认",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16
        };

        content.Children.Add(new TextBlock
        {
            Text = "确定要切换到跳过权限模式吗？",
            FontSize = 14,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = "此模式下所有工具调用将跳过确认，可能存在安全风险。",
            Foreground = Avalonia.Media.Brush.Parse("#F44336"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var okButton = new Button { Content = "确定" };
        var cancelButton = new Button { Content = "取消" };

        okButton.Click += (s, e) => dialog.Close(true);
        cancelButton.Click += (s, e) => dialog.Close(false);

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        content.Children.Add(buttonPanel);

        dialog.Content = content;

        var result = await dialog.ShowDialog<bool?>(this);
        if (result != true && _viewModel != null)
        {
            // 用户取消，恢复到默认模式
            _viewModel.PermissionMode = ToolPermissionMode.Default;
            UpdateFooter();
        }
    }

    /// <summary>
    /// 更新页脚状态栏
    /// </summary>
    private void UpdateFooter()
    {
        if (_footerBar == null || _footerDataProvider == null || _viewModel == null) return;

        _footerBar.UpdatePermissionMode(_viewModel.PermissionMode);
        _footerBar.UpdateWorkingDirectory(_footerDataProvider.GetWorkingDirectory());
        _footerBar.UpdateGitBranch(_footerDataProvider.GetGitBranch());
        _footerBar.UpdateTokenUsage(_footerDataProvider.GetTokenUsage());
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_inputBox != null && _viewModel != null)
        {
            _inputBox.IsRunning = _viewModel.IsRunning;
        }
    }

    /// <summary>
    /// 刷新附件预览条
    /// </summary>
    private void UpdateAttachmentBar()
    {
        if (_viewModel == null) return;
        _inputBox?.SetAttachments(_viewModel.PendingAttachments);
    }

    /// <summary>
    /// 按当前模型能力与附件数量启用/禁用 📎 附件按钮。
    /// 非 vision 模型不禁用按钮（文本文件仍可添加），仅在加入图片时提示；
    /// 仅当达到附件数量上限时禁用。
    /// </summary>
    private void UpdateAttachButtonState()
    {
        if (_inputBox == null || _viewModel == null) return;

        var modelId = _viewModel.Services.GetService<ConfigManager>()?.SelectedModel ?? "";
        var supportsVision = _viewModel.Services.GetModelCapabilityService().SupportsVision(modelId);
        var atCap = _viewModel.PendingAttachments.Count >= AttachmentService.MaxAttachmentsPerMessage;

        if (atCap)
            _inputBox.SetAttachEnabled(false, $"已达上限 {AttachmentService.MaxAttachmentsPerMessage} 个附件");
        else if (!supportsVision)
            _inputBox.SetAttachEnabled(true, "当前模型不支持图片，仅可添加文本文件");
        else
            _inputBox.SetAttachEnabled(true, "选择附件（也可拖拽文件到窗口 / Ctrl+V 粘贴）");
    }

    /// <summary>
    /// 📎 按钮：选择本地文件并加入附件
    /// </summary>
    private async Task PickAttachmentsAsync()
    {
        if (_viewModel == null) return;

        var extensions = _viewModel.Services.GetRequiredService<AttachmentService>().SupportedExtensions
            .Select(e => "*" + e)
            .ToList();

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择附件",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("支持的附件") { Patterns = extensions }
            }
        });

        var paths = files.Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        if (paths.Count == 0) return;

        var added = _viewModel.AddAttachments(paths);
        if (added < paths.Count)
        {
            _viewModel.Messages.Add(new SystemMessageItem
            {
                Content = $"已添加 {added} 个附件；其余被忽略（不支持的类型、重复或已达上限 {AttachmentService.MaxAttachmentsPerMessage} 个）"
            });
        }
    }

    /// <summary>
    /// 拖拽悬停：仅当携带文件时展示复制光标
    /// </summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// 拖拽释放：提取本地文件路径加入附件
    /// </summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_viewModel == null || !e.DataTransfer.Contains(DataFormat.File))
            return;

        var paths = new List<string>();
        foreach (var item in e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>())
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }

        if (paths.Count > 0)
            _viewModel.AddAttachments(paths);

        e.Handled = true;
    }

    private async void OnSessionSelected(object? sender, SessionItem item)
    {
        // 选择会话 = 回到对话视图（插件页是内容区的一次性覆盖）
        ShowChatArea();

        if (_viewModel != null)
        {
            await _viewModel.LoadSessionHistoryAsync(item.SessionId);
            UpdateFooter();
        }
    }

    #region 插件内容区

    /// <summary>
    /// 插件侧栏入口被选中：把该入口对应的面板挂载到内容区。
    /// </summary>
    private void OnPluginEntrySelected(object? sender, PluginSidebarRowModel row)
    {
        if (_pluginHost is null || _pluginPanelContent is null || _pluginContentHost is null) return;

        IPluginPanel? panel;
        try
        {
            // 面板工厂在此首次实例化（延迟创建，PRD §3.4）
            panel = _pluginHost.GetPanel(row.Id);
        }
        catch (Exception ex)
        {
            Logger.Error($"实例化插件面板失败：{row.Id}", ex);
            AddSystemMessage($"插件面板「{row.Label}」创建失败：{ex.Message}");
            return;
        }

        if (panel is not Control control)
        {
            AddSystemMessage($"插件「{row.Label}」未注册可用面板（宿主内容区需要 Avalonia 控件实现 IPluginPanel）。");
            return;
        }

        if (!ReferenceEquals(_pluginPanelContent.Content, control))
            _pluginPanelContent.Content = control;

        if (_pluginPanelTitle is not null)
            _pluginPanelTitle.Text = string.IsNullOrWhiteSpace(panel.Title) ? row.Label : panel.Title;

        _pluginContentHost.IsVisible = true;
        if (_chatArea is not null) _chatArea.IsVisible = false;

        _mountedPluginEntryId = row.Id;
    }

    /// <summary>
    /// 插件面板注销前的同步解绑（由插件宿主在 UI 线程调用）。
    /// </summary>
    private void OnPluginPanelDetaching(string entryId, IPluginPanel panel)
    {
        if (!string.Equals(_mountedPluginEntryId, entryId, StringComparison.OrdinalIgnoreCase))
            return;

        // 必须在 UI 线程同步清空：任何残留引用都会让插件程序集无法回收
        if (_pluginPanelContent is not null)
            _pluginPanelContent.Content = null;

        _mountedPluginEntryId = null;
        ShowChatArea();
    }

    /// <summary>
    /// 返回对话视图（隐藏插件面板挂载区）。
    /// </summary>
    private void ShowChatArea()
    {
        if (_pluginContentHost is not null) _pluginContentHost.IsVisible = false;
        if (_chatArea is not null) _chatArea.IsVisible = true;
    }

    /// <summary>
    /// 打开插件管理窗口。
    /// </summary>
    private void OnPluginManageRequested(object? sender, EventArgs e)
    {
        var services = _viewModel?.Services ?? _services;
        if (services is null) return;

        try
        {
            new PluginManageWindow(services).Show(this);
        }
        catch (Exception ex)
        {
            Logger.Error("打开插件管理窗口失败", ex);
            AddSystemMessage($"打开插件管理窗口失败：{ex.Message}");
        }
    }

    private void AddSystemMessage(string content)
        => _viewModel?.Messages.Add(new SystemMessageItem { Content = content });

    #endregion

    private async void OnWorkspaceSelected(object? sender, WorkspaceInfo ws)
    {
        try
        {
            // 切换工作区 = 回到对话视图（插件页是内容区的一次性覆盖）
            ShowChatArea();

            var services = _viewModel?.Services;
            if (services == null) return;

            var workspaceManager = services.GetRequiredService<IWorkspaceManager>();

            // 桌面端 AuthorizationPrompt 恒为 true（自动授权）：切换工作区前先确保已授权，
            // 否则工作区根目录不会被注入 PathGuard.AllowedRoots，导致文件/目录/分析类工具
            // 在 IsAllowed 处被拒（提示"路径不在允许访问的范围内"），且不会弹出任何权限询问。
            if (!ws.IsAuthorized)
                await workspaceManager.EnsureAuthorizedAsync(ws);

            // SetCurrentAsync 内部已恢复或新建当前会话（SwitchWorkspaceAsync）
            await workspaceManager.SetCurrentAsync(ws.WorkspaceId);

            // 知识库（LLM Wiki）工作区：自动加载当前会话历史，无需在列表中选择。
            // 复用 SetCurrentAsync 已恢复/新建的 CurrentSession，避免重复建会话（知识库只保留一个会话）
            if (ws.Type == WorkspaceTypes.LlmWiki)
            {
                var sessionManager = services.GetRequiredService<LuBan.AIAgent.Sessions.ISessionManager>();
                var current = sessionManager.CurrentSession;
                if (current == null)
                {
                    // 异常兜底：当前会话缺失则新建
                    current = await sessionManager.CreateSessionAsync(title: "知识库对话");
                }
                await _viewModel!.LoadSessionHistoryAsync(current.SessionId);
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _sidebar?.SetServiceProvider(services);
                UpdateFooter();
            });
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow.OnWorkspaceSelected 切换知识库会话异常", ex);
            await Dialogs.ShowErrorAsync(this, $"切换到知识库会话失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 初始化工作区 AGENTS.md 请求：切到目标工作区 → 结构化表单 → 生成 → 落盘 → 注入聊天流
    /// </summary>
    private async void OnAgentsMdInitRequested(object? sender, WorkspaceInfo ws)
    {
        try
        {
            var services = _viewModel?.Services;
            if (services == null) return;

            if (_viewModel!.IsRunning)
            {
                await Dialogs.ShowErrorAsync(this, "Agent 运行中，请等待当前回合结束后再试。");
                return;
            }

            var workspaceManager = services.GetRequiredService<IWorkspaceManager>();

            // 与 OnWorkspaceSelected 一致：先确保授权。授权后工作区根目录才会注入 PathGuard.AllowedRoots，
            // 否则生成过程中的文件/目录类工具会被拒（提示"路径不在允许访问的范围内"）
            if (!ws.IsAuthorized)
                await workspaceManager.EnsureAuthorizedAsync(ws);

            // 决策：右键非当前工作区时先切过去，保证生成时的文件访问与落盘都作用于目标工作区
            await workspaceManager.SetCurrentAsync(ws.WorkspaceId);

            Dispatcher.UIThread.Post(() =>
            {
                _sidebar?.SetServiceProvider(services);
                UpdateFooter();
            });

            var initializer = services.GetRequiredService<IAgentsMdInitializer>();
            var existing = initializer.ReadExisting(ws);

            // 预填：Agent 名称默认取工作区名；输出模式默认 full
            var dialog = new InitAgentsMdDialog(new AgentsMdOptions { AgentName = ws.Name });
            var options = await dialog.ShowDialog<AgentsMdOptions?>(this);
            if (options == null) return;

            if (existing != null)
            {
                var confirmed = await Dialogs.ShowConfirmAsync(
                    this,
                    "覆盖 AGENTS.md",
                    "已存在 AGENTS.md，将合并更新并覆盖",
                    initializer.GetTargetPath(ws),
                    "覆盖",
                    danger: true);
                if (!confirmed) return;
            }

            await _viewModel.RunInitAgentsMdAsync(options with { ExistingContent = existing });
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow.OnAgentsMdInitRequested 初始化 AGENTS.md 异常", ex);
            await Dialogs.ShowErrorAsync(this, $"初始化 AGENTS.md 失败：{ex.Message}");
        }
    }

    /// <summary>
    /// LLM Wiki 知识库初始化请求
    /// </summary>
    private async void OnLlmWikiInitRequested(object? sender, EventArgs e)
    {
        if (_viewModel?.Services == null) return;

        // 知识库只能初始化一个：已存在时提示先删除
        var workspaceManager = _viewModel.Services.GetRequiredService<IWorkspaceManager>();
        var alreadyHasLlmWiki = false;
        foreach (var w in await workspaceManager.GetUserWorkspacesAsync())
        {
            if (w.Type == WorkspaceTypes.LlmWiki) { alreadyHasLlmWiki = true; break; }
        }
        if (alreadyHasLlmWiki)
        {
            await Dialogs.ShowErrorAsync(this, "已存在知识库，请先删除后再初始化。");
            return;
        }

        // 弹出文件夹选择器让用户选择 LLM Wiki 目录
        var storage = this.StorageProvider;
        var folders = await storage.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择 LLM Wiki 知识库目录",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            var folder = folders[0];
            var path = folder.Path.LocalPath;
            if (string.IsNullOrEmpty(path)) return;

            // 创建 LLM Wiki 工作区：必需部分（创建/授权/预扫）同步完成即放行，索引进后台
            try
            {
                    var vm = _viewModel!;
                    var previous = workspaceManager.CurrentWorkspace;
                    vm.BusyOverlayText = "正在创建知识库…";
                    vm.BusyOverlayVisible = true;

                    try
                    {
                        var llmWikiWs = await workspaceManager.CreateWorkspaceAsync(path, null, type: WorkspaceTypes.LlmWiki);
                        await workspaceManager.EnsureAuthorizedAsync(llmWikiWs);
                        await workspaceManager.SetCurrentAsync(llmWikiWs.WorkspaceId);

                        // 检索 scope 为 root 派生，需回填当前用户身份
                        using var retrievalScope = vm.Services.CreateUserScope(vm.Services.ResolveUserKey());
                        var retrieval = retrievalScope.ServiceProvider.GetService<IRetrievalService>();
                        if (retrieval == null)
                        {
                            await Dialogs.ShowInfoAsync(this,
                                "知识库已创建，但嵌入模型未就绪，暂未建立索引。\n请将嵌入模型包放入 EmbeddingModels 目录后重新初始化。");
                            return;
                        }

                        var maxFileSizeBytes = vm.Services.GetRequiredService<IOptions<LuBanAgentOptions>>()
                            .Value.Tools.Retrieval.MaxFileSizeKB * 1024L;

                        // 预扫放后台线程，仅遮罩文案在 UI 线程更新（扫描期间按目录刷新动态文案，见 U-1）
                        vm.BusyOverlayText = RetrievalIndexPlanner.IndexScanStatus(path);
                        var fileCount = await Task.Run(() =>
                            RetrievalIndexPlanner.CountIndexableFiles(path, null, maxFileSizeBytes,
                                scan => Avalonia.Threading.Dispatcher.UIThread.Post(
                                    () => vm.BusyOverlayText = RetrievalIndexPlanner.IndexScanningStatus(scan.CurrentDirectory, scan.IndexableFiles))));

                        if (fileCount == 0)
                        {
                            await Dialogs.ShowInfoAsync(this,
                                $"{RetrievalIndexPlanner.NoIndexableFilesText}\n知识库已创建。");
                            return;
                        }

                        // 必需部分完成：立即关闭遮罩（finally）、恢复原工作区，然后提示并转后台索引
                        vm.Messages.Add(new SystemMessageItem
                        {
                            Content = $"知识库「{llmWikiWs.Name}」已创建，正在后台建立索引（{fileCount} 个文件）…"
                        });

                        var tasks = vm.Services.GetService<KnowledgeTaskService>();
                        if (tasks != null)
                            tasks.StartIndex(llmWikiWs.WorkspaceId, path, null, llmWikiWs.UserId);
                        else
                            await Dialogs.ShowInfoAsync(this,
                                "知识库已创建，但后台任务服务未就绪，请使用 /llmwiki -index 手动建立索引。");
                    }
                    finally
                    {
                        // 恢复到用户此前所在工作区，避免意外切换会话；索引按 workspaceId 归属新知识库
                        if (previous != null)
                            await workspaceManager.SetCurrentAsync(previous.WorkspaceId);
                        vm.BusyOverlayVisible = false;
                        _sidebar?.SetServiceProvider(vm.Services);
                    }
            }
            catch (Exception ex)
            {
                // 显示错误
                var dialog = new Window
                {
                    Title = "错误",
                    Width = 400,
                    Height = 200,
                    Content = new TextBlock
                    {
                        Text = $"创建 LLM Wiki 知识库失败: {ex.Message}",
                        Margin = new Avalonia.Thickness(20),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                await dialog.ShowDialog(this);
            }
        }
    }

    /// <summary>
    /// 后台知识库任务终态：向显示区插第二条系统消息（进行中不打扰用户）
    /// </summary>
    private void OnKnowledgeTaskUpdated(KnowledgeTaskInfo info)
    {
        if (info.State is not (KnowledgeTaskState.Completed or KnowledgeTaskState.Failed or KnowledgeTaskState.Canceled))
            return;

        var vm = _viewModel;
        if (vm == null) return;

        var text = info.State switch
        {
            KnowledgeTaskState.Completed => $"{info.Label} 已完成：{info.Detail}",
            KnowledgeTaskState.Canceled => $"{info.Label} 已取消",
            _ => $"{info.Label} 失败：{info.Detail}"
        };

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            vm.Messages.Add(new SystemMessageItem
            {
                Content = text,
                IsError = info.State == KnowledgeTaskState.Failed
            }));
    }

    /// <summary>
    /// 打开工作区设置中心（技能 / 规则 / MCP）
    /// </summary>
    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            var services = _viewModel?.Services;
            if (services == null) return;

            // 预选当前正在对话的工作区（非 LLM Wiki），便于直接编辑其配置；
            // LLM Wiki 知识库工作区不纳入设置窗（D9），当前为 LLM Wiki 时回落到「★ 全局」。
            var workspaceManager = services.GetRequiredService<IWorkspaceManager>();
            var current = workspaceManager.CurrentWorkspace;
            WorkspaceInfo? preselect = (current != null && current.Type != WorkspaceTypes.LlmWiki) ? current : null;

            var settings = new SettingsWindow(services, preselect);
            await settings.ShowDialog(this);

            // 关闭后刷新侧边栏（配置可能已变更，工作区名称等需同步）
            _sidebar?.SetServiceProvider(services);
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow.OnSettingsRequested 打开设置中心异常", ex);
            await Dialogs.ShowErrorAsync(this, $"打开设置中心失败：{ex.Message}");
        }
    }

}
