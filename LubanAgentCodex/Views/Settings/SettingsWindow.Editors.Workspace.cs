/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SettingsWindow.Editors.Workspace
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗右栏编辑器的工作区配置部分（Task 19 从 SettingsWindow.axaml.cs 拆出）。
*      控件构建顺序、标签文案、默认值与改动前逐字一致。
*
*****************************************************************************/

namespace LubanAgentCodex.Views;

/// <summary>
/// 设置窗口（编辑器-工作区部分）：按当前页签把技能、规则、MCP、连接器等
/// 工作区级配置分发到对应的右栏编辑表单。
/// </summary>
public partial class SettingsWindow
{
    /// <summary>
    /// 按页签分发到具体编辑器。
    /// </summary>
    /// <param name="host">编辑器容器。</param>
    /// <param name="kind">当前页签。</param>
    /// <param name="key">条目键。</param>
    private void BuildTabEditor(StackPanel host, SettingsTabKind kind, string key)
    {
        switch (kind)
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

    private void BuildSkillEditor(StackPanel host, string key)
    {
        var content = _vm.SkillRule.ReadSkill(key, ScopeRoot);

        SetField("name", AddField(host, "name（必填）",
            string.IsNullOrWhiteSpace(content?.Name) ? key : content!.Name));
        SetField("description", AddField(host, "description（必填）", content?.Description ?? ""));
        SetField("category", AddField(host, "category",
            string.IsNullOrWhiteSpace(content?.Category) ? "custom" : content!.Category));
        SetField("triggers", AddField(host, "triggers（逗号分隔，可选）", content?.Triggers ?? ""));
        SetField("body", AddField(host, "正文 Markdown", content?.Body ?? "", multiline: true, height: 220));
    }

    private void BuildRuleEditor(StackPanel host, string key)
    {
        var cfg = _vm.SkillRule.ReadRule(key, ScopeRoot) ?? new CustomRuleConfig { Id = key };

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
        if (!_vm.Mcp.Available) throw new InvalidOperationException("MCP 管理服务不可用");

        var cfg = _vm.Mcp.Read(key, ScopeRoot) ?? new McpServerConfig { Name = key };

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
}