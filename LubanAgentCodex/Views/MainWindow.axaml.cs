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
using LubanAgentCodex.Services;
using LubanAgentCodex.ViewModels;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCodex.Views.Controls;
using LubanAgentCore.Entities;
using LubanAgentCore.Services;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Retrieval;
using Microsoft.Extensions.DependencyInjection;
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
    private InputBox? _inputBox;
    private Sidebar? _sidebar;
    private FooterBar? _footerBar;
    private Grid? _loadingOverlay;
    private LubanAgentCodex.Services.FooterDataProvider? _footerDataProvider;
    private bool _confirmedClose;
    private bool _closing;

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
        _loadingOverlay = this.FindControl<Grid>("LoadingOverlay");

        // 订阅键盘事件
        this.KeyDown += OnKeyDown;
    }

    /// <summary>
    /// 设置服务提供者并初始化各组件
    /// </summary>
    public void SetServiceProvider(IServiceProvider services)
    {
        // 完整初始化仅执行一次：避免重复创建 ViewModel、重复订阅事件
        // （App 会在窗口创建与工作区选择完成后各调用一次，重复订阅会导致模型切换等事件被触发多次）
        if (_viewModel == null)
        {
            _viewModel = new MainWindowViewModel(services);
            DataContext = _viewModel;

            // 初始化页脚数据提供者
            _footerDataProvider = new LubanAgentCodex.Services.FooterDataProvider(services);

            // 绑定消息流
            if (_messageStream != null)
            {
                _messageStream.SetMessages(_viewModel.Messages);
                _viewModel.Messages.CollectionChanged += OnMessagesChanged;
            }

            // 绑定输入框
            if (_inputBox != null)
            {
                _inputBox.SetServiceProvider(services);
                _inputBox.SendRequested += async (s, e) =>
                {
                    var text = _inputBox.Text;
                    if (string.IsNullOrWhiteSpace(text)) return;

                    // 立即清空输入框：InputTextBox 未绑定 InputText，且 SendAsync 内部只清空
                    // VM 的 InputText 属性，不会清文本框；原实现把 _inputBox.Text = "" 放在 await
                    // 之后，导致整轮对话（含 AI 流式响应）期间文本一直残留在框内，回车/点发送都如此。
                    _viewModel.InputText = text;
                    _inputBox.Text = "";

                    await _viewModel.SendCommand.ExecuteAsync(null);
                };

                // 处理模型切换事件：重置 Agent，下次发送消息时用新模型重建
                _inputBox.ModelChanged += (s, model) =>
                {
                    _viewModel?.ResetAgent();
                    _viewModel?.Messages.Add(new SystemMessageItem { Content = $"已切换模型: {model}" });
                };

                _viewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.IsRunning))
                    {
                        _inputBox.IsRunning = _viewModel.IsRunning;
                    }
                    else if (e.PropertyName == nameof(MainWindowViewModel.IsSwitchingSession))
                    {
                        if (_loadingOverlay != null)
                        {
                            _loadingOverlay.IsVisible = _viewModel.IsSwitchingSession;
                        }
                    }
                };
            }

            // 绑定侧边栏并订阅其事件（仅首次）
            if (_sidebar != null)
            {
                _sidebar.SetServiceProvider(services);
                _sidebar.WorkspaceSelected += OnWorkspaceSelected;
                _sidebar.SessionSelected += OnSessionSelected;
                _sidebar.RagInitRequested += OnRagInitRequested;
            }
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
        // Shift+Tab 切换权限模式
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift)
        {
            CyclePermissionMode();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 循环切换权限模式
    /// </summary>
    private void CyclePermissionMode()
    {
        if (_viewModel == null) return;

        _viewModel.PermissionMode = _viewModel.PermissionMode switch
        {
            ToolPermissionMode.Default => ToolPermissionMode.Plan,
            ToolPermissionMode.Plan => ToolPermissionMode.AcceptEdits,
            ToolPermissionMode.AcceptEdits => ToolPermissionMode.BypassPermissions,
            ToolPermissionMode.BypassPermissions => ToolPermissionMode.Default,
            _ => ToolPermissionMode.Default
        };

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

    private async void OnSessionSelected(object? sender, SessionItem item)
    {
        if (_viewModel != null)
        {
            await _viewModel.LoadSessionHistoryAsync(item.SessionId);
            UpdateFooter();
        }
    }

    private async void OnWorkspaceSelected(object? sender, WorkspaceInfo ws)
    {
        try
        {
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

            // 知识库（RAG）工作区：自动加载当前会话历史，无需在列表中选择。
            // 复用 SetCurrentAsync 已恢复/新建的 CurrentSession，避免重复建会话（知识库只保留一个会话）
            if (ws.Type == "Rag")
            {
                var sessionManager = services.GetRequiredService<LuBan.AIAgent.Sessions.ISessionManager>();
                var current = sessionManager.CurrentSession;
                if (current == null)
                {
                    // 异常兜底：当前会话缺失则新建
                    current = await sessionManager.CreateSessionAsync(userId: "default", title: "知识库对话");
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
    /// RAG 知识库初始化请求
    /// </summary>
    private async void OnRagInitRequested(object? sender, EventArgs e)
    {
        if (_viewModel?.Services == null) return;

        // 知识库只能初始化一个：已存在时提示先删除
        var workspaceManager = _viewModel.Services.GetRequiredService<IWorkspaceManager>();
        var alreadyHasRag = false;
        foreach (var w in await workspaceManager.GetUserWorkspacesAsync())
        {
            if (w.Type == "Rag") { alreadyHasRag = true; break; }
        }
        if (alreadyHasRag)
        {
            await Dialogs.ShowErrorAsync(this, "已存在知识库，请先删除后再初始化。");
            return;
        }

        // 弹出文件夹选择器让用户选择 RAG 目录
        var storage = this.StorageProvider;
        var folders = await storage.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择 RAG 知识库目录",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            var folder = folders[0];
            var path = folder.Path.LocalPath;
            if (string.IsNullOrEmpty(path)) return;

            // 创建 RAG 工作区
            try
            {
                    var ragWs = await workspaceManager.CreateWorkspaceAsync(path, System.IO.Path.GetFileName(path), type: "Rag");

                    // 初始化向量索引：将所选目录内容嵌入并建立检索库，使知识库可问答。
                    // 必须在“当前工作区=该 RAG 工作区”下索引，保证切块按 WorkspaceId 隔离。
                    var retrieval = _viewModel.Services.GetService<IRetrievalService>();
                    if (retrieval != null)
                    {
                        var previous = workspaceManager.CurrentWorkspace;
                        try
                        {
                            await workspaceManager.EnsureAuthorizedAsync(ragWs);
                            await workspaceManager.SetCurrentAsync(ragWs.WorkspaceId);
                            var report = await retrieval.IndexDirectoryAsync(path);
                            await Dialogs.ShowInfoAsync(this,
                                $"知识库已初始化并索引完成：\n扫描 {report.ScannedFiles} 个文件，切块 {report.TotalChunks} 块。");
                        }
                        catch (Exception idxEx)
                        {
                            await Dialogs.ShowErrorAsync(this,
                                $"知识库已创建，但索引失败：{idxEx.Message}\n你仍可在对话中让 AI 执行索引。");
                        }
                        finally
                        {
                            // 恢复到用户此前所在工作区，避免意外切换会话
                            if (previous != null)
                                await workspaceManager.SetCurrentAsync(previous.WorkspaceId);
                        }
                    }
                    else
                    {
                        await Dialogs.ShowInfoAsync(this,
                            "知识库已创建，但嵌入模型未就绪，暂未建立索引。\n请将嵌入模型包放入 EmbeddingModels 目录后重新初始化。");
                    }

                    _sidebar?.SetServiceProvider(_viewModel.Services);
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
                        Text = $"创建 RAG 知识库失败: {ex.Message}",
                        Margin = new Avalonia.Thickness(20),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                await dialog.ShowDialog(this);
            }
        }
    }

}
