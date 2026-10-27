/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SettingsWindow.Navigation
*版本号： V1.0.0.0
*唯一标识：统一设置中心（左栏导航 / 顶栏作用域 / 中栏列表）
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗的导航与列表部分（Task 19 从 SettingsWindow.axaml.cs 拆出，行为与文案原样保留）。
*      条目枚举 / 供应商 / 模型 / A2A 列表均经 ViewModels.Settings 取数。
*
*****************************************************************************/
using LuBan.AIAgent;
using LubanAgentCodex.ViewModels.Settings;

namespace LubanAgentCodex.Views;

public partial class SettingsWindow
{
    // ---------------- 路径解析 ----------------

    /// <summary>
    /// 按当前作用域解析三类配置目录（仅工作区配置类使用）。
    /// </summary>
    /// <returns>(skills 目录, rules 目录, mcps 目录)</returns>
    private (string skills, string rules, string mcps) ResolveDirs()
    {
        if (IsGlobal)
            return (GlobalLubanAgentPath.SkillsDir, GlobalLubanAgentPath.RulesDir, GlobalLubanAgentPath.McpsDir);

        var baseDir = Path.Combine(SelectedWorkspace!.RootPath, ".luban-agent");
        return (Path.Combine(baseDir, "skills"), Path.Combine(baseDir, "rules"), Path.Combine(baseDir, "mcps"));
    }

    /// <summary>当前工作区的 A2A 配置目录；未选工作区时为 null。</summary>
    private string? CurrentA2ADir()
        => IsGlobal || SelectedWorkspace == null ? null : A2AWorkspaceConfigStore.ResolveDir(SelectedWorkspace.RootPath);

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
                Classes = { Tab == kind ? "dlgPrimary" : "dlgGhost" },
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
        if (Tab == SettingsTabKind.Provider || Tab == SettingsTabKind.Model || Tab == SettingsTabKind.Yunxiao)
        {
            _topBar.Children.Add(new TextBlock
            {
                Text = Tab == SettingsTabKind.Yunxiao
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

        var workspaces = _vm.Workspaces;
        var items = new List<string> { "★ 全局" };
        items.AddRange(workspaces.Select(w => string.IsNullOrWhiteSpace(w.Name) ? w.RootPath : w.Name));
        combo.ItemsSource = items;

        var currentLabel = IsGlobal ? "★ 全局"
            : (SelectedWorkspace != null ? (string.IsNullOrWhiteSpace(SelectedWorkspace.Name) ? SelectedWorkspace.RootPath : SelectedWorkspace.Name) : "★ 全局");
        combo.SelectedItem = currentLabel;

        combo.SelectionChanged += (s, e) =>
        {
            var sel = combo.SelectedItem as string;
            if (sel == "★ 全局")
                SelectedWorkspace = null;
            else
            {
                var ws = workspaces.FirstOrDefault(w => (string.IsNullOrWhiteSpace(w.Name) ? w.RootPath : w.Name) == sel);
                SelectedWorkspace = ws;
            }
            OnScopeChanged();
        };

        _topBar.Children.Add(combo);
    }

    private void OnScopeChanged()
    {
        SelectedItemKey = null;
        RefreshItems();
        BuildEditor();
    }

    // ---------------- 中栏：条目列表 ----------------

    private void RefreshItems()
    {
        if (_itemList == null) return;

        // A2A 类：服务端（server.json，若存在）+ 各客户端文件
        if (Tab == SettingsTabKind.A2A)
        {
            var list = _vm.A2a.BuildList(IsGlobal ? null : SelectedWorkspace?.RootPath);
            ApplyListSelection(list);
            UpdateActionBar();
            return;
        }

        // 供应商类：对象列表（含脱敏 ApiKey 与默认标记）
        if (Tab == SettingsTabKind.Provider)
        {
            ApplyListSelection(_vm.ProviderModel.BuildProviderList());
            UpdateActionBar();
            return;
        }

        // 模型类：对象列表（provider:model，默认加标记）
        if (Tab == SettingsTabKind.Model)
        {
            ApplyListSelection(_vm.ProviderModel.BuildModelList());
            UpdateActionBar();
            return;
        }

        // 云效类：固定单条目（外部集成连接）
        if (Tab == SettingsTabKind.Yunxiao)
        {
            _itemList.ItemsSource = new List<string> { "云效连接" };
            _itemList.SelectedItem = "云效连接";
            SelectedItemKey = "yunxiao";
            UpdateActionBar();
            return;
        }

        // 工作区配置（技能 / 规则 / MCP）
        var strs = _vm.EnumerateFileItems().ToList();
        _itemList.ItemsSource = strs;
        if (SelectedItemKey != null && strs.Contains(SelectedItemKey))
            _itemList.SelectedItem = SelectedItemKey;
        else
        {
            SelectedItemKey = strs.FirstOrDefault();
            _itemList.SelectedItem = SelectedItemKey;
        }
        UpdateActionBar();
    }

    /// <summary>
    /// 绑定对象列表并把选中项回落到「原选中项 → 首项」。
    /// </summary>
    /// <param name="list">对象列表（供应商 / 模型 / A2A）。</param>
    private void ApplyListSelection(IReadOnlyList<SettingsListItem> list)
    {
        if (_itemList == null) return;

        _itemList.ItemsSource = list;
        var first = list.FirstOrDefault();
        var match = SelectedItemKey != null ? list.FirstOrDefault(x => x.Key == SelectedItemKey) : null;
        _itemList.SelectedItem = match ?? first;
        SelectedItemKey = (match ?? first)?.Key;
    }

    private string? GetSelectedItemKey()
    {
        if (_itemList?.SelectedItem == null) return null;
        if (_itemList.SelectedItem is SettingsListItem item) return item.Key;
        return _itemList.SelectedItem as string;
    }
}