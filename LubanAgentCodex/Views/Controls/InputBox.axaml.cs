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
    }

    /// <summary>
    /// 加载可用模型列表
    /// </summary>
    private void LoadModels()
    {
        if (_modelCombo == null || _configManager == null) return;

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

        // 选中当前模型
        if (!string.IsNullOrEmpty(_configManager.SelectedModel))
        {
            var idx = allModels.IndexOf(_configManager.SelectedModel);
            if (idx >= 0) _modelCombo.SelectedIndex = idx;
        }
    }

    /// <summary>
    /// 模型选择变更
    /// </summary>
    private void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_modelCombo?.SelectedItem is string model && _configManager != null)
        {
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
    public event EventHandler? CancelRequested;
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
