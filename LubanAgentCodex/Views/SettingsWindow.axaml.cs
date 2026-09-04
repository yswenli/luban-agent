/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SettingsWindow
*版本号： V1.0.0.0
*唯一标识：工作区设置中心（技能 / 规则 / MCP）
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/4
*描述：三栏 IDE 风设置窗，直接读写作用域目录下的 .luban-agent（skills/rules/mcps）。
*      左栏为配置作用域（★ 全局 + 各工作区），顶部 Tab 切类型，中栏条目列表，右栏编辑器。
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
using LubanAgentCore.Services;
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
}

/// <summary>
/// 工作区设置中心：直接编辑作用域下 <c>.luban-agent</c> 目录内的 skills / rules / mcps。
/// </summary>
/// <remarks>
/// 作用域分两类：
/// <list type="bullet">
/// <item><description><b>★ 全局</b>：用户级 <c>~/.luban-agent</c>（由 <see cref="GlobalLubanAgentPath"/> 解析）。</description></item>
/// <item><description><b>工作区</b>：指定工作区根目录下的 <c>.luban-agent</c>。</description></item>
/// </list>
/// 加载顺序由框架保证：先加载全局、再加载工作区，同标识项工作区覆盖全局（见设计文档 4.7）。
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly IServiceProvider? _services;
    private readonly List<WorkspaceInfo> _workspaces = new();

    /// <summary>当前选中工作区；null 表示选中「★ 全局」。</summary>
    private WorkspaceInfo? _selectedWorkspace;

    private SettingsTabKind _tab = SettingsTabKind.Skill;

    /// <summary>当前条目标识：技能=目录名，规则/MCP=文件名（去 .json）。</summary>
    private string? _selectedItemKey;

    private bool IsGlobal => _selectedWorkspace == null;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly Dictionary<string, Control> _editorFields = new(StringComparer.Ordinal);

    private StackPanel? _scopePanel;
    private StackPanel? _tabBar;
    private ListBox? _itemList;
    private ScrollViewer? _editorHost;
    private TextBlock? _hintText;
    private Button? _newItemBtn;
    private Button? _deleteItemBtn;
    private Button? _applyBtn;
    private Button? _saveBtn;

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
        _selectedWorkspace = currentWorkspace;
        _ = LoadAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        _scopePanel = this.FindControl<StackPanel>("ScopePanel");
        _tabBar = this.FindControl<StackPanel>("TabBar");
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
                if (_itemList.SelectedItem is string key && key != _selectedItemKey)
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
            // D9：RAG 知识库工作区不纳入设置窗的工作区列表
            _workspaces.AddRange(all
                .Where(w => w.Type != "Rag")
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase));

            // 预选的工作区若不在列表内（例如已被删除或是 RAG 类型），回落到「★ 全局」
            if (_selectedWorkspace != null &&
                !_workspaces.Any(w => w.WorkspaceId == _selectedWorkspace.WorkspaceId))
                _selectedWorkspace = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载工作区列表失败: {ex.Message}");
        }

        BuildScopePanel();
        BuildTabBar();
        RefreshItems();
        BuildEditor();
    }

    /// <summary>
    /// 预选指定类型页签（供命令面板 /skill /rule /mcp 定位使用）。
    /// </summary>
    /// <param name="kind">要切换到的配置类型</param>
    public void PreselectTab(SettingsTabKind kind)
    {
        _tab = kind;
        _selectedItemKey = null;
        BuildTabBar();
        RefreshItems();
        BuildEditor();
    }

    // ---------------- 路径解析 ----------------

    /// <summary>
    /// 按当前作用域解析三类配置目录。
    /// </summary>
    /// <returns>(skills 目录, rules 目录, mcps 目录)</returns>
    private (string skills, string rules, string mcps) ResolveDirs()
    {
        if (IsGlobal)
            return (GlobalLubanAgentPath.SkillsDir, GlobalLubanAgentPath.RulesDir, GlobalLubanAgentPath.McpsDir);

        var baseDir = Path.Combine(_selectedWorkspace!.RootPath, ".luban-agent");
        return (Path.Combine(baseDir, "skills"), Path.Combine(baseDir, "rules"), Path.Combine(baseDir, "mcps"));
    }

    /// <summary>当前类型对应的目录。</summary>
    private string CurrentDir()
    {
        var dirs = ResolveDirs();
        return _tab switch
        {
            SettingsTabKind.Skill => dirs.skills,
            SettingsTabKind.Rule => dirs.rules,
            _ => dirs.mcps,
        };
    }

    // ---------------- 左栏：作用域 ----------------

    private void BuildScopePanel()
    {
        if (_scopePanel == null) return;
        _scopePanel.Children.Clear();

        _scopePanel.Children.Add(BuildScopeRow(
            "★ 全局", GlobalLubanAgentPath.Root, IsGlobal, () =>
            {
                _selectedWorkspace = null;
                OnScopeChanged();
            }));

        _scopePanel.Children.Add(new Border
        {
            Height = 1,
            Background = Brush.Parse("#2A2A2A"),
            Margin = new Thickness(8, 6),
        });

        foreach (var ws in _workspaces)
        {
            var captured = ws;
            var selected = !IsGlobal && _selectedWorkspace?.WorkspaceId == ws.WorkspaceId;
            var title = string.IsNullOrWhiteSpace(ws.Name) ? ws.RootPath : ws.Name;

            _scopePanel.Children.Add(BuildScopeRow(title, ws.RootPath, selected, () =>
            {
                _selectedWorkspace = captured;
                OnScopeChanged();
            }));
        }
    }

    private static Border BuildScopeRow(string title, string sub, bool selected, Action onSelect)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 2),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = selected ? Brush.Parse("#2D2D30") : Brushes.Transparent,
            BorderThickness = selected ? new Thickness(2, 0, 0, 0) : new Thickness(0),
            BorderBrush = selected ? Brush.Parse("#007ACC") : null,
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (!string.IsNullOrWhiteSpace(sub))
        {
            stack.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 10,
                Foreground = Brush.Parse("#8A8A8A"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        border.Child = stack;
        border.PointerPressed += (s, e) => onSelect();
        return border;
    }

    private void OnScopeChanged()
    {
        _selectedItemKey = null;
        BuildScopePanel();
        RefreshItems();
        BuildEditor();
    }

    // ---------------- 顶部 Tab ----------------

    private void BuildTabBar()
    {
        if (_tabBar == null) return;
        _tabBar.Children.Clear();

        void AddTab(string text, SettingsTabKind kind)
        {
            var btn = new Button
            {
                Content = text,
                Padding = new Thickness(14, 6),
                FontSize = 12,
                Cursor = new Cursor(StandardCursorType.Hand),
                Classes = { _tab == kind ? "dlgPrimary" : "dlgGhost" },
            };
            btn.Click += (s, e) => PreselectTab(kind);
            _tabBar.Children.Add(btn);
        }

        AddTab("技能", SettingsTabKind.Skill);
        AddTab("规则", SettingsTabKind.Rule);
        AddTab("MCP", SettingsTabKind.Mcp);
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

        var items = EnumerateItems();
        _itemList.ItemsSource = items;

        if (_selectedItemKey != null && items.Contains(_selectedItemKey))
            _itemList.SelectedItem = _selectedItemKey;
        else
        {
            _selectedItemKey = items.FirstOrDefault();
            _itemList.SelectedItem = _selectedItemKey;
        }
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

        var name = baseName;
        var i = 1;
        while (ItemExists(name)) name = $"{baseName}-{i++}";

        try
        {
            if (_tab == SettingsTabKind.Skill)
            {
                var d = Path.Combine(dir, name);
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, "SKILL.md"),
                    BuildSkillMarkdown(name, "", "custom", "", ""));
            }
            else if (_tab == SettingsTabKind.Rule)
            {
                File.WriteAllText(Path.Combine(dir, name + ".json"),
                    JsonSerializer.Serialize(new CustomRuleConfig { Id = name, Name = name }, JsonOpts));
            }
            else
            {
                File.WriteAllText(Path.Combine(dir, name + ".json"),
                    JsonSerializer.Serialize(new McpServerConfig { Name = name }, JsonOpts));
            }
        }
        catch (Exception ex)
        {
            SetHint($"新建失败：{ex.Message}", true);
            return;
        }

        _selectedItemKey = name;
        RefreshItems();
        BuildEditor();
        SetHint("已新建条目，请填写内容后点击「保存」。", false);
    }

    // ---------------- 右栏：编辑器 ----------------

    private void BuildEditor()
    {
        _editorFields.Clear();
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
            if (_deleteItemBtn != null) _deleteItemBtn.IsEnabled = false;
            UpdateActionBar();
            return;
        }

        if (_deleteItemBtn != null) _deleteItemBtn.IsEnabled = true;

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
                default:
                    BuildMcpEditor(host, key);
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
            string.Join(Environment.NewLine, cfg.Args), multiline: true, height: 100));
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
                default:
                    await SaveMcpAsync();
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

        var args = GetText("Args")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

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

    private async Task DeleteItemAsync()
    {
        var key = _selectedItemKey;
        if (string.IsNullOrEmpty(key)) return;

        var owner = this;
        var ok = await Dialogs.ShowConfirmAsync(owner, "确认删除",
            $"确定要删除条目 \"{key}\" 吗？",
            "该条目对应的文件或目录将被直接删除，且不可恢复。",
            "确定删除", danger: true);
        if (!ok) return;

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
    /// 「应用配置」：重置 Agent 宿主，使其在下次对话时按「先全局 → 后工作区」重新加载配置。
    /// </summary>
    private void ApplyConfig()
    {
        try
        {
            var host = _services.GetRequiredService<AgentHostService>();
            host.Reset();
            SetHint("已重置 Agent 宿主，新配置将在下次对话时生效（不打断当前上下文）。", false);
        }
        catch (Exception ex)
        {
            SetHint($"应用配置失败：{ex.Message}", true);
        }
    }

    /// <summary>
    /// 仅「★ 全局」与「当前正在对话的工作区」支持立即应用（其余工作区需切换后才加载）。
    /// </summary>
    private bool CanApply()
    {
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
        if (_deleteItemBtn != null) _deleteItemBtn.IsEnabled = !string.IsNullOrEmpty(_selectedItemKey);
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
}
