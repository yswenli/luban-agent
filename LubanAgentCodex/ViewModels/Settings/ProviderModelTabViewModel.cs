/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.ViewModels.Settings
*文件名： ProviderModelTabViewModel
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗「供应商 / 模型」页业务（列表、保存、默认模型、自定义模型增删改）
*      注：本页仍直连 ConfigManager（Task 19 决策 P1）——Keep 现有行为与文案不变；
*      模型页的即时操作语义亦保持不变（无需「保存」按钮）。
*
*****************************************************************************/

namespace LubanAgentCodex.ViewModels.Settings;

/// <summary>
/// 供应商 / 模型页 ViewModel：直连 <see cref="ConfigManager"/>，不引入任何 UI 类型。
/// </summary>
/// <param name="services">宿主服务提供者（为 null 时视为容器不可用，操作返回失败文案）。</param>
public sealed class ProviderModelTabViewModel(IServiceProvider? services)
{
    private ConfigManager? Config => services?.GetService<ConfigManager>();

    /// <summary>容器是否可用（供应商 / 模型页编辑前置条件）。</summary>
    public bool Available => Config != null;

    /// <summary>当前选中的模型（<c>provider:model</c>，未选为 null）。</summary>
    public string? SelectedModel => Config?.SelectedModel;

    /// <summary>
    /// 内置供应商预设显示名（新建态预填用）。
    /// </summary>
    public IReadOnlyList<string> PresetDisplayNames { get; } =
        ProviderCatalog.All.Select(b => b.DisplayName).ToList();

    /// <summary>
    /// 供应商列表（含脱敏 ApiKey 与默认标记）。
    /// </summary>
    public IReadOnlyList<SettingsListItem> BuildProviderList()
        => (Config?.Providers ?? [])
            .Select(p => new SettingsListItem
            {
                Key = p.Name,
                Display = $"{p.Name}   {(string.IsNullOrEmpty(p.ApiKey) ? "" : SecretMasker.MaskApiKey(p.ApiKey))}" +
                          (Config?.SelectedModel?.StartsWith(p.Name + ":") == true ? "   ✓默认" : ""),
            })
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 模型列表（跨供应商，<c>provider:model</c>，默认项加标记）。
    /// </summary>
    public IReadOnlyList<SettingsListItem> BuildModelList()
    {
        var config = Config;
        if (config == null) return [];

        var list = new List<SettingsListItem>();
        foreach (var p in config.Providers)
            foreach (var m in config.GetAllModels(p.Name))
            {
                var key = $"{p.Name}:{m}";
                list.Add(new SettingsListItem
                {
                    Key = key,
                    Display = key + (config.SelectedModel == key ? "   ✓默认" : ""),
                });
            }

        return list.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>全部供应商名（模型页「选择供应商」下拉用）。</summary>
    public IReadOnlyList<string> GetProviderNames()
        => (Config?.Providers ?? []).Select(p => p.Name).ToList();

    /// <summary>指定供应商的可用模型（内置 + 远端缓存 + 自定义）。</summary>
    /// <param name="providerName">供应商名。</param>
    public IReadOnlyList<string> GetAllModels(string providerName)
        => Config?.GetAllModels(providerName) ?? [];

    /// <summary>读取供应商配置；不存在返回 null。</summary>
    /// <param name="name">供应商名。</param>
    public ProviderConfig? GetProvider(string name) => Config?.GetProvider(name);

    /// <summary>供应商是否已存在。</summary>
    /// <param name="name">供应商名。</param>
    public bool HasProvider(string name) => Config?.HasProvider(name) == true;

    /// <summary>
    /// 新建供应商草稿：返回一个未被占用的占位名。
    /// </summary>
    public SettingsOutcome CreateProviderDraft()
    {
        var config = Config;
        if (config == null)
            return SettingsOutcome.Failure("配置管理器不可用，无法新建供应商。");

        var name = "new-provider";
        var i = 1;
        while (config.HasProvider(name)) name = $"new-provider-{i++}";

        return SettingsOutcome.Success("已新建供应商草稿，请填写 Name / ApiKey 后点击「保存」。", name);
    }

    /// <summary>
    /// 保存供应商：同名只更新 ApiKey / BaseUrl，再落 DisplayName / 超时与自定义模型差异。
    /// </summary>
    /// <param name="form">表单内容。</param>
    public SettingsOutcome SaveProvider(ProviderForm form)
    {
        var config = Config;
        if (config == null) return SettingsOutcome.Failure("配置管理器不可用，无法保存。");

        var name = form.Name.Trim().ToLowerInvariant();
        var apiKey = form.ApiKey.Trim();
        if (string.IsNullOrEmpty(name)) return SettingsOutcome.Failure("Name 不能为空。");
        if (string.IsNullOrEmpty(apiKey)) return SettingsOutcome.Failure("ApiKey 不能为空。");

        var baseUrl = string.IsNullOrWhiteSpace(form.BaseUrl) ? null : form.BaseUrl.Trim();

        // upsert（仅更新 ApiKey / BaseUrl，保留 DisplayName / CustomModels 等）
        config.AddProvider(name, apiKey, baseUrl);
        var provider = config.GetProvider(name);
        if (provider == null) return SettingsOutcome.Failure("保存失败：Provider 不存在。");

        provider.DisplayName = string.IsNullOrEmpty(form.DisplayName.Trim()) ? null : form.DisplayName.Trim();
        provider.NetworkTimeoutSeconds =
            int.TryParse(form.NetworkTimeoutSecondsText.Trim(), out var timeout) && timeout > 0 ? timeout : null;

        // 自定义模型差异更新
        var current = provider.CustomModels.ToList();
        foreach (var m in form.CustomModels.Where(m => !current.Contains(m)))
            config.AddCustomModel(name, m);
        foreach (var m in current.Where(m => !form.CustomModels.Contains(m)))
            config.RemoveCustomModel(name, m);

        config.Save();

        return SettingsOutcome.Success(key: name);
    }

    /// <summary>
    /// 删除供应商；若当前默认模型属于该供应商则一并清除选择。
    /// </summary>
    /// <param name="name">供应商名。</param>
    public SettingsOutcome DeleteProvider(string name)
    {
        var config = Config;
        if (config == null) return SettingsOutcome.Failure("配置管理器不可用，无法删除。");

        var provider = config.GetProvider(name);
        if (provider == null) return SettingsOutcome.Success("已删除该供应商。");

        config.Providers.Remove(provider);
        config.Save();
        if (config.SelectedModel?.StartsWith($"{name}:", StringComparison.Ordinal) == true)
            config.ClearSelectedModel();

        return SettingsOutcome.Success("已删除该供应商。");
    }

    /// <summary>
    /// 模型页「设为默认」。
    /// </summary>
    /// <param name="key"><c>provider:model</c>。</param>
    public SettingsOutcome SetDefaultModel(string key)
    {
        var config = Config;
        if (config == null) return SettingsOutcome.Failure("配置管理器不可用。");

        config.SetSelectedModel(key);
        return SettingsOutcome.Success($"已设为默认：{key}", key);
    }

    /// <summary>
    /// 供应商页「设为默认模型」。
    /// </summary>
    /// <param name="providerName">供应商名。</param>
    /// <param name="modelName">模型名（可为空，空时返回提示）。</param>
    public SettingsOutcome SetDefaultForProvider(string providerName, string? modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return SettingsOutcome.Failure("请先选择模型。");

        var config = Config;
        if (config == null) return SettingsOutcome.Failure("配置管理器不可用。");

        var key = $"{providerName}:{modelName}";
        config.SetSelectedModel(key);
        return SettingsOutcome.Success($"已设为默认模型：{key}", key);
    }

    /// <summary>
    /// 重命名自定义模型。
    /// </summary>
    /// <param name="providerName">供应商名。</param>
    /// <param name="oldModelName">原模型名。</param>
    /// <param name="newModelName">新模型名。</param>
    public SettingsOutcome RenameModel(string providerName, string oldModelName, string newModelName)
    {
        var name = newModelName.Trim();
        if (string.IsNullOrEmpty(name)) return SettingsOutcome.Failure("名称不能为空。");

        var config = Config;
        if (config == null) return SettingsOutcome.Failure("配置管理器不可用。");

        config.UpdateCustomModel(providerName, oldModelName, name);
        var key = $"{providerName}:{name}";
        return SettingsOutcome.Success("已改名。", key);
    }

    /// <summary>
    /// 删除自定义模型。
    /// </summary>
    /// <param name="providerName">供应商名。</param>
    /// <param name="modelName">模型名。</param>
    public SettingsOutcome DeleteModel(string providerName, string modelName)
    {
        var config = Config;
        if (config == null) return SettingsOutcome.Failure("配置管理器不可用。");

        config.RemoveCustomModel(providerName, modelName);
        return SettingsOutcome.Success("已删除自定义模型。");
    }

    /// <summary>
    /// 新增自定义模型。
    /// </summary>
    /// <param name="providerName">供应商名。</param>
    /// <param name="modelName">模型名。</param>
    public SettingsOutcome AddModel(string? providerName, string modelName)
    {
        var name = modelName.Trim();
        if (string.IsNullOrEmpty(providerName) || string.IsNullOrEmpty(name))
            return SettingsOutcome.Failure("请选择供应商并填写模型名。");

        var config = Config;
        if (config == null) return SettingsOutcome.Failure("配置管理器不可用。");

        try
        {
            config.AddCustomModel(providerName, name);
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"添加失败：{ex.Message}");
        }

        return SettingsOutcome.Success("已添加自定义模型。", $"{providerName}:{name}");
    }
}