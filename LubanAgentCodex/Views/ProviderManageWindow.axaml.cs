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
        {
            _providerListBox.SelectionChanged += OnSelectionChanged;
        }
    }

    private void LoadProviders()
    {
        if (_providerListBox == null) return;

        var providers = _configManager.Providers
            .Select(p => new ProviderItem
            {
                Name = p.Name,
                Models = string.Join(", ", _configManager.GetAllModels(p.Name).Take(3)),
                Status = _configManager.SelectedModel?.StartsWith(p.Name + ":") == true ? "✓ 默认" : ""
            })
            .ToList();

        _providerListBox.ItemsSource = providers;
    }

    private void OnAdd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 添加 Provider
    }

    private void OnEdit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 编辑 Provider
    }

    private void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 删除 Provider
    }

    private void OnSetDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: 设为默认
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
        public string Models { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
