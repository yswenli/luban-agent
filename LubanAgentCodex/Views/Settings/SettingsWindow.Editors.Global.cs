/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SettingsWindow.Editors.Global
*版本号： V1.0.0.0
*唯一标识：统一设置中心（供应商 / 模型 / A2A / 云效 编辑器）
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗右栏编辑器的全局配置与 A2A 部分（Task 19 从 SettingsWindow.axaml.cs 拆出）。
*      控件构建顺序、标签文案、默认值与改动前逐字一致。
*
*****************************************************************************/
using LuBan.AIAgent.Configuration.Providers;

namespace LubanAgentCodex.Views;

public partial class SettingsWindow
{
    private void BuildProviderEditor(StackPanel host, string key)
    {
        if (!_vm.ProviderModel.Available) { SetHint("配置管理器不可用，无法编辑供应商。", true); return; }
        var provider = _vm.ProviderModel.GetProvider(key);
        var isNew = provider == null;
        if (isNew) provider = new ProviderConfig { Name = key };
        var nonNullProvider = provider!;

        // 类型（仅新建态显示，便捷预填 Name / BaseUrl）
        ComboBox? typeCombo = null;
        if (isNew)
        {
            var presets = _vm.ProviderModel.PresetDisplayNames;
            typeCombo = AddCombo(host, "类型（内置预设，可选）", presets, presets[0]);
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
            var modelCombo = AddCombo(host, "选择模型", _vm.ProviderModel.GetAllModels(nonNullProvider.Name), "");
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
                var applied = _vm.ProviderModel.SetDefaultForProvider(nonNullProvider.Name, m);
                SetHint(applied.Message, !applied.Ok);
                if (applied.Ok) RefreshItems();
            };
            host.Children.Add(setDefaultBtn);
        }
    }

    private void BuildModelEditor(StackPanel host, string key)
    {
        if (!_vm.ProviderModel.Available) { SetHint("配置管理器不可用，无法编辑模型。", true); return; }
        var parts = key.Split(':', 2);
        var pName = parts[0];
        var mName = parts.Length > 1 ? parts[1] : "";
        var provider = _vm.ProviderModel.GetProvider(pName);
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
                var renamed = _vm.ProviderModel.RenameModel(pName, mName, GetText("ModelName"));
                if (!renamed.Ok) { SetHint(renamed.Message, true); return; }

                SelectedItemKey = renamed.Key;
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
                _vm.ProviderModel.DeleteModel(pName, mName);
                SelectedItemKey = null;
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
            var applied = _vm.ProviderModel.SetDefaultModel(key);
            SetHint(applied.Message, !applied.Ok);
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
        var pCombo = AddCombo(host, "选择供应商", _vm.ProviderModel.GetProviderNames(), pName);
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
            var added = _vm.ProviderModel.AddModel(pCombo.SelectedItem as string, GetText("NewModelName"));
            if (!added.Ok) { SetHint(added.Message, true); return; }

            SelectedItemKey = added.Key;
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
        if (IsGlobal || SelectedWorkspace == null)
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

        var root = SelectedWorkspace.RootPath;

        if (key == "server")
        {
            var cfg = _vm.A2a.ReadServer(root) ?? new A2AWorkspaceFile();
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
        var client = _vm.A2a.ReadClient(root, key)
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

        if (!_vm.Yunxiao.Available)
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

        var organizationId = _vm.Yunxiao.GetOrganizationId();
        var hasPat = _vm.Yunxiao.HasPat();

        SetField("YunxiaoOrganizationId",
            AddField(host, "组织 ID（云效企业标识，如 60d1xxxxxx）", organizationId));
        SetField("YunxiaoPat",
            AddPasswordField(host, hasPat
                ? "个人访问令牌 PAT（已配置；留空表示不修改）"
                : "个人访问令牌 PAT（尚未配置）", ""));

        host.Children.Add(new TextBlock
        {
            Text = "PAT 仅写入凭据存储，页面不回显明文；也可用环境变量 LUBAN_YUNXIAO_PAT 兜底。"
                 + "插件若要在出口调用云效接口，须在清单中声明 credential: [\"connector:yunxiao:token\"] 与对应网络白名单。",
            FontSize = 10,
            Foreground = Brush.Parse("#8A8A8A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });
    }

    // ---------------- 供应商 / 模型编辑器辅助 ----------------

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
}