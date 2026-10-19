/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SettingsWindow
*版本号： V1.0.0.0
*唯一标识：统一设置中心（工作区配置 + 供应商与模型）
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/4
*描述：三栏 IDE 风设置窗，合并「工作区配置（技能/规则/MCP）」与「供应商与模型」两类配置。
*      左栏为分类导航，顶栏为作用域下拉（工作区类）或全局说明（供应商/模型类），
*      中栏条目列表，右栏编辑器。
*
*****************************************************************************/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LuBan.AIAgent;
using LuBan.AIAgent.Configuration;
using LubanAgentCore.Configuration;
using LubanAgentCore.Services;
using LubanAgentCodex.Security;
using LubanAgentA2A.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace LubanAgentCodex.Views;

/// <summary>
/// 设置窗可编辑的配置类型
/// </summary>
public enum SettingsTabKind
{
    /// <summary>技能（skills/&lt;name&gt;/SKILL.md）</summary>
    Skill,

    /// <summary>规则（rules/&lt;id&gt;.json）</summary>
    Rule,

    /// <summary>MCP 服务（mcps/&lt;name&gt;.json）</summary>
    Mcp,

    /// <summary>A2A 协作（.luban-agent/a2a/server.json + client.&lt;name&gt;.json）</summary>
    A2A,

    /// <summary>供应商（全局 config.json）</summary>
    Provider,

    /// <summary>模型（跨供应商，全局 config.json）</summary>
    Model,

    /// <summary>云效连接（外部集成：组织 ID + PAT，全局 config.json / 凭据存储）</summary>
    Yunxiao,
}

/// <summary>
/// 统一设置中心：编辑工作区作用域下 <c>.luban-agent</c> 的 skills/rules/mcps，
/// 以及全局 <c>config.json</c> 中的供应商与模型。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly IServiceProvider? _services;
    private readonly List<WorkspaceInfo> _workspaces = new();
    private ConfigManager? _configManager;

    /// <summary>云效连接配置读写门面（外部集成页；容器不可用时为 null）。</summary>
    private YunxiaoSettingsService? _yunxiaoSettings;

    /// <summary>当前选中工作区；null 表示选中「★ 全局」。</summary>
    private WorkspaceInfo? _selectedWorkspace;

    private SettingsTabKind _tab = SettingsTabKind.Skill;

    /// <summary>当前条目标识：技能=目录名，规则/MCP=文件名（去 .json），供应商=Name，模型=provider:model。</summary>
    private string? _selectedItemKey;

    private bool IsGlobal => _selectedWorkspace == null;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly Dictionary<string, Control> _editorFields = new(StringComparer.Ordinal);

    private StackPanel? _categoryPanel;
    private StackPanel? _topBar;
    private ListBox? _itemList;
    private ScrollViewer? _editorHost;
    private TextBlock? _hintText;
    private Button? _newItemBtn;
    private Button? _deleteItemBtn;
    private Button? _applyBtn;
    private Button? _saveBtn;

    /// <summary>供应商编辑器中「自定义模型」行容器，供保存时收集。</summary>
    private StackPanel? _providerCustomModelsPanel;

    /// <summary>
    /// 内置 Provider 预设见 <see cref="ProviderCatalog.All"/>（CLI / GUI 共用，含自定义项）。
    /// </summary>

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
        _services = services;
        try { _configManager = _services.GetRequiredService<ConfigManager>(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"获取 ConfigManager 失败: {ex.Message}"); _configManager = null; }
        try { _yunxiaoSettings = _services.GetService<YunxiaoSettingsService>(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"获取 YunxiaoSettingsService 失败: {ex.Message}"); _yunxiaoSettings = null; }
        _selectedWorkspace = currentWorkspace;
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
                if (key != null && key != _selectedItemKey)
                {
                    _selectedItemKey = key;
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
        try
        {
            var mgr = _services!.GetRequiredService<IWorkspaceManager>();
            var all = await mgr.GetUserWorkspacesAsync();

            _workspaces.Clear();
            // LLM Wiki 知识库工作区不纳入设置窗的工作区列表
            _workspaces.AddRange(all
                .Where(w => w.Type != WorkspaceTypes.LlmWiki)
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase));

            // 预选的工作区若不在列表内（例如已被删除或是 LLM Wiki 类型），回落到「★ 全局」
            if (_selectedWorkspace != null &&
                !_workspaces.Any(w => w.WorkspaceId == _selectedWorkspace.WorkspaceId))
                _selectedWorkspace = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载工作区列表失败: {ex.Message}");
        }

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
        _tab = kind;
        _selectedItemKey = null;
        BuildCategoryNav();
        BuildTopBar();
        RefreshItems();
        BuildEditor();
    }

    // ---------------- 路径解析 ----------------

    /// <summary>
    /// 按当前作用域解析三类配置目录（仅工作区配置类使用）。
    /// </summary>
    /// <returns>(skills 目录, rules 目录, mcps 目录)</returns>
    private (string skills, string rules, string mcps) ResolveDirs()
    {
        if (IsGlobal)
            return (GlobalLubanAgentPath.SkillsDir, GlobalLubanAgentPath.RulesDir, GlobalLubanAgentPath.McpsDir);

        var baseDir = Path.Combine(_selectedWorkspace!.RootPath, ".luban-agent");
        return (Path.Combine(baseDir, "skills"), Path.Combine(baseDir, "rules"), Path.Combine(baseDir, "mcps"));
    }

    /// <summary>当前类型对应的目录（仅工作区配置类）。</summary>
    private string CurrentDir()
    {
        var dirs = ResolveDirs();
        return _tab switch
        {
            SettingsTabKind.Skill => dirs.skills,
            SettingsTabKind.Rule => dirs.rules,
            SettingsTabKind.A2A => CurrentA2ADir() ?? "",
            _ => dirs.mcps,
        };
    }

    /// <summary>当前工作区的 A2A 配置目录；未选工作区时为 null。</summary>
    private string? CurrentA2ADir()
        => IsGlobal || _selectedWorkspace == null ? null : A2AWorkspaceConfigStore.ResolveDir(_selectedWorkspace.RootPath);

    // ---------------- 左栏：分类导航 ----------------

    private void BuildCategoryNav()
    {
        if (_categoryPanel == null) return;
        _categoryPanel.Children.Clear();

        void AddGroup(string title)
        {
            _categoryPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11,
                Foreground = Brush.Parse("#8A8A8A"),
                Margin = new Thickness(8, 10, 8, 4),
            });
        }

        void AddItem(string text, SettingsTabKind kind)
        {
            var btn = new Button
            {
                Content = text,
                Padding = new Thickness(10, 6),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
                Classes = { _tab == kind ? "dlgPrimary" : "dlgGhost" },
            };
            btn.Click += (s, e) => PreselectTab(kind);
            _categoryPanel.Children.Add(btn);
        }

        AddGroup("工作区配置");
        AddItem("技能", SettingsTabKind.Skill);
        AddItem("规则", SettingsTabKind.Rule);
        AddItem("MCP 服务", SettingsTabKind.Mcp);
        AddItem("A2A 协作", SettingsTabKind.A2A);
        AddGroup("供应商与模型");
        AddItem("供应商", SettingsTabKind.Provider);
        AddItem("模型", SettingsTabKind.Model);
        AddGroup("外部集成");
        AddItem("云效连接", SettingsTabKind.Yunxiao);
    }

    // ---------------- 顶栏：作用域 / 全局说明 ----------------

    private void BuildTopBar()
    {
        if (_topBar == null) return;
        _topBar.Children.Clear();

        // 供应商 / 模型 / 云效：无工作区作用域，显示全局说明
        if (_tab == SettingsTabKind.Provider || _tab == SettingsTabKind.Model || _tab == SettingsTabKind.Yunxiao)
        {
            _topBar.Children.Add(new TextBlock
            {
                Text = _tab == SettingsTabKind.Yunxiao
                    ? "全局 config.json（外部集成凭据与 Provider.ApiKey 同基线，不分工作区）"
                    : "全局 config.json（供应商与模型配置不分工作区）",
                FontSize = 12,
                Foreground = Brush.Parse("#8A8A8A"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }

        // 工作区配置类：作用域下拉
        _topBar.Children.Add(new TextBlock
        {
            Text = "作用域：",
            FontSize = 12,
            Foreground = Brush.Parse("#8A8A8A"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });

        var combo = new ComboBox
        {
            FontSize = 12,
            Padding = new Thickness(8, 4),
            MinWidth = 220,
            Background = Brush.Parse("#1E1E1E"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#3F3F46"),
        };

        var items = new List<string> { "★ 全局" };
        items.AddRange(_workspaces.Select(w => string.IsNullOrWhiteSpace(w.Name) ? w.RootPath : w.Name));
        combo.ItemsSource = items;

        var currentLabel = IsGlobal ? "★ 全局"
            : (_selectedWorkspace != null ? (string.IsNullOrWhiteSpace(_selectedWorkspace.Name) ? _selectedWorkspace.RootPath : _selectedWorkspace.Name) : "★ 全局");
        combo.SelectedItem = currentLabel;

        combo.SelectionChanged += (s, e) =>
        {
            var sel = combo.SelectedItem as string;
            if (sel == "★ 全局")
                _selectedWorkspace = null;
            else
            {
                var ws = _workspaces.FirstOrDefault(w => (string.IsNullOrWhiteSpace(w.Name) ? w.RootPath : w.Name) == sel);
                _selectedWorkspace = ws;
            }
            OnScopeChanged();
        };

        _topBar.Children.Add(combo);
    }

    private void OnScopeChanged()
    {
        _selectedItemKey = null;
        RefreshItems();
        BuildEditor();
    }

    // ---------------- 中栏：条目列表 ----------------

    private List<string> EnumerateItems()
    {
        var dir = CurrentDir();
        if (!Directory.Exists(dir)) return new List<string>();

        if (_tab == SettingsTabKind.Skill)
        {
            return Directory.GetDirectories(dir)
                .Where(d => File.Exists(Path.Combine(d, "SKILL.md")))
                .Select(d => Path.GetFileName(d))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Directory.GetFiles(dir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RefreshItems()
    {
        if (_itemList == null) return;

        // A2A 类：服务端（server.json，若存在）+ 各客户端文件
        if (_tab == SettingsTabKind.A2A)
        {
            var list = new List<A2AListItem>();
            if (!IsGlobal && _selectedWorkspace != null)
            {
                var root = _selectedWorkspace.RootPath;
                if (A2AWorkspaceConfigStore.ServerExists(root))
                {
                    list.Add(new A2AListItem { Key = "server", Display = "服务端   server.json" });
                }
                foreach (var (fileName, cfg) in A2AWorkspaceConfigStore.LoadClients(root))
                {
                    var name = string.IsNullOrWhiteSpace(cfg.Name)
                        ? Path.GetFileNameWithoutExtension(fileName)
                        : cfg.Name!;
                    list.Add(new A2AListItem { Key = fileName, Display = $"客户端   {name}" });
                }
            }
            _itemList.ItemsSource = list;
            var first = list.FirstOrDefault();
            var match = _selectedItemKey != null ? list.FirstOrDefault(x => x.Key == _selectedItemKey) : null;
            _itemList.SelectedItem = match ?? first;
            _selectedItemKey = (match ?? first)?.Key;
            UpdateActionBar();
            return;
        }

        // 供应商类：对象列表（含脱敏 ApiKey 与默认标记）
        if (_tab == SettingsTabKind.Provider)
        {
            var list = (_configManager?.Providers ?? new List<ProviderConfig>())
                .Select(p => new ProviderListItem
                {
                    Name = p.Name,
                    Display = $"{p.Name}   {(string.IsNullOrEmpty(p.ApiKey) ? "" : MaskApiKey(p.ApiKey))}" +
                              (_configManager != null && _configManager.SelectedModel?.StartsWith(p.Name + ":") == true ? "   ✓默认" : ""),
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _itemList.ItemsSource = list;
            var first = list.FirstOrDefault();
            var match = _selectedItemKey != null ? list.FirstOrDefault(x => x.Name == _selectedItemKey) : null;
            _itemList.SelectedItem = match ?? first;
            _selectedItemKey = (match ?? first)?.Name;
            UpdateActionBar();
            return;
        }

        // 模型类：对象列表（provider:model，默认加标记）
        if (_tab == SettingsTabKind.Model)
        {
            var list = new List<ModelListItem>();
            if (_configManager != null)
                foreach (var p in _configManager.Providers)
                    foreach (var m in _configManager.GetAllModels(p.Name))
                    {
                        var key = $"{p.Name}:{m}";
                        list.Add(new ModelListItem { Key = key, Display = key + (_configManager.SelectedModel == key ? "   ✓默认" : "") });
                    }
            list = list.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
            _itemList.ItemsSource = list;
            var first = list.FirstOrDefault();
            var match = _selectedItemKey != null ? list.FirstOrDefault(x => x.Key == _selectedItemKey) : null;
            _itemList.SelectedItem = match ?? first;
            _selectedItemKey = (match ?? first)?.Key;
            UpdateActionBar();
            return;
        }

        // 云效类：固定单条目（外部集成连接）
        if (_tab == SettingsTabKind.Yunxiao)
        {
            _itemList.ItemsSource = new List<string> { "云效连接" };
            _itemList.SelectedItem = "云效连接";
            _selectedItemKey = "yunxiao";
            UpdateActionBar();
            return;
        }

        // 工作区配置（技能 / 规则 / MCP）
        var strs = EnumerateItems();
        _itemList.ItemsSource = strs;
        if (_selectedItemKey != null && strs.Contains(_selectedItemKey))
            _itemList.SelectedItem = _selectedItemKey;
        else
        {
            _selectedItemKey = strs.FirstOrDefault();
            _itemList.SelectedItem = _selectedItemKey;
        }
        UpdateActionBar();
    }

    private string? GetSelectedItemKey()
    {
        if (_itemList?.SelectedItem == null) return null;
        if (_tab == SettingsTabKind.Provider && _itemList.SelectedItem is ProviderListItem pi) return pi.Name;
        if (_tab == SettingsTabKind.Model && _itemList.SelectedItem is ModelListItem mi) return mi.Key;
        if (_tab == SettingsTabKind.A2A && _itemList.SelectedItem is A2AListItem ai) return ai.Key;
        return _itemList.SelectedItem as string;
    }

    private bool ItemExists(string name)
    {
        var dirs = ResolveDirs();
        return _tab switch
        {
            SettingsTabKind.Skill => Directory.Exists(Path.Combine(dirs.skills, name)),
            SettingsTabKind.Rule => File.Exists(Path.Combine(dirs.rules, name + ".json")),
            _ => File.Exists(Path.Combine(dirs.mcps, name + ".json")),
        };
    }

    private void NewItem()
    {
        // 云效：固定单条目，无需新建（按钮已隐藏，此处兜底）
        if (_tab == SettingsTabKind.Yunxiao) return;

        // A2A：服务端文件（若不存在）或新客户端文件
        if (_tab == SettingsTabKind.A2A)
        {
            if (IsGlobal || _selectedWorkspace == null)
            {
                SetHint("A2A 配置仅作用于工作区，请先在顶栏选择一个工作区。", true);
                return;
            }

            var root = _selectedWorkspace.RootPath;
            if (!A2AWorkspaceConfigStore.ServerExists(root))
            {
                A2AWorkspaceConfigStore.SaveServer(root, new A2AWorkspaceFile { Host = "127.0.0.1", Port = 0, Path = "/a2a", RequireApiKey = true, ApiKeys = new List<string>(), AllowedWebhookDomains = new List<string>() });
                _selectedItemKey = "server";
                RefreshItems();
                BuildEditor();
                SetHint("已新建服务端配置 server.json，请填写后点击「保存」。", false);
                return;
            }

            var i = 1;
            var fileName = A2AWorkspaceConfigStore.BuildClientFileName("remote");
            while (File.Exists(Path.Combine(A2AWorkspaceConfigStore.ResolveDir(root), fileName)))
                fileName = A2AWorkspaceConfigStore.BuildClientFileName($"remote{i++}");
            A2AWorkspaceConfigStore.SaveClient(root, fileName, new A2AWorkspaceFile { Mode = "client", Name = Path.GetFileNameWithoutExtension(fileName) });
            _selectedItemKey = fileName;
            RefreshItems();
            BuildEditor();
            SetHint("已新建客户端配置，请填写 Name / Endpoint 后点击「保存」。", false);
            return;
        }

        // 供应商：创建草稿（占位 Name），待编辑器填写后保存
        if (_tab == SettingsTabKind.Provider)
        {
            if (_configManager == null) { SetHint("配置管理器不可用，无法新建供应商。", true); return; }
            var name = "new-provider";
            var i = 1;
            while (_configManager.HasProvider(name)) name = $"new-provider-{i++}";
            _selectedItemKey = name;
            RefreshItems();
            BuildEditor();
            SetHint("已新建供应商草稿，请填写 Name / ApiKey 后点击「保存」。", false);
            return;
        }

        // 模型：无独立新建入口，使用右侧内联「新建自定义模型」
        if (_tab == SettingsTabKind.Model)
        {
            SetHint("请使用右侧编辑器的「新建自定义模型」：选择供应商并填写模型名。", true);
            return;
        }

        // 工作区配置（技能 / 规则 / MCP）
        var dir = CurrentDir();
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            SetHint($"创建目录失败：{ex.Message}", true);
            return;
        }

        var baseName = _tab switch
        {
            SettingsTabKind.Skill => "new-skill",
            SettingsTabKind.Rule => "new-rule",
            _ => "new-mcp",
        };

        var name2 = baseName;
        var j = 1;
        while (ItemExists(name2)) name2 = $"{baseName}-{j++}";

        try
        {
            if (_tab == SettingsTabKind.Skill)
            {
                var d = Path.Combine(dir, name2);
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, "SKILL.md"),
                    BuildSkillMarkdown(name2, "", "custom", "", ""));
            }
            else if (_tab == SettingsTabKind.Rule)
            {
                File.WriteAllText(Path.Combine(dir, name2 + ".json"),
                    JsonSerializer.Serialize(new CustomRuleConfig { Id = name2, Name = name2 }, JsonOpts));
            }
            else
            {
                File.WriteAllText(Path.Combine(dir, name2 + ".json"),
                    JsonSerializer.Serialize(new McpServerConfig { Name = name2 }, JsonOpts));
            }
        }
        catch (Exception ex)
        {
            SetHint($"新建失败：{ex.Message}", true);
            return;
        }

        _selectedItemKey = name2;
        RefreshItems();
        BuildEditor();
        SetHint("已新建条目，请填写内容后点击「保存」。", false);
    }

    // ---------------- 右栏：编辑器 ----------------

    private void BuildEditor()
    {
        _editorFields.Clear();
        _providerCustomModelsPanel = null;
        if (_editorHost == null) return;

        var key = _selectedItemKey;
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
            switch (_tab)
            {
                case SettingsTabKind.Skill:
                    BuildSkillEditor(host, key);
                    break;
                case SettingsTabKind.Rule:
                    BuildRuleEditor(host, key);
                    break;
                case SettingsTabKind.Mcp:
                    BuildMcpEditor(host, key);
                    break;
                case SettingsTabKind.A2A:
                    BuildA2AEditor(host, key);
                    break;
                case SettingsTabKind.Provider:
                    BuildProviderEditor(host, key);
                    break;
                case SettingsTabKind.Model:
                    BuildModelEditor(host, key);
                    break;
                case SettingsTabKind.Yunxiao:
                    BuildYunxiaoEditor(host, key);
                    break;
            }
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

    private void BuildSkillEditor(StackPanel host, string key)
    {
        var dirs = ResolveDirs();
        var mdPath = Path.Combine(dirs.skills, key, "SKILL.md");
        var md = File.Exists(mdPath) ? File.ReadAllText(mdPath) : "";
        var parsed = ParseSkillMarkdown(md);

        SetField("name", AddField(host, "name（必填）",
            string.IsNullOrWhiteSpace(parsed.name) ? key : parsed.name));
        SetField("description", AddField(host, "description（必填）", parsed.desc));
        SetField("category", AddField(host, "category",
            string.IsNullOrWhiteSpace(parsed.category) ? "custom" : parsed.category));
        SetField("triggers", AddField(host, "triggers（逗号分隔，可选）", parsed.triggers));
        SetField("body", AddField(host, "正文 Markdown", parsed.body, multiline: true, height: 220));
    }

    private void BuildRuleEditor(StackPanel host, string key)
    {
        var dirs = ResolveDirs();
        var path = Path.Combine(dirs.rules, key + ".json");

        CustomRuleConfig cfg;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            cfg = JsonSerializer.Deserialize<CustomRuleConfig>(json) ?? new CustomRuleConfig();
        }
        else
        {
            cfg = new CustomRuleConfig { Id = key };
        }

        SetField("Id", AddField(host, "Id（必填，即文件名）", cfg.Id));
        SetField("Name", AddField(host, "Name", cfg.Name));
        SetField("Description", AddField(host, "Description", cfg.Description));
        SetField("ActionTypePattern", AddField(host, "ActionTypePattern（支持通配符）",
            string.IsNullOrEmpty(cfg.ActionTypePattern) ? "*" : cfg.ActionTypePattern));
        SetField("TargetPattern", AddField(host, "TargetPattern（支持通配符）",
            string.IsNullOrEmpty(cfg.TargetPattern) ? "*" : cfg.TargetPattern));
        SetField("Action", AddField(host, "Action（allow / deny）",
            string.IsNullOrEmpty(cfg.Action) ? "deny" : cfg.Action));
        SetField("Priority", AddField(host, "Priority（数字越大优先级越高）", cfg.Priority.ToString()));
        SetField("Enabled", AddToggle(host, "启用该规则", cfg.Enabled));
        SetField("Content", AddField(host, "Content（可选，供 IContentRule 读取的引导文本）",
            cfg.Content ?? "", multiline: true, height: 140));
    }

    private void BuildMcpEditor(StackPanel host, string key)
    {
        var dirs = ResolveDirs();
        var path = Path.Combine(dirs.mcps, key + ".json");

        McpServerConfig cfg;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            cfg = JsonSerializer.Deserialize<McpServerConfig>(json) ?? new McpServerConfig();
        }
        else
        {
            cfg = new McpServerConfig { Name = key };
        }

        SetField("Name", AddField(host, "Name（必填，即文件名）", cfg.Name));
        SetField("Description", AddField(host, "Description", cfg.Description));
        SetField("Transport", AddCombo(host, "Transport", new[] { "stdio", "http", "sse" }, cfg.Transport));
        SetField("Command", AddField(host, "Command（http/sse 时可将 URL 填在此处）", cfg.Command));
        SetField("Args", AddField(host, "Args（一行一个）",
            McpArgsParser.Format(cfg.Args), multiline: true, height: 100));
        SetField("Enabled", AddToggle(host, "启用该服务", cfg.Enabled));

        host.Children.Add(new TextBlock
        {
            Text = "提示：框架侧 http/sse 传输的 baseUrl 取 Args[0] ?? Command，本表单无独立 URL 字段。",
            FontSize = 10,
            Foreground = Brush.Parse("#8A8A8A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
    }

    private void BuildProviderEditor(StackPanel host, string key)
    {
        if (_configManager == null) { SetHint("配置管理器不可用，无法编辑供应商。", true); return; }
        var provider = _configManager.GetProvider(key);
        var isNew = provider == null;
        if (isNew) provider = new ProviderConfig { Name = key };
        var nonNullProvider = provider!;

        // 类型（仅新建态显示，便捷预填 Name / BaseUrl）
        ComboBox? typeCombo = null;
        if (isNew)
        {
            typeCombo = AddCombo(host, "类型（内置预设，可选）", ProviderCatalog.All.Select(b => b.DisplayName).ToList(), ProviderCatalog.All[0].DisplayName);
        }

        var nameBox = AddField(host, "Name（唯一标识，小写）", nonNullProvider.Name);
        if (!isNew) nameBox.IsReadOnly = true;

        SetField("ApiKey", AddPasswordField(host, "ApiKey", nonNullProvider.ApiKey));
        SetField("BaseUrl", AddField(host, "BaseUrl（空=默认）", nonNullProvider.BaseUrl ?? ""));
        SetField("DisplayName", AddField(host, "DisplayName（可选）", nonNullProvider.DisplayName ?? ""));
        SetField("NetworkTimeoutSeconds", AddField(host, "NetworkTimeoutSeconds（空=默认 60）", nonNullProvider.NetworkTimeoutSeconds?.ToString() ?? ""));

        if (typeCombo != null)
            typeCombo.SelectionChanged += (s, e) => OnProviderTypeChanged(typeCombo, nameBox, (TextBox)_editorFields["BaseUrl"]);

        // 自定义模型
        host.Children.Add(new TextBlock
        {
            Text = "自定义模型（可增删）",
            FontSize = 11,
            Foreground = Brush.Parse("#8A8A8A"),
            Margin = new Thickness(0, 8, 0, 4),
        });
        var modelsPanel = new StackPanel();
        host.Children.Add(modelsPanel);
        _providerCustomModelsPanel = modelsPanel;
        foreach (var m in nonNullProvider.CustomModels) AddCustomModelRow(modelsPanel, m);
        var addModelBtn = new Button
        {
            Content = "＋ 添加模型",
            Classes = { "dlgGhost" },
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        addModelBtn.Click += (s, e) => AddCustomModelRow(modelsPanel, "");
        host.Children.Add(addModelBtn);

        // 设为默认模型（仅已有供应商）
        if (!isNew)
        {
            host.Children.Add(new TextBlock
            {
                Text = "设为默认模型",
                FontSize = 11,
                Foreground = Brush.Parse("#8A8A8A"),
                Margin = new Thickness(0, 12, 0, 4),
            });
            var modelCombo = AddCombo(host, "选择模型", _configManager.GetAllModels(nonNullProvider.Name), "");
            var setDefaultBtn = new Button
            {
                Content = "设为默认",
                Classes = { "dlgGhost" },
                Padding = new Thickness(8, 6),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            setDefaultBtn.Click += (s, e) =>
            {
                var m = modelCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(m)) { SetHint("请先选择模型。", true); return; }
                _configManager.SetSelectedModel($"{nonNullProvider.Name}:{m}");
                SetHint($"已设为默认模型：{nonNullProvider.Name}:{m}", false);
                RefreshItems();
            };
            host.Children.Add(setDefaultBtn);
        }
    }

    private void BuildModelEditor(StackPanel host, string key)
    {
        if (_configManager == null) { SetHint("配置管理器不可用，无法编辑模型。", true); return; }
        var parts = key.Split(':', 2);
        var pName = parts[0];
        var mName = parts.Length > 1 ? parts[1] : "";
        var provider = _configManager.GetProvider(pName);
        var isCustom = provider != null && provider.CustomModels.Contains(mName);

        host.Children.Add(new TextBlock
        {
            Text = $"模型：{key}",
            FontSize = 12,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (isCustom)
        {
            SetField("ModelName", AddField(host, "模型名称（可改名）", mName));
            var renameBtn = new Button
            {
                Content = "改名",
                Classes = { "dlgGhost" },
                Padding = new Thickness(8, 6),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            renameBtn.Click += (s, e) =>
            {
                var nm = GetText("ModelName").Trim();
                if (string.IsNullOrEmpty(nm)) { SetHint("名称不能为空。", true); return; }
                _configManager.UpdateCustomModel(pName, mName, nm);
                _selectedItemKey = $"{pName}:{nm}";
                RefreshItems();
                BuildEditor();
                SetHint("已改名。", false);
            };
            host.Children.Add(renameBtn);

            var delBtn = new Button
            {
                Content = "删除模型",
                Classes = { "dlgDanger" },
                Padding = new Thickness(8, 6),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            delBtn.Click += (s, e) =>
            {
                _configManager.RemoveCustomModel(pName, mName);
                _selectedItemKey = null;
                RefreshItems();
                BuildEditor();
                SetHint("已删除自定义模型。", false);
            };
            host.Children.Add(delBtn);
        }
        else
        {
            host.Children.Add(new TextBlock
            {
                Text = "内置模型（只读），可设为默认。",
                FontSize = 11,
                Foreground = Brush.Parse("#8A8A8A"),
            });
        }

        var setDefaultBtn = new Button
        {
            Content = "设为默认",
            Classes = { "dlgGhost" },
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        setDefaultBtn.Click += (s, e) =>
        {
            _configManager.SetSelectedModel(key);
            SetHint($"已设为默认：{key}", false);
            RefreshItems();
        };
        host.Children.Add(setDefaultBtn);

        // 新建自定义模型（内联）
        host.Children.Add(new TextBlock
        {
            Text = "新建自定义模型",
            FontSize = 11,
            Foreground = Brush.Parse("#8A8A8A"),
            Margin = new Thickness(0, 12, 0, 4),
        });
        var pCombo = AddCombo(host, "选择供应商", (_configManager.Providers ?? new List<ProviderConfig>()).Select(p => p.Name).ToList(), pName);
        SetField("NewModelName", AddField(host, "模型名称", ""));
        var addBtn = new Button
        {
            Content = "添加模型",
            Classes = { "dlgPrimary" },
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        addBtn.Click += (s, e) =>
        {
            var selP = pCombo.SelectedItem as string;
            var nm = GetText("NewModelName").Trim();
            if (string.IsNullOrEmpty(selP) || string.IsNullOrEmpty(nm)) { SetHint("请选择供应商并填写模型名。", true); return; }
            try { _configManager.AddCustomModel(selP, nm); }
            catch (Exception ex) { SetHint($"添加失败：{ex.Message}", true); return; }
            _selectedItemKey = $"{selP}:{nm}";
            RefreshItems();
            BuildEditor();
            SetHint("已添加自定义模型。", false);
        };
        host.Children.Add(addBtn);
    }

    // ---------------- A2A 编辑器 ----------------

    /// <summary>
    /// A2A 编辑器：服务端条目（server.json）与客户端条目（client.&lt;name&gt;.json）。
    /// 角色由工作区文件是否存在决定，不使用独立开关。
    /// </summary>
    private void BuildA2AEditor(StackPanel host, string key)
    {
        if (IsGlobal || _selectedWorkspace == null)
        {
            host.Children.Add(new TextBlock
            {
                Text = "A2A 配置仅作用于工作区。请先在顶栏选择一个工作区。",
                FontSize = 12,
                Foreground = Brush.Parse("#8A8A8A"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0),
            });
            return;
        }

        var root = _selectedWorkspace.RootPath;

        if (key == "server")
        {
            var cfg = A2AWorkspaceConfigStore.LoadServer(root) ?? new A2AWorkspaceFile();
            host.Children.Add(new TextBlock
            {
                Text = "服务端角色（server.json）：存在该文件即对外开放 A2A 端点。",
                FontSize = 12,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });

            SetField("Host", AddField(host, "Host（仅回环：127.0.0.1 / ::1）",
                string.IsNullOrWhiteSpace(cfg.Host) ? "127.0.0.1" : cfg.Host));
            SetField("Port", AddField(host, "Port（0 = 随机可用端口）", (cfg.Port ?? 0).ToString()));
            SetField("Path", AddField(host, "Path（端点前缀）",
                string.IsNullOrWhiteSpace(cfg.Path) ? "/a2a" : cfg.Path));
            SetField("RequireApiKey", AddToggle(host, "要求 API Key（X-Api-Key）", cfg.RequireApiKey ?? true));
            SetField("ApiKeys", AddField(host, "ApiKeys（一行一个）",
                string.Join(Environment.NewLine, cfg.ApiKeys ?? new List<string>()), multiline: true, height: 80));
            SetField("AgentName", AddField(host, "AgentName（对外名称）",
                string.IsNullOrWhiteSpace(cfg.AgentName) ? "luban-agent" : cfg.AgentName));
            SetField("AgentDescription", AddField(host, "AgentDescription（可选）", cfg.AgentDescription ?? ""));
            SetField("AgentVersion", AddField(host, "AgentVersion",
                string.IsNullOrWhiteSpace(cfg.AgentVersion) ? "1.0.0" : cfg.AgentVersion));
            return;
        }

        // 客户端文件
        var fallbackName = Path.GetFileNameWithoutExtension(key);
        var client = A2AWorkspaceConfigStore.LoadClients(root)
            .FirstOrDefault(x => string.Equals(x.FileName, key, StringComparison.OrdinalIgnoreCase)).Config
            ?? new A2AWorkspaceFile { Mode = "client", Name = fallbackName };

        host.Children.Add(new TextBlock
        {
            Text = $"客户端角色（{key}）：连接一个远端 A2A agent。",
            FontSize = 12,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        SetField("Name", AddField(host, "Name（唯一标识，用于编排角色路由）", client.Name ?? fallbackName));
        SetField("Endpoint", AddField(host, "Endpoint（远端基址，如 https://host/a2a）", client.Endpoint ?? ""));
        SetField("ApiKey", AddPasswordField(host, "ApiKey（明文，勿提交版本库）", client.ApiKey));
        SetField("RegisterAsSubAgent", AddToggle(host, "注册为本地 SubAgent 角色（registerAsSubAgent）", client.RegisterAsSubAgent ?? false));
        SetField("RemoteAgentName", AddField(host, "RemoteAgentName（可选）", client.RemoteAgentName ?? ""));
        SetField("AllowAll", AddToggle(host, "AllowAll（对外身份声明）", client.AllowAll ?? false));

        host.Children.Add(new TextBlock
        {
            Text = "安全提示：工作区 client*.json 中的 apiKey 为明文，建议将 .luban-agent/a2a/ 加入 .gitignore。",
            FontSize = 10,
            Foreground = Brush.Parse("#8A8A8A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
    }

    // ---------------- 云效连接编辑器（外部集成） ----------------

    /// <summary>
    /// 云效连接编辑器：组织 ID（持久化 + 运行时选项即时生效）+ PAT（写入宿主凭据存储，不回显明文）。
    /// </summary>
    private void BuildYunxiaoEditor(StackPanel host, string key)
    {
        host.Children.Add(new TextBlock
        {
            Text = "云效（Codeup / Flow）连接配置。凭据与 LLM ApiKey 同基线存放在全局 config.json 的 Integrations 节，"
                 + "不会写入会话库；组织 ID 与 PAT 保存后即时生效（无需重启）。",
            FontSize = 11,
            Foreground = Brush.Parse("#8A8A8A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        if (_yunxiaoSettings == null)
        {
            host.Children.Add(new TextBlock
            {
                Text = "云效设置服务不可用（容器未注册 YunxiaoSettingsService）。",
                FontSize = 12,
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
            });
            return;
        }

        var organizationId = _yunxiaoSettings.GetOrganizationId();
        var hasPat = _yunxiaoSettings.HasPat();

        SetField("YunxiaoOrganizationId",
            AddField(host, "组织 ID（云效企业标识，如 60d1xxxxxx）", organizationId));
        SetField("YunxiaoPat",
            AddPasswordField(host, hasPat
                ? "个人访问令牌 PAT（已配置；留空表示不修改）"
                : "个人访问令牌 PAT（尚未配置）", ""));

        host.Children.Add(new TextBlock
        {
            Text = "PAT 仅写入凭据存储，页面不回显明文；也可用环境变量 LUBAN_YUNXIAO_PAT 兜底。"
                 + "插件若要在出口调用云效接口，须在清单中声明 credential: [\"connector:yunxiao:token\"] 与对应网络白名单（PRD §3.7）。",
            FontSize = 10,
            Foreground = Brush.Parse("#8A8A8A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });
    }

    private async Task SaveYunxiaoAsync()
    {
        if (_yunxiaoSettings == null) { SetHint("云效设置服务不可用，无法保存。", true); return; }

        var organizationId = GetText("YunxiaoOrganizationId").Trim();
        var pat = GetText("YunxiaoPat").Trim();

        // PAT 留空 = 保持原值不变（避免误清空既有令牌）
        await _yunxiaoSettings.SaveAsync(organizationId, string.IsNullOrWhiteSpace(pat) ? null : pat);

        BuildEditor();
        SetHint("已保存。组织 ID 与 PAT 已生效（无需重启）。", false);
    }

    // ---------------- 编辑器字段辅助 ----------------

    private void SetField(string key, Control control) => _editorFields[key] = control;

    private string GetText(string key)
        => _editorFields.TryGetValue(key, out var c) && c is TextBox tb ? tb.Text ?? "" : "";

    private bool GetToggle(string key)
        => _editorFields.TryGetValue(key, out var c) && c is CheckBox cb && cb.IsChecked == true;

    private string GetCombo(string key)
        => _editorFields.TryGetValue(key, out var c) && c is ComboBox cmb
            ? cmb.SelectedItem as string ?? ""
            : "";

    private static TextBox AddField(StackPanel host, string label, string? value,
        bool multiline = false, double height = 34)
    {
        host.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Brush.Parse("#8A8A8A"),
            Margin = new Thickness(0, 8, 0, 4),
        });

        var tb = new TextBox
        {
            Text = value ?? "",
            FontSize = 12,
            Padding = new Thickness(8, 6),
            Background = Brush.Parse("#1E1E1E"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#3F3F46"),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = height,
        };

        host.Children.Add(tb);
        return tb;
    }

    private static TextBox AddPasswordField(StackPanel host, string label, string? value)
    {
        host.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Brush.Parse("#8A8A8A"),
            Margin = new Thickness(0, 8, 0, 4),
        });

        var tb = new TextBox
        {
            Text = value ?? "",
            FontSize = 12,
            Padding = new Thickness(8, 6),
            Background = Brush.Parse("#1E1E1E"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#3F3F46"),
            PasswordChar = '*',
        };

        host.Children.Add(tb);
        return tb;
    }

    private static CheckBox AddToggle(StackPanel host, string label, bool value)
    {
        var cb = new CheckBox
        {
            Content = label,
            IsChecked = value,
            FontSize = 12,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 10, 0, 4),
        };
        host.Children.Add(cb);
        return cb;
    }

    private static ComboBox AddCombo(StackPanel host, string label, IEnumerable<string> options, string? value)
    {
        host.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Brush.Parse("#8A8A8A"),
            Margin = new Thickness(0, 8, 0, 4),
        });

        var list = options.ToList();
        var cb = new ComboBox
        {
            ItemsSource = list,
            SelectedItem = list.Contains(value ?? "") ? value : list.FirstOrDefault(),
            FontSize = 12,
            Padding = new Thickness(8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brush.Parse("#1E1E1E"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#3F3F46"),
        };
        host.Children.Add(cb);
        return cb;
    }

    private void AddCustomModelRow(StackPanel panel, string value)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var tb = new TextBox
        {
            Text = value,
            FontSize = 12,
            Padding = new Thickness(8, 6),
            Background = Brush.Parse("#1E1E1E"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#3F3F46"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var del = new Button
        {
            Content = "删除",
            Classes = { "dlgDanger" },
            Padding = new Thickness(8, 6),
        };
        del.Click += (s, e) => panel.Children.Remove(row);
        row.Children.Add(tb);
        row.Children.Add(del);
        panel.Children.Add(row);
    }

    private List<string> CollectCustomModels()
    {
        var result = new List<string>();
        if (_providerCustomModelsPanel == null) return result;
        foreach (var child in _providerCustomModelsPanel.Children)
        {
            if (child is StackPanel row && row.Children.Count > 0 && row.Children[0] is TextBox tb)
            {
                var v = tb.Text?.Trim();
                if (!string.IsNullOrEmpty(v)) result.Add(v);
            }
        }
        return result;
    }

    private void OnProviderTypeChanged(ComboBox typeCombo, TextBox nameBox, TextBox baseUrlBox)
    {
        var idx = typeCombo.SelectedIndex;
        if (idx < 0 || idx >= ProviderCatalog.All.Count) return;
        var preset = ProviderCatalog.All[idx];
        nameBox.Text = preset.Name;
        nameBox.IsReadOnly = preset.Name != "custom";
        baseUrlBox.Text = preset.NeedCustomEndpoint ? preset.DefaultUrl : TryGetDefaultEndpoint(preset.Name);
    }

    private static string TryGetDefaultEndpoint(string name)
    {
        try
        {
            var eps = ProviderHelper.GetEndpoints(name);
            return eps.Count > 0 ? (eps[0].Url ?? "") : "";
        }
        catch
        {
            return "";
        }
    }

    private static string MaskApiKey(string apiKey) => SecretMasker.MaskApiKey(apiKey);

    // ---------------- 技能 Markdown 拼装 / 解析 ----------------

    private static string BuildSkillMarkdown(string name, string desc, string category, string triggers, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {name}");
        sb.AppendLine($"description: {desc}");
        if (!string.IsNullOrWhiteSpace(category)) sb.AppendLine($"category: {category}");
        if (!string.IsNullOrWhiteSpace(triggers)) sb.AppendLine($"triggers: {triggers}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(body ?? "");
        return sb.ToString();
    }

    /// <summary>
    /// 解析 SKILL.md：仅识别 SkillMdParser 支持的四个 frontmatter 键
    /// （name / description / category / triggers），其余键忽略。
    /// </summary>
    private static (string name, string desc, string category, string triggers, string body) ParseSkillMarkdown(string md)
    {
        var name = "";
        var desc = "";
        var category = "";
        var triggers = "";

        var lines = md.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            i = 1;
            while (i < lines.Length && lines[i].Trim() != "---")
            {
                var line = lines[i];
                var idx = line.IndexOf(':');
                if (idx > 0)
                {
                    var k = line[..idx].Trim();
                    var v = line[(idx + 1)..].Trim();
                    switch (k)
                    {
                        case "name": name = v; break;
                        case "description": desc = v; break;
                        case "category": category = v; break;
                        case "triggers": triggers = v; break;
                    }
                }
                i++;
            }
            i++; // 跳过结束分隔符
        }

        var body = string.Join("\n", lines.Skip(i));
        return (name, desc, category, triggers, body);
    }

    // ---------------- 保存 / 删除 / 应用 ----------------

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedItemKey)) return;

        try
        {
            switch (_tab)
            {
                case SettingsTabKind.Skill:
                    await SaveSkillAsync();
                    break;
                case SettingsTabKind.Rule:
                    await SaveRuleAsync();
                    break;
                case SettingsTabKind.Mcp:
                    await SaveMcpAsync();
                    break;
                case SettingsTabKind.A2A:
                    await SaveA2AAsync();
                    break;
                case SettingsTabKind.Provider:
                    await SaveProviderAsync();
                    break;
                case SettingsTabKind.Model:
                    SetHint("模型变更已即时保存（无需点击「保存」）。", false);
                    break;
                case SettingsTabKind.Yunxiao:
                    await SaveYunxiaoAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            SetHint($"保存失败：{ex.Message}", true);
        }
    }

    private async Task SaveSkillAsync()
    {
        var name = GetText("name").Trim();
        var desc = GetText("description").Trim();
        var category = GetText("category").Trim();
        var triggers = GetText("triggers").Trim();
        var body = GetText("body");

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(desc))
        {
            SetHint("技能的 name 与 description 不能为空。", true);
            return;
        }

        var dirs = ResolveDirs();
        Directory.CreateDirectory(dirs.skills);

        var oldKey = _selectedItemKey;
        var targetDir = Path.Combine(dirs.skills, name);

        // 改名：移动目录
        if (!string.IsNullOrEmpty(oldKey) && oldKey != name)
        {
            var oldDir = Path.Combine(dirs.skills, oldKey);
            if (Directory.Exists(oldDir))
            {
                if (Directory.Exists(targetDir))
                {
                    SetHint($"已存在同名技能 \"{name}\"，请更换 name。", true);
                    return;
                }
                Directory.Move(oldDir, targetDir);
            }
        }

        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "SKILL.md"),
            BuildSkillMarkdown(name, desc, category, triggers, body));

        _selectedItemKey = name;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
    }

    private async Task SaveRuleAsync()
    {
        var id = GetText("Id").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            SetHint("规则的 Id 不能为空。", true);
            return;
        }

        _ = int.TryParse(GetText("Priority").Trim(), out var priority);

        var cfg = new CustomRuleConfig
        {
            Id = id,
            Name = GetText("Name").Trim(),
            Description = GetText("Description").Trim(),
            ActionTypePattern = GetText("ActionTypePattern").Trim(),
            TargetPattern = GetText("TargetPattern").Trim(),
            Action = GetText("Action").Trim(),
            Priority = priority,
            Enabled = GetToggle("Enabled"),
            Content = GetText("Content"),
        };

        var dirs = ResolveDirs();
        Directory.CreateDirectory(dirs.rules);

        // 改名：删除旧文件
        var oldKey = _selectedItemKey;
        if (!string.IsNullOrEmpty(oldKey) && oldKey != id)
        {
            var oldPath = Path.Combine(dirs.rules, oldKey + ".json");
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }

        await File.WriteAllTextAsync(Path.Combine(dirs.rules, id + ".json"),
            JsonSerializer.Serialize(cfg, JsonOpts));

        _selectedItemKey = id;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
    }

    private async Task SaveMcpAsync()
    {
        var name = GetText("Name").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetHint("MCP 服务的 Name 不能为空。", true);
            return;
        }

        var args = McpArgsParser.Parse(GetText("Args"));

        var cfg = new McpServerConfig
        {
            Name = name,
            Description = GetText("Description").Trim(),
            Transport = string.IsNullOrWhiteSpace(GetCombo("Transport")) ? "stdio" : GetCombo("Transport"),
            Command = GetText("Command").Trim(),
            Args = args,
            Enabled = GetToggle("Enabled"),
        };

        var dirs = ResolveDirs();
        Directory.CreateDirectory(dirs.mcps);

        var oldKey = _selectedItemKey;
        if (!string.IsNullOrEmpty(oldKey) && oldKey != name)
        {
            var oldPath = Path.Combine(dirs.mcps, oldKey + ".json");
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }

        await File.WriteAllTextAsync(Path.Combine(dirs.mcps, name + ".json"),
            JsonSerializer.Serialize(cfg, JsonOpts));

        _selectedItemKey = name;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
    }

    private async Task SaveA2AAsync()
    {
        if (IsGlobal || _selectedWorkspace == null)
        {
            SetHint("A2A 配置仅作用于工作区。", true);
            return;
        }

        var root = _selectedWorkspace.RootPath;
        var key = _selectedItemKey ?? "";

        if (key == "server")
        {
            var apiKeys = GetText("ApiKeys")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            _ = int.TryParse(GetText("Port").Trim(), out var port);

            var cfg = A2AWorkspaceConfigStore.LoadServer(root) ?? new A2AWorkspaceFile();
            cfg.Mode = "server";
            cfg.Host = string.IsNullOrWhiteSpace(GetText("Host")) ? "127.0.0.1" : GetText("Host").Trim();
            cfg.Port = port;
            cfg.Path = string.IsNullOrWhiteSpace(GetText("Path")) ? "/a2a" : GetText("Path").Trim();
            cfg.RequireApiKey = GetToggle("RequireApiKey");
            cfg.ApiKeys = apiKeys;
            cfg.AgentName = GetText("AgentName").Trim();
            cfg.AgentDescription = GetText("AgentDescription").Trim();
            cfg.AgentVersion = string.IsNullOrWhiteSpace(GetText("AgentVersion")) ? "1.0.0" : GetText("AgentVersion").Trim();

            A2AWorkspaceConfigStore.SaveServer(root, cfg);
            _selectedItemKey = "server";
            RefreshItems();
            BuildEditor();
            SetSavedHint();
            return;
        }

        // 客户端文件
        var name = GetText("Name").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetHint("客户端的 Name 不能为空。", true);
            return;
        }
        var endpoint = GetText("Endpoint").Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            SetHint("客户端的 Endpoint 不能为空。", true);
            return;
        }

        var oldFile = key;
        var targetFile = A2AWorkspaceConfigStore.BuildClientFileName(name);
        var dir = A2AWorkspaceConfigStore.ResolveDir(root);
        if (!string.IsNullOrEmpty(oldFile) && !string.Equals(oldFile, targetFile, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(dir, targetFile)))
        {
            SetHint($"已存在同名客户端文件 {targetFile}，请更换 Name。", true);
            return;
        }

        var client = A2AWorkspaceConfigStore.LoadClients(root)
            .FirstOrDefault(x => string.Equals(x.FileName, key, StringComparison.OrdinalIgnoreCase)).Config
            ?? new A2AWorkspaceFile();
        client.Mode = "client";
        client.Name = name;
        client.Endpoint = endpoint;
        client.ApiKey = string.IsNullOrWhiteSpace(GetText("ApiKey")) ? null : GetText("ApiKey").Trim();
        client.RegisterAsSubAgent = GetToggle("RegisterAsSubAgent");
        client.RemoteAgentName = string.IsNullOrWhiteSpace(GetText("RemoteAgentName")) ? null : GetText("RemoteAgentName").Trim();
        client.AllowAll = GetToggle("AllowAll");

        A2AWorkspaceConfigStore.SaveClient(root, targetFile, client);
        if (!string.IsNullOrEmpty(oldFile) && !string.Equals(oldFile, targetFile, StringComparison.OrdinalIgnoreCase))
            A2AWorkspaceConfigStore.DeleteFile(root, oldFile);

        _selectedItemKey = targetFile;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
        await Task.CompletedTask;
    }

    private async Task SaveProviderAsync()
    {
        if (_configManager == null) { SetHint("配置管理器不可用，无法保存。", true); return; }

        var name = GetText("Name").Trim().ToLowerInvariant();
        var apiKey = GetText("ApiKey").Trim();
        var baseUrl = string.IsNullOrWhiteSpace(GetText("BaseUrl")) ? null : GetText("BaseUrl").Trim();
        var displayName = GetText("DisplayName").Trim();
        var timeoutText = GetText("NetworkTimeoutSeconds").Trim();

        if (string.IsNullOrEmpty(name)) { SetHint("Name 不能为空。", true); return; }
        if (string.IsNullOrEmpty(apiKey)) { SetHint("ApiKey 不能为空。", true); return; }

        // upsert（仅更新 ApiKey / BaseUrl，保留 DisplayName / CustomModels 等）
        _configManager.AddProvider(name, apiKey, baseUrl);
        var provider = _configManager.GetProvider(name);
        if (provider == null) { SetHint("保存失败：Provider 不存在。", true); return; }

        provider.DisplayName = string.IsNullOrEmpty(displayName) ? null : displayName;
        provider.NetworkTimeoutSeconds = int.TryParse(timeoutText, out var t) && t > 0 ? t : null;

        // 自定义模型差异更新
        var desired = CollectCustomModels();
        var current = provider.CustomModels.ToList();
        foreach (var m in desired.Where(m => !current.Contains(m))) _configManager.AddCustomModel(name, m);
        foreach (var m in current.Where(m => !desired.Contains(m))) _configManager.RemoveCustomModel(name, m);

        _configManager.Save();

        _selectedItemKey = name;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
    }

    private async Task DeleteItemAsync()
    {
        var key = _selectedItemKey;
        if (string.IsNullOrEmpty(key)) return;

        // 云效：固定单条目，不可删除（按钮已隐藏，此处兜底）
        if (_tab == SettingsTabKind.Yunxiao) return;

        if (_tab == SettingsTabKind.A2A)
        {
            if (IsGlobal || _selectedWorkspace == null) return;
            var root = _selectedWorkspace.RootPath;
            var isServer = key == "server";
            var ok = await Dialogs.ShowConfirmAsync(this, "确认删除",
                isServer ? "确定删除服务端配置 server.json 吗？" : $"确定删除客户端配置 {key} 吗？",
                "删除后该角色将不再启用，且不可恢复。",
                "确定删除", danger: true);
            if (!ok) return;

            try
            {
                A2AWorkspaceConfigStore.DeleteFile(root, isServer ? "server.json" : key);
            }
            catch (Exception ex)
            {
                SetHint($"删除失败：{ex.Message}", true);
                return;
            }

            _selectedItemKey = null;
            RefreshItems();
            BuildEditor();
            SetHint("已删除该 A2A 配置。", false);
            return;
        }

        if (_tab == SettingsTabKind.Provider)
        {
            if (_configManager == null) return;
            var provider = _configManager.GetProvider(key);
            if (provider == null) return;

            var ok = await Dialogs.ShowConfirmAsync(this, "确认删除",
                $"确定删除供应商 \"{key}\" 吗？",
                "该供应商及其配置将被直接删除，且不可恢复。",
                "确定删除", danger: true);
            if (!ok) return;

            _configManager.Providers.Remove(provider);
            _configManager.Save();
            if (_configManager.SelectedModel?.StartsWith($"{key}:") == true)
                _configManager.ClearSelectedModel();

            _selectedItemKey = null;
            RefreshItems();
            BuildEditor();
            SetHint("已删除该供应商。", false);
            return;
        }

        if (_tab == SettingsTabKind.Model)
        {
            SetHint("模型删除请使用右侧编辑器的「删除模型」按钮。", true);
            return;
        }

        // 工作区配置（技能 / 规则 / MCP）
        var owner = this;
        var confirm = await Dialogs.ShowConfirmAsync(owner, "确认删除",
            $"确定要删除条目 \"{key}\" 吗？",
            "该条目对应的文件或目录将被直接删除，且不可恢复。",
            "确定删除", danger: true);
        if (!confirm) return;

        try
        {
            var dirs = ResolveDirs();
            if (_tab == SettingsTabKind.Skill)
            {
                var dir = Path.Combine(dirs.skills, key);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            else
            {
                var dir = _tab == SettingsTabKind.Rule ? dirs.rules : dirs.mcps;
                var file = Path.Combine(dir, key + ".json");
                if (File.Exists(file)) File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            SetHint($"删除失败：{ex.Message}", true);
            return;
        }

        _selectedItemKey = null;
        RefreshItems();
        BuildEditor();
        SetHint("已删除该条目。", false);
    }

    /// <summary>
    /// 「应用配置」：重置 Agent 宿主，使其在下次对话时按最新配置加载。
    /// 供应商/模型为全局配置，恒可应用。
    /// </summary>
    private void ApplyConfig()
    {
        try
        {
            var host = _services!.GetRequiredService<AgentHostService>();
            host.Reset();
            SetHint("已重置 Agent 宿主，新配置将在下次对话时生效（不打断当前上下文）。", false);
        }
        catch (Exception ex)
        {
            SetHint($"应用配置失败：{ex.Message}", true);
        }
    }

    /// <summary>
    /// 供应商/模型为全局配置恒可应用；工作区配置仅「★ 全局」或「当前对话工作区」可立即应用。
    /// </summary>
    private bool CanApply()
    {
        if (_tab == SettingsTabKind.Provider || _tab == SettingsTabKind.Model) return true;
        if (_tab == SettingsTabKind.A2A) return false;
        // 云效：保存即写入共享选项与凭据存储、即时生效，无需「应用配置」触发工作区回放
        if (_tab == SettingsTabKind.Yunxiao) return false;
        if (IsGlobal) return true;

        try
        {
            var current = _services!.GetRequiredService<IWorkspaceManager>().CurrentWorkspace;
            return current != null && _selectedWorkspace != null
                && current.WorkspaceId == _selectedWorkspace.WorkspaceId;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateActionBar()
    {
        var canApply = CanApply();
        if (_applyBtn != null) _applyBtn.IsVisible = canApply;

        if (_tab == SettingsTabKind.Model)
        {
            // 模型：内联操作，隐藏新建 / 删除 / 保存
            if (_newItemBtn != null) _newItemBtn.IsVisible = false;
            if (_deleteItemBtn != null) _deleteItemBtn.IsVisible = false;
            if (_saveBtn != null) _saveBtn.IsVisible = false;
        }
        else if (_tab == SettingsTabKind.Yunxiao)
        {
            // 云效：固定单条目，隐藏新建 / 删除，保留保存
            if (_newItemBtn != null) _newItemBtn.IsVisible = false;
            if (_deleteItemBtn != null) _deleteItemBtn.IsVisible = false;
            if (_saveBtn != null) _saveBtn.IsVisible = true;
        }
        else
        {
            if (_newItemBtn != null) _newItemBtn.IsVisible = true;
            if (_deleteItemBtn != null)
            {
                _deleteItemBtn.IsVisible = true;
                _deleteItemBtn.IsEnabled = !string.IsNullOrEmpty(_selectedItemKey);
            }
            if (_saveBtn != null) _saveBtn.IsVisible = true;
        }
    }

    private void SetSavedHint()
    {
        SetHint(CanApply()
            ? "已保存。点击「应用配置」可立即生效，否则将在下次切换工作区 / 重启时生效。"
            : "已保存。该配置将在下次切换到此工作区时生效。", false);
    }

    private void SetHint(string text, bool isError)
    {
        if (_hintText == null) return;
        _hintText.Text = text;
        _hintText.Foreground = isError ? Brushes.OrangeRed : Brush.Parse("#8A8A8A");
    }

    // ---------------- 列表项类型 ----------------

    private class ProviderListItem
    {
        public string Name { get; set; } = "";
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }

    private class ModelListItem
    {
        public string Key { get; set; } = "";
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }

    private class A2AListItem
    {
        public string Key { get; set; } = "";
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }
}
