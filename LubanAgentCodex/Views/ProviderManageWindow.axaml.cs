/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： ProviderManageWindow
*版本号： V1.0.0.0
*唯一标识：Provider 管理窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：Provider 管理窗口，用于管理 AI Provider
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCore.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

/// <summary>
/// Provider 管理窗口
/// </summary>
public partial class ProviderManageWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly ConfigManager _configManager;
    private ListBox? _providerListBox;
    private Button? _addButton;
    private Button? _editButton;
    private Button? _deleteButton;
    private Button? _setDefaultButton;

    public ProviderManageWindow(IServiceProvider services)
    {
        _services = services;
        _configManager = services.GetRequiredService<ConfigManager>();
        InitializeComponent();
        LoadProviders();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _providerListBox = this.FindControl<ListBox>("ProviderListBox");
        _addButton = this.FindControl<Button>("AddButton");
        _editButton = this.FindControl<Button>("EditButton");
        _deleteButton = this.FindControl<Button>("DeleteButton");
        _setDefaultButton = this.FindControl<Button>("SetDefaultButton");

        if (_addButton != null) _addButton.Click += OnAdd;
        if (_editButton != null) _editButton.Click += OnEdit;
        if (_deleteButton != null) _deleteButton.Click += OnDelete;
        if (_setDefaultButton != null) _setDefaultButton.Click += OnSetDefault;
        if (_providerListBox != null)
            _providerListBox.SelectionChanged += OnSelectionChanged;
    }

    private void LoadProviders()
    {
        if (_providerListBox == null) return;

        var providers = _configManager.Providers;
        var selectedIndex = _providerListBox.SelectedIndex;

        _providerListBox.ItemsSource = providers.Select(p => new ProviderItem
        {
            Name = p.Name,
            ApiKeyMasked = MaskApiKey(p.ApiKey),
            BaseUrl = p.BaseUrl ?? "(默认)",
            Models = string.Join(", ", _configManager.GetAllModels(p.Name).Take(3)),
            Status = _configManager.SelectedModel?.StartsWith(p.Name + ":") == true ? "✓ 默认" : ""
        }).ToList();

        if (selectedIndex >= 0 && selectedIndex < providers.Count)
            _providerListBox.SelectedIndex = selectedIndex;
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8) return "****";
        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }

    private async void OnAdd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var dlg = new ProviderEditDialog();
            var result = await dlg.ShowDialog<ProviderEditResult?>(this);
            if (result == null) return;

            _configManager.AddProvider(result.Name, result.ApiKey, result.BaseUrl);
            LoadProviders();
            await Dialogs.ShowInfoAsync(this, "Provider 已添加");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderManageWindow.OnAdd 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnEdit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_providerListBox?.SelectedItem is not ProviderItem item) return;
        try
        {
            var provider = _configManager.GetProvider(item.Name);
            if (provider == null) { await Dialogs.ShowErrorAsync(this, "Provider 不存在"); return; }

            var dlg = new ProviderEditDialog(provider);
            var result = await dlg.ShowDialog<ProviderEditResult?>(this);
            if (result == null) return;

            _configManager.AddProvider(provider.Name, result.ApiKey, result.BaseUrl);
            LoadProviders();
            await Dialogs.ShowInfoAsync(this, "Provider 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderManageWindow.OnEdit 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_providerListBox?.SelectedItem is not ProviderItem item) return;
        try
        {
            var ok = await Dialogs.ShowConfirmAsync(this, "删除 Provider",
                $"确定删除 {item.Name} 吗？", okText: "删除", danger: true);
            if (!ok) return;

            var provider = _configManager.GetProvider(item.Name);
            if (provider == null) { await Dialogs.ShowErrorAsync(this, "Provider 不存在"); return; }

            _configManager.Providers.Remove(provider);
            _configManager.Save();

            if (_configManager.SelectedModel?.StartsWith($"{provider.Name}:") == true)
                _configManager.ClearSelectedModel();

            LoadProviders();
            await Dialogs.ShowInfoAsync(this, "Provider 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderManageWindow.OnDelete 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnSetDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_providerListBox?.SelectedItem is not ProviderItem item) return;
        try
        {
            var models = _configManager.GetAllModels(item.Name);
            if (models.Count == 0)
            {
                await Dialogs.ShowInfoAsync(this, "该 Provider 无可用模型，请先添加模型");
                return;
            }

            var currentModel = _configManager.SelectedModel?.StartsWith(item.Name + ":") == true
                ? _configManager.SelectedModel[(item.Name.Length + 1)..]
                : null;

            var dlg = new ModelSelectDialog(models, currentModel);
            var selected = await dlg.ShowDialog<string?>(this);
            if (string.IsNullOrEmpty(selected)) return;

            _configManager.SetSelectedModel($"{item.Name}:{selected}");
            LoadProviders();
            await Dialogs.ShowInfoAsync(this, $"已设为默认: {item.Name}:{selected}");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderManageWindow.OnSetDefault 异常", ex);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = _providerListBox?.SelectedItem != null;
        if (_editButton != null) _editButton.IsEnabled = hasSelection;
        if (_deleteButton != null) _deleteButton.IsEnabled = hasSelection;
        if (_setDefaultButton != null) _setDefaultButton.IsEnabled = hasSelection;
    }

    private class ProviderItem
    {
        public string Name { get; set; } = "";
        public string ApiKeyMasked { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Models { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
