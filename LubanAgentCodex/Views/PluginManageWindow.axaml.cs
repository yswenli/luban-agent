/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： PluginManageWindow
*版本号： V1.0.0.0
*唯一标识：插件管理窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：插件管理窗口（PRD §3.9）：安装 / 启用 / 停用 / 升级 / 卸载
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Lifecycle;
using LuBan.AIAgent.Plugins.Loading;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

/// <summary>
/// 插件管理窗口：列出全部已登记插件并支持安装 / 启用 / 停用 / 升级 / 卸载。
/// </summary>
public partial class PluginManageWindow : Window
{
    private PluginManager? _manager;
    private ListBox? _list;
    private TextBlock? _status;
    private bool _busy;

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）。
    /// </summary>
    public PluginManageWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 带参构造函数。
    /// </summary>
    /// <param name="services">宿主服务提供者（<c>PluginManager</c> 为单例，任意作用域可解析）。</param>
    public PluginManageWindow(IServiceProvider services) : this()
    {
        _manager = services.GetService<PluginManager>();
        if (_manager is null)
        {
            SetStatus("插件体系未注册，无法管理插件。");
            return;
        }

        _manager.Changed += OnPluginChanged;
        Closing += (_, _) => _manager.Changed -= OnPluginChanged;
        Refresh();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        _list = this.FindControl<ListBox>("PluginList");
        _status = this.FindControl<TextBlock>("StatusText");

        if (this.FindControl<TextBlock>("RootPathText") is TextBlock rootText)
            rootText.Text = $"插件根目录：{PluginLocation.BuiltinRoot}";

        Wire("RefreshBtn", Refresh);
        Wire("InstallBtn", () => _ = InstallAsync());
        Wire("EnableBtn", () => _ = EnableAsync());
        Wire("DisableBtn", () => _ = DisableAsync());
        Wire("UpgradeBtn", () => _ = UpgradeAsync());
        Wire("UninstallBtn", () => _ = UninstallAsync());
        Wire("CloseBtn", Close);
    }

    private void Wire(string name, Action action)
    {
        if (this.FindControl<Button>(name) is Button button)
            button.Click += (_, _) => action();
    }

    private PluginRowModel? Selected => _list?.SelectedItem as PluginRowModel;

    #region 列表刷新

    /// <summary>
    /// 重新读取插件列表（保留当前选中项）。
    /// </summary>
    public void Refresh()
    {
        if (_list is null || _manager is null) return;

        var selectedId = Selected?.Id;
        var rows = _manager.GetPlugins().Select(PluginRowModel.From).ToList();
        _list.ItemsSource = rows;

        if (selectedId is not null)
        {
            _list.SelectedItem = rows.FirstOrDefault(
                r => string.Equals(r.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        }

        if (rows.Count == 0)
            SetStatus("未发现任何插件。可点「安装…」选择含 plugin.json 的本地已解压目录。");
    }

    private void OnPluginChanged(object? sender, PluginChangedEventArgs e)
    {
        // 插件状态变更可能来自后台线程（卸载流程），统一切回 UI 线程刷新
        if (Dispatcher.UIThread.CheckAccess())
            Refresh();
        else
            Dispatcher.UIThread.Post(Refresh);
    }

    private void SetStatus(string message)
    {
        if (_status is not null)
            _status.Text = message;

        Logger.Info($"[插件管理] {message}");
    }

    #endregion

    #region 操作

    private async Task InstallAsync()
    {
        if (_manager is null) return;

        var source = await PickDirectoryAsync("选择插件目录（需包含 plugin.json）");
        if (source is null) return;

        await RunAsync(
            $"正在安装：{source}",
            () => _manager.InstallAsync(source),
            id => string.IsNullOrEmpty(id) ? "安装失败：目录不含有效 plugin.json 或清单校验未通过。" : $"插件 {id} 已安装，可点「启用」生效。");
    }

    private async Task EnableAsync()
    {
        if (_manager is null) return;

        var row = Selected ?? RequireSelection();
        if (row is null) return;

        if (row.State == PluginState.Enabled)
        {
            SetStatus($"插件 {row.Name} 已处于启用状态。");
            return;
        }

        await RunAsync(
            $"正在启用 {row.Name}…",
            () => _manager.EnableAsync(row.Id),
            ok => ok ? $"插件 {row.Name} 已启用。" : $"启用失败：{DescribeError(row.Id)}");
    }

    private async Task DisableAsync()
    {
        if (_manager is null) return;

        var row = Selected ?? RequireSelection();
        if (row is null) return;

        if (row.State != PluginState.Enabled)
        {
            SetStatus($"插件 {row.Name} 当前不是启用状态。");
            return;
        }

        var confirmed = await Dialogs.ShowConfirmAsync(
            this,
            "停用插件",
            $"确认停用 {row.Name}？",
            "将注销其侧栏入口 / 面板 / 技能 / MCP 贡献，插件文件保留。",
            "停用");
        if (!confirmed) return;

        await RunAsync(
            $"正在停用 {row.Name}…",
            () => _manager.DisableAsync(row.Id),
            ok => ok ? $"插件 {row.Name} 已停用。" : $"停用失败：{DescribeError(row.Id)}");
    }

    private async Task UpgradeAsync()
    {
        if (_manager is null) return;

        var row = Selected ?? RequireSelection();
        if (row is null) return;

        if (row.IsBuiltin)
        {
            SetStatus("内置插件不支持升级。");
            return;
        }

        var source = await PickDirectoryAsync($"选择 {row.Name} 的新版本目录（plugin.json 的 id 需为 {row.Id}）");
        if (source is null) return;

        // UpgradeAsync 内部完成「卸载旧版 → 校验新版 → 安装 → 重新启用」，
        // 且在卸载验证未通过时中止安装（避免降级态下继续泄漏程序集）。
        await RunAsync(
            $"正在升级 {row.Name}…",
            () => _manager.UpgradeAsync(row.Id, source),
            ok => ok ? $"插件 {row.Name} 已升级到新版本。" : $"升级失败：{DescribeError(row.Id)}");
    }

    private async Task UninstallAsync()
    {
        if (_manager is null) return;

        var row = Selected ?? RequireSelection();
        if (row is null) return;

        if (row.IsBuiltin)
        {
            SetStatus("内置插件不可卸载。");
            return;
        }

        var confirmed = await Dialogs.ShowConfirmAsync(
            this,
            "卸载插件",
            $"确认卸载 {row.Name}？",
            "将停止插件、注销其全部贡献并删除插件目录，操作不可撤销。",
            "卸载",
            danger: true);
        if (!confirmed) return;

        await RunAsync(
            $"正在卸载 {row.Name}…",
            () => _manager.UninstallAsync(row.Id),
            ok => ok ? $"插件 {row.Name} 已卸载。" : $"卸载失败：{DescribeError(row.Id)}");
    }

    private PluginRowModel? RequireSelection()
    {
        SetStatus("请先在列表中选择一个插件。");
        return null;
    }

    /// <summary>
    /// 统一的操作执行壳：防重入、状态提示、结束后刷新列表。
    /// </summary>
    private async Task RunAsync(
        string busyMessage,
        Func<Task<bool>> action,
        Func<bool, string> describe)
    {
        if (_busy)
        {
            SetStatus("正在处理上一个操作，请稍候…");
            return;
        }

        _busy = true;
        SetStatus(busyMessage);
        try
        {
            var ok = await action();
            SetStatus(describe(ok));
        }
        catch (Exception ex)
        {
            SetStatus($"操作异常：{ex.Message}");
            Logger.Error("[插件管理] 操作失败", ex);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private async Task RunAsync(
        string busyMessage,
        Func<Task<string?>> action,
        Func<string?, string> describe)
    {
        if (_busy)
        {
            SetStatus("正在处理上一个操作，请稍候…");
            return;
        }

        _busy = true;
        SetStatus(busyMessage);
        try
        {
            var result = await action();
            SetStatus(describe(result));
        }
        catch (Exception ex)
        {
            SetStatus($"操作异常：{ex.Message}");
            Logger.Error("[插件管理] 操作失败", ex);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private string DescribeError(string pluginId)
    {
        var descriptor = _manager?.GetPlugin(pluginId);
        var error = descriptor?.LastError;
        var hint = descriptor?.RequiresRestart == true ? "；该插件需重启应用才能彻底生效" : string.Empty;
        return string.IsNullOrWhiteSpace(error) ? $"未通过，详见日志{hint}" : $"{error}{hint}";
    }

    private async Task<string?> PickDirectoryAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    #endregion
}

/// <summary>
/// 插件列表行模型。
/// </summary>
public sealed class PluginRowModel
{
    /// <summary>插件标识。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>显示名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>版本号。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>状态文本。</summary>
    public string StateText { get; init; } = string.Empty;

    /// <summary>状态徽标底色。</summary>
    public IBrush StateBrush { get; init; } = Brushes.Gray;

    /// <summary>附加徽标（内置 / 需重启）。</summary>
    public string Badge { get; init; } = string.Empty;

    /// <summary>副标题（作者 · 说明 · id）。</summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>最近一次错误。</summary>
    public string LastError { get; init; } = string.Empty;

    /// <summary>是否有错误信息。</summary>
    public bool HasError { get; init; }

    /// <summary>生命周期状态。</summary>
    public PluginState State { get; init; }

    /// <summary>是否内置插件。</summary>
    public bool IsBuiltin { get; init; }

    /// <summary>
    /// 由插件描述构建行模型。
    /// </summary>
    /// <param name="descriptor">插件描述。</param>
    public static PluginRowModel From(PluginDescriptor descriptor)
    {
        var badges = new List<string>();
        if (descriptor.IsBuiltin) badges.Add("内置");
        if (descriptor.RequiresRestart) badges.Add("需重启生效");

        var subtitle = new List<string>();
        if (!string.IsNullOrWhiteSpace(descriptor.Author)) subtitle.Add(descriptor.Author!);
        if (!string.IsNullOrWhiteSpace(descriptor.Description)) subtitle.Add(descriptor.Description!);
        subtitle.Add(descriptor.Id);

        return new PluginRowModel
        {
            Id = descriptor.Id,
            Name = string.IsNullOrWhiteSpace(descriptor.Name) ? descriptor.Id : descriptor.Name,
            Version = string.IsNullOrWhiteSpace(descriptor.Version) ? string.Empty : $"v{descriptor.Version}",
            StateText = StateLabel(descriptor.State),
            StateBrush = StateColor(descriptor.State),
            Badge = string.Join(" · ", badges),
            Subtitle = string.Join(" · ", subtitle),
            LastError = descriptor.LastError ?? string.Empty,
            HasError = !string.IsNullOrWhiteSpace(descriptor.LastError),
            State = descriptor.State,
            IsBuiltin = descriptor.IsBuiltin,
        };
    }

    private static string StateLabel(PluginState state) => state switch
    {
        PluginState.Enabled => "已启用",
        PluginState.Disabled => "已停用",
        PluginState.Error => "异常",
        _ => "已安装",
    };

    private static IBrush StateColor(PluginState state) => state switch
    {
        PluginState.Enabled => Brush.Parse("#2E7D32"),
        PluginState.Error => Brush.Parse("#C62828"),
        PluginState.Disabled => Brush.Parse("#757575"),
        _ => Brush.Parse("#1565C0"),
    };
}