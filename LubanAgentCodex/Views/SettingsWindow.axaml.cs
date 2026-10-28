/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SettingsWindow
*版本号： V1.1.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/4
*描述：三栏 IDE 风设置窗，合并「工作区配置（技能/规则/MCP/A2A）」与「供应商与模型/云效连接」两类配置。
*      左栏为分类导航，顶栏为作用域下拉（工作区类）或全局说明（供应商/模型类），
*      中栏条目列表，右栏编辑器。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人：yswenli
*版本号： V1.1.0.0
*描述：本文件收敛为「窗口壳」（状态装配 + 编辑器分发）。业务下沉到 ViewModels/Settings，
*      UI 构建拆分到 Views/Settings/SettingsWindow.{Navigation,Actions,Editors.*}.cs。
*
*****************************************************************************/

namespace LubanAgentCodex.Views;

/// <summary>
/// 统一设置中心：编辑工作区作用域下 <c>.luban-agent</c> 的 skills/rules/mcps/a2a，
/// 以及全局 <c>config.json</c> 中的供应商、模型与云效连接。
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>壳 ViewModel（作用域 / 页签 / 选中项 + 五个分页 ViewModel）。</summary>
    private SettingsWindowViewModel _vm = new(null);

    /// <summary>应用服务提供者（「应用配置」与工作区比对用；容器不可用时为 null）。</summary>
    private readonly IServiceProvider? _services;

    /// <summary>当前编辑器已注册的字段控件（键与 <c>SetField</c> 一致）。</summary>
    private readonly Dictionary<string, Control> _editorFields = new(StringComparer.Ordinal);

    /// <summary>供应商编辑器中「自定义模型」行容器，供保存时收集。</summary>
    private StackPanel? _providerCustomModelsPanel;

    private StackPanel? _categoryPanel;
    private StackPanel? _topBar;
    private ListBox? _itemList;
    private ScrollViewer? _editorHost;
    private TextBlock? _hintText;
    private Button? _newItemBtn;
    private Button? _deleteItemBtn;
    private Button? _applyBtn;
    private Button? _saveBtn;

    private SettingsTabKind Tab { get => _vm.Tab; set => _vm.Tab = value; }

    private string? SelectedItemKey { get => _vm.SelectedItemKey; set => _vm.SelectedItemKey = value; }

    private bool IsGlobal => _vm.IsGlobal;

    private string? ScopeRoot => _vm.ScopeRoot;

    private WorkspaceInfo? SelectedWorkspace { get => _vm.SelectedWorkspace; set => _vm.SelectedWorkspace = value; }

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 创建设置窗并加载指定工作区的配置（传 null 表示默认选中「★ 全局」）。
    /// </summary>
    /// <param name="services">应用服务提供者</param>
    /// <param name="currentWorkspace">预选工作区；为 null 时选中全局作用域</param>
    public SettingsWindow(IServiceProvider services, WorkspaceInfo? currentWorkspace) : this()
    {
        _vm = new SettingsWindowViewModel(services);
        _vm.SelectedWorkspace = currentWorkspace;
        _services = services;
        _ = LoadAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        _categoryPanel = this.FindControl<StackPanel>("CategoryPanel");
        _topBar = this.FindControl<StackPanel>("TopBar");
        _itemList = this.FindControl<ListBox>("ItemList");
        _editorHost = this.FindControl<ScrollViewer>("EditorHost");
        _hintText = this.FindControl<TextBlock>("HintText");
        _newItemBtn = this.FindControl<Button>("NewItemBtn");
        _deleteItemBtn = this.FindControl<Button>("DeleteItemBtn");
        _applyBtn = this.FindControl<Button>("ApplyBtn");
        _saveBtn = this.FindControl<Button>("SaveBtn");

        if (_itemList != null)
        {
            _itemList.SelectionChanged += (s, e) =>
            {
                var key = GetSelectedItemKey();
                if (key != null && key != SelectedItemKey)
                {
                    SelectedItemKey = key;
                    BuildEditor();
                }
            };
        }

        if (_newItemBtn != null) _newItemBtn.Click += (s, e) => NewItem();
        if (_deleteItemBtn != null) _deleteItemBtn.Click += async (s, e) => await DeleteItemAsync();
        if (_applyBtn != null) _applyBtn.Click += (s, e) => ApplyConfig();
        if (_saveBtn != null) _saveBtn.Click += OnSaveClick;
    }

    private async Task LoadAsync()
    {
        await _vm.LoadWorkspacesAsync(SelectedWorkspace);

        BuildCategoryNav();
        BuildTopBar();
        RefreshItems();
        BuildEditor();
    }

    /// <summary>
    /// 预选指定类型页签（供命令面板 /skill /rule /mcp /provider 定位使用）。
    /// </summary>
    /// <param name="kind">要切换到的配置类型</param>
    public void PreselectTab(SettingsTabKind kind)
    {
        Tab = kind;
        SelectedItemKey = null;
        BuildCategoryNav();
        BuildTopBar();
        RefreshItems();
        BuildEditor();
    }

    /// <summary>
    /// 重建右栏编辑器：清空字段表 → 按当前页签构建 → 挂载到内容区。
    /// </summary>
    private void BuildEditor()
    {
        _editorFields.Clear();
        _providerCustomModelsPanel = null;
        if (_editorHost == null) return;

        var key = SelectedItemKey;
        if (string.IsNullOrEmpty(key))
        {
            _editorHost.Content = new TextBlock
            {
                Text = "暂无条目。请点击左下方「＋ 新建」创建，或从列表中选择。",
                FontSize = 12,
                Foreground = Brush.Parse("#8A8A8A"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0),
            };
            UpdateActionBar();
            return;
        }

        var host = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        try
        {
            BuildTabEditor(host, Tab, key);
        }
        catch (Exception ex)
        {
            host.Children.Add(new TextBlock
            {
                Text = $"读取条目失败：{ex.Message}",
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            });
        }

        _editorHost.Content = host;
        UpdateActionBar();
    }
}