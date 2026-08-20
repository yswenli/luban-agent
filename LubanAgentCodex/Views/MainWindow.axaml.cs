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
*描述：主窗口，包含标题栏、侧边栏、消息流、输入框和页脚状态栏
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using LubanAgentCodex.Services;
using LubanAgentCodex.ViewModels;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCodex.Views.Controls;
using LubanAgentCore.Services;
using LuBan.AIAgent.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Specialized;

namespace LubanAgentCodex.Views;

/// <summary>
/// 主窗口
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private MessageStream? _messageStream;
    private InputBox? _inputBox;
    private TitleBar? _titleBar;
    private Sidebar? _sidebar;
    private FooterBar? _footerBar;
    private LubanAgentCodex.Services.FooterDataProvider? _footerDataProvider;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _messageStream = this.FindControl<MessageStream>("MessageStream");
        _inputBox = this.FindControl<InputBox>("InputBox");
        _titleBar = this.FindControl<TitleBar>("TitleBar");
        _sidebar = this.FindControl<Sidebar>("Sidebar");
        _footerBar = this.FindControl<FooterBar>("FooterBar");
        
        // 订阅键盘事件
        this.KeyDown += OnKeyDown;
    }

    /// <summary>
    /// 设置服务提供者并初始化各组件
    /// </summary>
    public void SetServiceProvider(IServiceProvider services)
    {
        _viewModel = new MainWindowViewModel(services);
        DataContext = _viewModel;

        // 初始化页脚数据提供者
        _footerDataProvider = new LubanAgentCodex.Services.FooterDataProvider(services);

        // 绑定侧边栏
        if (_sidebar != null)
        {
            _sidebar.SetServiceProvider(services);
            _sidebar.WorkspaceSelected += OnWorkspaceSelected;
            _sidebar.SessionSelected += OnSessionSelected;
            _sidebar.RagInitRequested += OnRagInitRequested;
        }

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
                _viewModel.InputText = _inputBox.Text;
                await _viewModel.SendCommand.ExecuteAsync(null);
                _inputBox.Text = "";
            };

            // 处理模型切换事件
            _inputBox.ModelChanged += (s, model) =>
            {
                // 模型已通过 InputBox 内部的 ConfigManager 更新
                // 这里可以添加通知或其他逻辑
                System.Diagnostics.Debug.WriteLine($"模型已切换: {model}");
            };

            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsRunning))
                {
                    _inputBox.IsRunning = _viewModel.IsRunning;
                }
            };
        }

        // 更新标题栏和页脚
        UpdateTitleBar();
        UpdateFooter();
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
            if (_titleBar != null)
            {
                _titleBar.SessionTitle = item.Title;
            }
            UpdateFooter();
        }
    }

    private void OnWorkspaceSelected(object? sender, WorkspaceInfo ws)
    {
        var workspaceManager = _viewModel?.Services?.GetRequiredService<IWorkspaceManager>();
        if (workspaceManager != null && _viewModel?.Services != null)
        {
            _ = Task.Run(async () =>
            {
                await workspaceManager.SetCurrentAsync(ws.WorkspaceId);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _sidebar?.SetServiceProvider(_viewModel.Services);
                    UpdateTitleBar();
                    UpdateFooter();
                });
            });
        }
    }

    /// <summary>
    /// RAG 知识库初始化请求
    /// </summary>
    private async void OnRagInitRequested(object? sender, EventArgs e)
    {
        if (_viewModel?.Services == null) return;

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
            var workspaceManager = _viewModel.Services.GetRequiredService<IWorkspaceManager>();
            try
            {
                var ragWs = await workspaceManager.CreateWorkspaceAsync(path, System.IO.Path.GetFileName(path), type: "Rag");
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

    private void UpdateTitleBar()
    {
        if (_titleBar != null && _viewModel?.Services != null)
        {
            var workspaceManager = _viewModel.Services.GetRequiredService<IWorkspaceManager>();
            if (workspaceManager.CurrentWorkspace != null)
            {
                _titleBar.SessionTitle = workspaceManager.CurrentWorkspace.Name;
            }
            _titleBar.SessionTime = DateTime.Now.ToString("MM月dd日 HH:mm");
        }
    }
}
