/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： ModelCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/28
*描述：模型管理命令（支持 list/add/update/delete/switch 子命令）
*
*****************************************************************************/
using LubanAgent.App;

namespace LubanAgent.Commands;

/// <summary>
/// 模型管理命令
/// </summary>
public class ModelCommand : CommandBase
{
    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "model";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "管理模型（-list/-add/-update/-delete/-switch）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public ModelCommand(ConfigManager configManager, IConfiguration configuration,
        ITuiOutputWriter writer, ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
    }

    /// <summary>
    /// 执行命令（无参数时显示帮助）
    /// </summary>
    public override Task ExecuteAsync()
    {
        Writer.WriteLine();
        Writer.WriteHeader("模型管理命令");
        Writer.WriteLine("  model -list                 - 列出所有可用模型");
        Writer.WriteLine("  model -add                  - 为 Provider 添加自定义模型");
        Writer.WriteLine("  model -update               - 更新自定义模型名称");
        Writer.WriteLine("  model -delete               - 删除自定义模型");
        Writer.WriteLine("  model -switch [provider:model] - 切换当前使用的模型");
        Writer.WriteLine("  简写: /m -l, /m -a, /m -u, /m -d, /m -s");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行带子命令的命令
    /// </summary>
    public override Task<bool> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(false);
        }

        var subCommand = args[0].ToLower();
        return subCommand switch
        {
            "-list" or "list" => ExecuteListAsync(),
            "-add" or "add" => ExecuteAddAsync(args[1..]),
            "-update" or "update" => ExecuteUpdateAsync(args[1..]),
            "-delete" or "delete" => ExecuteDeleteAsync(args[1..]),
            "-switch" or "switch" => ExecuteSwitchAsync(args[1..]),
            _ => Task.FromResult(false)
        };
    }

    /// <summary>
    /// 列出所有可用模型
    /// </summary>
    private async Task<bool> ExecuteListAsync()
    {
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            Writer.WriteInfo("暂无配置的 Provider，请先使用 provider add 添加");
            return true;
        }

        // 先异步刷新所有 Provider 的模型列表（容错、不阻塞）
        foreach (var p in providers)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await ProviderHelper.RefreshModelsAsync(p.Name, p.ApiKey, p.BaseUrl, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Writer.WriteInfo($"刷新 {p.Name} 模型列表超时，将使用本地预定义模型。");
            }
            catch (Exception ex)
            {
                Writer.WriteInfo($"刷新 {p.Name} 模型列表失败: {ex.Message}，将使用本地预定义模型。");
            }
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var p in providers)
        {
            var displayName = ProviderHelper.GetDisplayName(p.Name);

            var allModels = ProviderHelper.GetAllModels(p.Name, p.CustomModels);
            if (allModels.Count == 0)
            {
                rows.Add(new[] { displayName, "(无可用模型)", "" });
            }
            else
            {
                foreach (var model in allModels)
                {
                    var tags = new List<string>();
                    if (ConfigManager.SelectedModel == $"{p.Name}:{model}") tags.Add("当前");
                    if (p.CustomModels?.Contains(model) == true) tags.Add("自定义");
                    rows.Add(new[] { displayName, model, string.Join(", ", tags) });
                }
            }
        }

        Ui.ShowTable("所有可用模型", new[] { "Provider", "模型", "备注" }, rows);

        if (!string.IsNullOrEmpty(ConfigManager.SelectedModel))
        {
            Writer.WriteSuccess($"当前选择的模型: {ConfigManager.SelectedModel}");
        }
        else
        {
            Writer.WriteInfo("当前未选择模型，请使用 model switch 选择");
        }

        return true;
    }

    /// <summary>
    /// 为 Provider 添加自定义模型
    /// </summary>
    /// <param name="args">可选的 provider 和 model 参数</param>
    private Task<bool> ExecuteAddAsync(string[] args)
    {
        Writer.WriteLine();

        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return Task.FromResult(true);
        }

        string providerName;
        string modelName;

        if (args.Length >= 2)
        {
            providerName = args[0].ToLower();
            modelName = args[1];

            if (!ConfigManager.HasProvider(providerName))
            {
                Writer.WriteError($"Provider '{providerName}' 不存在");
                return Task.FromResult(true);
            }
        }
        else
        {
            // 编号菜单 → Choose 对话框
            var chosen = Ui.Choose("选择 Provider",
                providers.Select(p => ProviderHelper.GetDisplayName(p.Name)).ToList());
            if (chosen is null) return Task.FromResult(true); // 用户取消

            providerName = providers[chosen.Value].Name;

            var values = Ui.ShowForm("添加自定义模型", new[]
            {
                new FormField("模型名称")
            });
            if (values is null) return Task.FromResult(true); // 用户取消

            modelName = values[0].Trim();
        }

        if (string.IsNullOrEmpty(modelName))
        {
            Writer.WriteError("模型名称不能为空");
            return Task.FromResult(true);
        }

        try
        {
            var provider = ConfigManager.GetProvider(providerName);
            var existing = ProviderHelper.GetAllModels(providerName, provider?.CustomModels);
            if (existing.Contains(modelName))
            {
                Writer.WriteInfo($"模型 '{modelName}' 已存在于 {ProviderHelper.GetDisplayName(providerName)}");
                return Task.FromResult(true);
            }

            ConfigManager.AddCustomModel(providerName, modelName);
            Writer.WriteSuccess($"已为 {ProviderHelper.GetDisplayName(providerName)} 添加模型: {modelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 更新自定义模型名称
    /// </summary>
    /// <param name="args">未使用的参数</param>
    private Task<bool> ExecuteUpdateAsync(string[] args)
    {
        Writer.WriteLine();

        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider");
            return Task.FromResult(true);
        }

        var providersWithCustom = providers.Where(p => p.CustomModels?.Count > 0).ToList();
        if (providersWithCustom.Count == 0)
        {
            Writer.WriteInfo("没有自定义模型可更新");
            return Task.FromResult(true);
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择包含自定义模型的 Provider",
            providersWithCustom.Select(p => ProviderHelper.GetDisplayName(p.Name)).ToList());
        if (chosen is null) return Task.FromResult(true); // 用户取消

        var provider = providersWithCustom[chosen.Value];

        // 编号菜单 → Choose 对话框
        var modelChosen = Ui.Choose($"{ProviderHelper.GetDisplayName(provider.Name)} 的自定义模型",
            provider.CustomModels.ToList());
        if (modelChosen is null) return Task.FromResult(true); // 用户取消

        var oldModelName = provider.CustomModels[modelChosen.Value];

        // 字段非必填（Required: false）+ 初始值为现值；清空提交仍按原逻辑报错
        var values = Ui.ShowForm($"更新模型 {oldModelName}", new[]
        {
            new FormField("新的模型名称", InitialValue: oldModelName, Required: false)
        });
        if (values is null) return Task.FromResult(true); // 用户取消

        var newModelName = values[0].Trim();

        if (string.IsNullOrEmpty(newModelName))
        {
            Writer.WriteError("模型名称不能为空");
            return Task.FromResult(true);
        }

        try
        {
            ConfigManager.UpdateCustomModel(provider.Name, oldModelName, newModelName);
            Writer.WriteSuccess($"已更新模型: {oldModelName} -> {newModelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 删除自定义模型
    /// </summary>
    /// <param name="args">未使用的参数</param>
    private Task<bool> ExecuteDeleteAsync(string[] args)
    {
        Writer.WriteLine();

        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider");
            return Task.FromResult(true);
        }

        var providersWithCustom = providers.Where(p => p.CustomModels?.Count > 0).ToList();
        if (providersWithCustom.Count == 0)
        {
            Writer.WriteInfo("没有自定义模型可删除");
            return Task.FromResult(true);
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择包含自定义模型的 Provider",
            providersWithCustom.Select(p => ProviderHelper.GetDisplayName(p.Name)).ToList());
        if (chosen is null) return Task.FromResult(true); // 用户取消

        var provider = providersWithCustom[chosen.Value];

        // 编号菜单 → Choose 对话框
        var modelChosen = Ui.Choose($"{ProviderHelper.GetDisplayName(provider.Name)} 的自定义模型",
            provider.CustomModels.ToList());
        if (modelChosen is null) return Task.FromResult(true); // 用户取消

        var modelName = provider.CustomModels[modelChosen.Value];

        // 危险操作：默认"否"
        if (!Ui.Confirm("删除模型", $"确定要删除模型 '{modelName}' 吗？", defaultValue: false))
        {
            Writer.WriteInfo("已取消");
            return Task.FromResult(true);
        }

        try
        {
            ConfigManager.RemoveCustomModel(provider.Name, modelName);
            Writer.WriteSuccess($"已删除模型: {modelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 切换当前使用的模型
    /// </summary>
    /// <param name="args">可选的 provider:model 参数</param>
    private async Task<bool> ExecuteSwitchAsync(string[] args)
    {
        if (ConfigManager.Providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return true;
        }

        if (args.Length > 0)
        {
            var modelId = string.Join(' ', args);
            if (!modelId.Contains(':'))
            {
                Writer.WriteError("模型格式错误，应为 provider:model，例如 openai:gpt-4o");
                return true;
            }

            var parts = modelId.Split(':', 2);
            if (!ConfigManager.HasProvider(parts[0]))
            {
                Writer.WriteError($"Provider '{parts[0]}' 不存在");
                return true;
            }

            try
            {
                ConfigManager.SetSelectedModel(modelId);
                Writer.WriteSuccess($"已切换模型: {modelId}");
            }
            catch (Exception ex)
            {
                Logger.Error("ModelCommand 操作异常", ex);
                Writer.WriteError(ex.Message);
            }

            return true;
        }

        Writer.WriteLine();

        var providerList = ConfigManager.Providers.ToList();

        // 编号菜单 → Choose 对话框
        var providerChosen = Ui.Choose("选择 Provider",
            providerList.Select(p =>
            {
                var selected = ConfigManager.SelectedModel?.StartsWith(p.Name + ":") == true ? " (当前)" : "";
                return $"{ProviderHelper.GetDisplayName(p.Name)}{selected}";
            }).ToList());
        if (providerChosen is null) return true; // 用户取消

        var selectedProvider = providerList[providerChosen.Value];

        // 先刷新该 Provider 的模型列表（容错、不阻塞）
        if (!string.IsNullOrEmpty(selectedProvider.ApiKey))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await ProviderHelper.RefreshModelsAsync(selectedProvider.Name, selectedProvider.ApiKey, selectedProvider.BaseUrl, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Writer.WriteInfo($"刷新 {ProviderHelper.GetDisplayName(selectedProvider.Name)} 模型列表超时，将使用本地预定义模型。");
            }
            catch (Exception ex)
            {
                Writer.WriteInfo($"刷新 {ProviderHelper.GetDisplayName(selectedProvider.Name)} 模型列表失败: {ex.Message}，将使用本地预定义模型。");
            }
        }

        var allModels = ProviderHelper.GetAllModels(selectedProvider.Name, selectedProvider.CustomModels);

        if (allModels.Count > 0)
        {
            // 编号菜单 → Choose 对话框，末位附加"手动输入其他模型"选项
            var modelChosen = Ui.Choose($"{ProviderHelper.GetDisplayName(selectedProvider.Name)} 可用模型",
                allModels.Select(m =>
                {
                    var selected = ConfigManager.SelectedModel == $"{selectedProvider.Name}:{m}" ? " (已选)" : "";
                    return $"{m}{selected}";
                }).Append("手动输入其他模型").ToList());
            if (modelChosen is null) return true; // 用户取消

            string modelName;
            if (modelChosen.Value == allModels.Count)
            {
                var values = Ui.ShowForm("手动输入模型", new[]
                {
                    new FormField("模型名称")
                });
                if (values is null) return true; // 用户取消

                modelName = values[0].Trim();
            }
            else
            {
                modelName = allModels[modelChosen.Value];
            }

            if (string.IsNullOrEmpty(modelName))
            {
                Writer.WriteError("模型名称不能为空");
                return true;
            }

            try
            {
                var fullModel = $"{selectedProvider.Name}:{modelName}";
                ConfigManager.SetSelectedModel(fullModel);
                Writer.WriteSuccess($"已切换模型: {fullModel}");
            }
            catch (Exception ex)
            {
                Logger.Error("ModelCommand 操作异常", ex);
                Writer.WriteError(ex.Message);
            }
        }
        else
        {
            var values = Ui.ShowForm($"{ProviderHelper.GetDisplayName(selectedProvider.Name)} 没有预定义模型", new[]
            {
                new FormField("请输入模型名称")
            });
            if (values is null) return true; // 用户取消

            var modelName = values[0].Trim();

            if (string.IsNullOrEmpty(modelName))
            {
                Writer.WriteError("模型名称不能为空");
                return true;
            }

            try
            {
                var fullModel = $"{selectedProvider.Name}:{modelName}";
                ConfigManager.SetSelectedModel(fullModel);
                Writer.WriteSuccess($"已切换模型: {fullModel}");
            }
            catch (Exception ex)
            {
                Logger.Error("ModelCommand 操作异常", ex);
                Writer.WriteError(ex.Message);
            }
        }

        return true;
    }
}
