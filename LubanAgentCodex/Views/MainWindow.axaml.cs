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
*描述：主窗口，包含标题栏、侧边栏、消息流和输入框
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCodex.ViewModels;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCodex.Views.Controls;
using LubanAgentCore.Services;
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
    }

    /// <summary>
    /// 设置服务提供者并初始化各组件
    /// </summary>
    public void SetServiceProvider(IServiceProvider services)
    {
        _viewModel = new MainWindowViewModel(services);
        DataContext = _viewModel;

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

        // 更新标题栏
        UpdateTitleBar();
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
