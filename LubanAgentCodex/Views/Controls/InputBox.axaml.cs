/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： InputBox
*版本号： V1.0.0.0
*唯一标识：输入框控件
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：消息输入框控件，支持 Enter 发送和 Ctrl+Enter 换行
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LubanAgentCore.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 输入框控件
/// </summary>
public partial class InputBox : UserControl
{
    private TextBox? _inputTextBox;
    private Button? _sendButton;
    private Button? _attachButton;
    private TextBlock? _processingHint;
    private ComboBox? _modelCombo;
    private ItemsControl? _attachmentBar;
    private ConfigManager? _configManager;
    private bool _suppressSelectionChanged;

    public InputBox()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _inputTextBox = this.FindControl<TextBox>("InputTextBox");
        _sendButton = this.FindControl<Button>("SendButton");
        _attachButton = this.FindControl<Button>("AttachButton");
        _processingHint = this.FindControl<TextBlock>("ProcessingHint");
        _modelCombo = this.FindControl<ComboBox>("ModelCombo");
        _attachmentBar = this.FindControl<ItemsControl>("AttachmentBar");

        if (_inputTextBox != null)
            _inputTextBox.KeyDown += OnKeyDown;

        if (_sendButton != null)
            _sendButton.Click += OnSendButtonClick;

        if (_attachButton != null)
            _attachButton.Click += OnAttachButtonClick;

        if (_modelCombo != null)
            _modelCombo.SelectionChanged += OnModelSelectionChanged;
    }

    /// <summary>
    /// 设置服务提供者，加载模型列表
    /// </summary>
    public void SetServiceProvider(IServiceProvider services)
    {
        _configManager = services.GetRequiredService<ConfigManager>();
        LoadModels();
        RefreshModels();
    }

    /// <summary>
    /// 加载可用模型列表。
    /// 始终保证当前选中模型出现在下拉项中并处于选中状态；
    /// 同时纳入各 Provider 通过远程刷新获取的模型（ProviderHelper._fetchedModels 缓存）。
    /// </summary>
    private void LoadModels()
    {
        if (_modelCombo == null || _configManager == null) return;

        _suppressSelectionChanged = true;
        try
        {
            _modelCombo.Items.Clear();
            var allModels = new List<string>();

            foreach (var provider in _configManager.Providers)
            {
                var models = _configManager.GetAllModels(provider.Name);
                foreach (var model in models)
                {
                    var fullName = $"{provider.Name}:{model}";
                    allModels.Add(fullName);
                    _modelCombo.Items.Add(fullName);
                }
            }

            // 当前选中模型必须常显：即便其所属 Provider 无任何预设/自定义/远程模型，也要加入下拉
            if (!string.IsNullOrEmpty(_configManager.SelectedModel) &&
                !allModels.Contains(_configManager.SelectedModel))
            {
                allModels.Add(_configManager.SelectedModel);
                _modelCombo.Items.Add(_configManager.SelectedModel);
            }

            // 选中当前模型
            if (!string.IsNullOrEmpty(_configManager.SelectedModel))
            {
                var idx = allModels.IndexOf(_configManager.SelectedModel);
                if (idx >= 0) _modelCombo.SelectedIndex = idx;
            }
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    /// <summary>
    /// 后台异步拉取各 Provider 的远程模型列表（/v1/models），成功后重建下拉项。
    /// 单个 Provider 失败不影响其它 Provider，且最终回退到「仅当前模型」的可用状态。
    /// </summary>
    private void RefreshModels()
    {
        if (_configManager == null || _modelCombo == null) return;

        _ = Task.Run(async () =>
        {
            foreach (var provider in _configManager.Providers)
            {
                if (string.IsNullOrWhiteSpace(provider.BaseUrl) &&
                    string.IsNullOrWhiteSpace(provider.ApiKey))
                {
                    continue;
                }

                try
                {
                    await ProviderHelper.RefreshModelsAsync(
                        provider.Name,
                        provider.ApiKey ?? string.Empty,
                        provider.BaseUrl);
                }
                catch
                {
                    // 忽略单个 Provider 刷新失败，留给 LoadModels 的回退逻辑
                }
            }

            // 回到 UI 线程重建下拉项
            Avalonia.Threading.Dispatcher.UIThread.Post(LoadModels);
        });
    }

    /// <summary>
    /// 模型选择变更
    /// </summary>
    private void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (_modelCombo?.SelectedItem is string model && _configManager != null)
        {
            // 幂等护栏：避免重复触发（如 ComboBox 下拉点选时重复引发 SelectionChanged）
            // 已与当前持久化模型相同则视为无变化，跳过
            if (model == _configManager.SelectedModel) return;
            _configManager.SetSelectedModel(model);
            ModelChanged?.Invoke(this, model);
        }
    }

    /// <summary>
    /// 键盘事件处理：Enter 发送，Ctrl+Enter 换行
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = HandleClipboardFilesAsync();
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                if (_inputTextBox != null)
                {
                    var caretIndex = _inputTextBox.CaretIndex;
                    var text = _inputTextBox.Text ?? "";
                    _inputTextBox.Text = text.Insert(caretIndex, Environment.NewLine);
                    _inputTextBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                }
                e.Handled = true;
            }
            else
            {
                e.Handled = true;
                if (!_isProcessing)
                    SendRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnSendButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isProcessing) return;
        SendRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SendRequested;
    public event EventHandler<string>? ModelChanged;

    /// <summary>请求选择附件（由 MainWindow 接管 StorageProvider）。</summary>
    public event EventHandler? AttachRequested;

    /// <summary>粘贴到附件（剪贴板中的文件路径）。</summary>
    public event EventHandler<string>? ImagePasted;

    /// <summary>请求移除附件（参数为文件名）。</summary>
    public event EventHandler<string>? AttachmentRemoveRequested;

    /// <summary>刷新附件预览条。</summary>
    public void SetAttachments(IReadOnlyList<AttachmentDisplayInfo> attachments)
    {
        if (_attachmentBar == null) return;
        _attachmentBar.ItemsSource = attachments;
        _attachmentBar.IsVisible = attachments.Count > 0;
    }

    /// <summary>按模型能力启用/禁用附件入口并更新提示。</summary>
    public void SetAttachEnabled(bool enabled, string tip)
    {
        if (_attachButton == null) return;
        _attachButton.IsEnabled = enabled;
        _attachButton.Opacity = enabled ? 1.0 : 0.5;
        // 默认禁用态不显示 ToolTip；开启后可提示"已达上限"等原因
        ToolTip.SetShowOnDisabled(_attachButton, true);
        ToolTip.SetTip(_attachButton, tip);
    }

    private void OnAttachButtonClick(object? sender, RoutedEventArgs e)
        => AttachRequested?.Invoke(this, EventArgs.Empty);

    private void OnRemoveAttachmentClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AttachmentDisplayInfo info)
            AttachmentRemoveRequested?.Invoke(this, info.FileName);
    }

    /// <summary>
    /// Ctrl+V 时探测剪贴板中的文件或位图。注意不要同步 e.Handled=true 后再 await：
    /// 事件早已冒泡完成，异步置位无效。这里不拦截默认粘贴——文件/位图形式的剪贴板数据
    /// 不含文本格式，TextBox 不会插入内容；无文件无位图时退化为普通文本粘贴。
    /// </summary>
    private async Task HandleClipboardFilesAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            var files = await clipboard.TryGetFilesAsync();
            if (files != null)
            {
                var paths = files.Select(f => f.TryGetLocalPath())
                                 .Where(p => !string.IsNullOrEmpty(p))
                                 .Cast<string>()
                                 .ToList();
                foreach (var p in paths)
                    ImagePasted?.Invoke(this, p);
                if (paths.Count > 0) return;
            }

            // 剪贴板位图（如系统截图）：写入工作区临时目录后按附件路径加入
            var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap == null) return;

            var tempPath = LuBan.Common.IO.TempDirectory.GetTempFilePath("clipboard-", ".png");
            bitmap.Save(tempPath);
            ImagePasted?.Invoke(this, tempPath);
        }
        catch (Exception ex)
        {
            Logger.Debug("InputBox.HandleClipboardFilesAsync 读取剪贴板失败", ex);
        }
    }

    private bool _isProcessing;

    public string Text
    {
        get => _inputTextBox?.Text ?? "";
        set { if (_inputTextBox != null) _inputTextBox.Text = value; }
    }

    public bool IsRunning
    {
        set
        {
            _isProcessing = value;
            if (_sendButton != null)
            {
                _sendButton.IsEnabled = !value;
                _sendButton.Opacity = value ? 0.6 : 1.0;
            }
            if (_processingHint != null)
                _processingHint.IsVisible = value;
        }
    }
}
