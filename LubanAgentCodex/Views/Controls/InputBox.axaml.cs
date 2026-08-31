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
using Avalonia.Markup.Xaml;
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
    private TextBlock? _processingHint;
    private ComboBox? _modelCombo;
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
        _processingHint = this.FindControl<TextBlock>("ProcessingHint");
        _modelCombo = this.FindControl<ComboBox>("ModelCombo");

        if (_inputTextBox != null)
            _inputTextBox.KeyDown += OnKeyDown;

        if (_sendButton != null)
            _sendButton.Click += OnSendButtonClick;

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
