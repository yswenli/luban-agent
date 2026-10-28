/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SettingsWindow.Actions
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗的动作部分（Task 19 从 SettingsWindow.axaml.cs 拆出）。
*      表单由窗口采集为 record，业务与全部提示文案由 ViewModels.Settings 产出。
*
*****************************************************************************/

namespace LubanAgentCodex.Views;

/// <summary>
/// 设置窗口（操作命令部分）：承担新建、保存、删除、导入导出等动作，
/// 表单值由窗口采集后交给 ViewModels.Settings 执行，提示文案也由该层产出。
/// </summary>
public partial class SettingsWindow
{
    // ---------------- 新建 ----------------

    private void NewItem()
    {
        // 云效：固定单条目，无需新建（按钮已隐藏，此处兜底）
        if (Tab == SettingsTabKind.Yunxiao) return;

        // A2A：服务端文件（若不存在）或新客户端文件
        if (Tab == SettingsTabKind.A2A)
        {
            if (IsGlobal || SelectedWorkspace == null)
            {
                SetHint("A2A 配置仅作用于工作区，请先在顶栏选择一个工作区。", true);
                return;
            }

            var a2a = _vm.A2a.CreateDefault(SelectedWorkspace.RootPath);
            if (!a2a.Ok) { SetHint(a2a.Message, true); return; }

            SelectedItemKey = a2a.Key;
            RefreshItems();
            BuildEditor();
            SetHint(a2a.Message, false);
            return;
        }

        // 供应商：创建草稿（占位 Name），待编辑器填写后保存
        if (Tab == SettingsTabKind.Provider)
        {
            var draft = _vm.ProviderModel.CreateProviderDraft();
            if (!draft.Ok) { SetHint(draft.Message, true); return; }

            SelectedItemKey = draft.Key;
            RefreshItems();
            BuildEditor();
            SetHint(draft.Message, false);
            return;
        }

        // 模型：无独立新建入口，使用右侧内联「新建自定义模型」
        if (Tab == SettingsTabKind.Model)
        {
            SetHint("请使用右侧编辑器的「新建自定义模型」：选择供应商并填写模型名。", true);
            return;
        }

        // 工作区配置（技能 / 规则 / MCP）
        var created = Tab == SettingsTabKind.Mcp
            ? _vm.Mcp.CreateDefault(ScopeRoot)
            : _vm.SkillRule.CreateDefault(Tab, ScopeRoot);

        if (!created.Ok) { SetHint(created.Message, true); return; }

        SelectedItemKey = created.Key;
        RefreshItems();
        BuildEditor();
        SetHint(created.Message, false);
    }

    // ---------------- 保存 ----------------

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedItemKey)) return;

        try
        {
            switch (Tab)
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

    private Task SaveSkillAsync()
    {
        var form = new SkillForm(
            GetText("name"), GetText("description"), GetText("category"), GetText("triggers"), GetText("body"));

        var saved = _vm.SkillRule.SaveSkill(SelectedItemKey, form, ScopeRoot);
        if (!saved.Ok) { SetHint(saved.Message, true); return Task.CompletedTask; }

        SelectedItemKey = saved.Key;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
        return Task.CompletedTask;
    }

    private Task SaveRuleAsync()
    {
        var form = new RuleForm(
            GetText("Id"),
            GetText("Name"),
            GetText("Description"),
            GetText("ActionTypePattern"),
            GetText("TargetPattern"),
            GetText("Action"),
            GetText("Priority"),
            GetToggle("Enabled"),
            GetText("Content"));

        var saved = _vm.SkillRule.SaveRule(SelectedItemKey, form, ScopeRoot);
        if (!saved.Ok) { SetHint(saved.Message, true); return Task.CompletedTask; }

        SelectedItemKey = saved.Key;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
        return Task.CompletedTask;
    }

    private Task SaveMcpAsync()
    {
        var form = new McpForm(
            GetText("Name"),
            GetText("Description"),
            GetCombo("Transport"),
            GetText("Command"),
            GetText("Args"),
            GetToggle("Enabled"));

        var saved = _vm.Mcp.Save(SelectedItemKey, form, ScopeRoot);
        if (!saved.Ok) { SetHint(saved.Message, true); return Task.CompletedTask; }

        SelectedItemKey = saved.Key;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
        return Task.CompletedTask;
    }

    private async Task SaveA2AAsync()
    {
        if (IsGlobal || SelectedWorkspace == null)
        {
            SetHint("A2A 配置仅作用于工作区。", true);
            return;
        }

        var root = SelectedWorkspace.RootPath;
        var key = SelectedItemKey ?? "";

        if (key == "server")
        {
            var serverForm = new A2aServerForm(
                GetText("Host"),
                GetText("Port"),
                GetText("Path"),
                GetToggle("RequireApiKey"),
                GetText("ApiKeys"),
                GetText("AgentName"),
                GetText("AgentDescription"),
                GetText("AgentVersion"));

            var saved = _vm.A2a.SaveServer(root, serverForm);
            if (!saved.Ok) { SetHint(saved.Message, true); return; }

            SelectedItemKey = saved.Key;
            RefreshItems();
            BuildEditor();
            SetSavedHint();
            return;
        }

        // 客户端文件
        var clientForm = new A2aClientForm(
            GetText("Name"),
            GetText("Endpoint"),
            GetText("ApiKey"),
            GetToggle("RegisterAsSubAgent"),
            GetText("RemoteAgentName"),
            GetToggle("AllowAll"));

        var result = _vm.A2a.SaveClient(root, key, clientForm);
        if (!result.Ok) { SetHint(result.Message, true); return; }

        SelectedItemKey = result.Key;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
        await Task.CompletedTask;
    }

    private Task SaveProviderAsync()
    {
        if (!_vm.ProviderModel.Available)
        {
            SetHint("配置管理器不可用，无法保存。", true);
            return Task.CompletedTask;
        }

        var form = new ProviderForm(
            GetText("Name"),
            GetText("ApiKey"),
            GetText("BaseUrl"),
            GetText("DisplayName"),
            GetText("NetworkTimeoutSeconds"),
            CollectCustomModels());

        var saved = _vm.ProviderModel.SaveProvider(form);
        if (!saved.Ok) { SetHint(saved.Message, true); return Task.CompletedTask; }

        SelectedItemKey = saved.Key;
        RefreshItems();
        BuildEditor();
        SetSavedHint();
        return Task.CompletedTask;
    }

    private async Task SaveYunxiaoAsync()
    {
        if (!_vm.Yunxiao.Available)
        {
            SetHint("云效设置服务不可用，无法保存。", true);
            return;
        }

        var saved = await _vm.Yunxiao.SaveAsync(GetText("YunxiaoOrganizationId"), GetText("YunxiaoPat"));
        if (!saved.Ok) { SetHint(saved.Message, true); return; }

        BuildEditor();
        SetHint(saved.Message, false);
    }

    // ---------------- 删除 ----------------

    private async Task DeleteItemAsync()
    {
        var key = SelectedItemKey;
        if (string.IsNullOrEmpty(key)) return;

        // 云效：固定单条目，不可删除（按钮已隐藏，此处兜底）
        if (Tab == SettingsTabKind.Yunxiao) return;

        if (Tab == SettingsTabKind.A2A)
        {
            if (IsGlobal || SelectedWorkspace == null) return;
            var root = SelectedWorkspace.RootPath;
            var isServer = key == "server";
            var ok = await Dialogs.ShowConfirmAsync(this, "确认删除",
                isServer ? "确定删除服务端配置 server.json 吗？" : $"确定删除客户端配置 {key} 吗？",
                "删除后该角色将不再启用，且不可恢复。",
                "确定删除", danger: true);
            if (!ok) return;

            var deleted = _vm.A2a.Delete(root, key);
            if (!deleted.Ok) { SetHint(deleted.Message, true); return; }

            SelectedItemKey = null;
            RefreshItems();
            BuildEditor();
            SetHint(deleted.Message, false);
            return;
        }

        if (Tab == SettingsTabKind.Provider)
        {
            if (!_vm.ProviderModel.Available) return;
            var provider = _vm.ProviderModel.GetProvider(key);
            if (provider == null) return;

            var ok = await Dialogs.ShowConfirmAsync(this, "确认删除",
                $"确定删除供应商 \"{key}\" 吗？",
                "该供应商及其配置将被直接删除，且不可恢复。",
                "确定删除", danger: true);
            if (!ok) return;

            var deleted = _vm.ProviderModel.DeleteProvider(key);
            if (!deleted.Ok) { SetHint(deleted.Message, true); return; }

            SelectedItemKey = null;
            RefreshItems();
            BuildEditor();
            SetHint(deleted.Message, false);
            return;
        }

        if (Tab == SettingsTabKind.Model)
        {
            SetHint("模型删除请使用右侧编辑器的「删除模型」按钮。", true);
            return;
        }

        // 工作区配置（技能 / 规则 / MCP）
        var confirm = await Dialogs.ShowConfirmAsync(this, "确认删除",
            $"确定要删除条目 \"{key}\" 吗？",
            "该条目对应的文件或目录将被直接删除，且不可恢复。",
            "确定删除", danger: true);
        if (!confirm) return;

        var removed = Tab == SettingsTabKind.Mcp
            ? await _vm.Mcp.DeleteAsync(key, ScopeRoot)
            : await _vm.SkillRule.DeleteAsync(Tab, key, ScopeRoot);

        if (!removed.Ok) { SetHint(removed.Message, true); return; }

        SelectedItemKey = null;
        RefreshItems();
        BuildEditor();
        SetHint(removed.Message, false);
    }

    // ---------------- 应用配置 / 动作栏 ----------------

    /// <summary>
    /// 「应用配置」：重置 Agent 宿主，使其在下次对话时按最新配置加载。
    /// 供应商/模型为全局配置，恒可应用。
    /// </summary>
    private void ApplyConfig()
    {
        try
        {
            var host = _services!.GetRequiredService<IAgentHost>();
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
        if (Tab == SettingsTabKind.Provider || Tab == SettingsTabKind.Model) return true;
        if (Tab == SettingsTabKind.A2A) return false;
        // 云效：保存即写入共享选项与凭据存储、即时生效，无需「应用配置」触发工作区回放
        if (Tab == SettingsTabKind.Yunxiao) return false;
        if (IsGlobal) return true;

        try
        {
            var current = _services!.GetRequiredService<IWorkspaceManager>().CurrentWorkspace;
            return current != null && SelectedWorkspace != null
                && current.WorkspaceId == SelectedWorkspace.WorkspaceId;
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

        if (Tab == SettingsTabKind.Model)
        {
            // 模型：内联操作，隐藏新建 / 删除 / 保存
            if (_newItemBtn != null) _newItemBtn.IsVisible = false;
            if (_deleteItemBtn != null) _deleteItemBtn.IsVisible = false;
            if (_saveBtn != null) _saveBtn.IsVisible = false;
        }
        else if (Tab == SettingsTabKind.Yunxiao)
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
                _deleteItemBtn.IsEnabled = !string.IsNullOrEmpty(SelectedItemKey);
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
}