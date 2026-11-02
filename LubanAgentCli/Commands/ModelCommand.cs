/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*Author：yswenli
*命名空间：LubanAgentCli.Commands
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
namespace LubanAgentCli.Commands;

/// <summary>
/// 模型管理命令
/// </summary>
public class ModelCommand : CommandBase
{
    private readonly IProviderModelManagementService _providers;

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
    /// <param name="services">服务提供者（用于解析按用户隔离的 Provider 缓存）</param>
    public ModelCommand(ConfigManager configManager, IConfiguration configuration,
        ITuiOutputWriter writer, ITuiUiService ui, DiContainer services)
        : base(configManager, configuration, writer, ui)
    {
        _providers = services.GetRequiredService<IProviderModelManagementService>();
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
    /// 列出模型并交互式选择 Model 来确认当前使用的 LLM
    /// </summary>
    private async Task<bool> ExecuteListAsync()
    {
        var providers = await _providers.GetProvidersAsync();
        if (providers.Count == 0)
        {
            Writer.WriteInfo("暂无配置的 Provider");
            // 直接引导用户添加 Provider
            return await ExecuteAddAsync(Array.Empty<string>());
        }

        // 检查是否有当前 provider
        var (selectedProviderId, selectedModelId) = _providers.GetSelection();
        var selectedFullName = selectedProviderId is null ? null : $"{selectedProviderId}:{selectedModelId}";

        string? currentProviderName = null;
        if (selectedProviderId is not null)
        {
            currentProviderName = selectedProviderId;
            if (!providers.Any(p => p.Name == currentProviderName))
            {
                currentProviderName = null;
            }
        }

        ProviderConfig selectedProvider;

        if (currentProviderName is null)
        {
            // 没有当前 provider，需要选择
            var providerChosen = Ui.Choose("选择 Provider",
                providers.Select(p => ProviderHelper.GetDisplayName(p.Name)).ToList());

            if (providerChosen is null) return true; // 用户取消

            selectedProvider = providers[providerChosen.Value];
        }
        else
        {
            // 有当前 provider，直接使用
            selectedProvider = providers.First(p => p.Name == currentProviderName);
            Writer.WriteInfo($"当前 Provider: {ProviderHelper.GetDisplayName(currentProviderName)}");
        }

        var providerName = selectedProvider.Name;

        // 刷新该 Provider 的模型列表（容错、不阻塞）
        if (!string.IsNullOrEmpty(selectedProvider.ApiKey))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _providers.RefreshModelsAsync(providerName, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Writer.WriteInfo($"刷新 {ProviderHelper.GetDisplayName(providerName)} 模型列表超时，将使用本地预定义模型。");
            }
            catch (Exception ex)
            {
                Writer.WriteInfo($"刷新 {ProviderHelper.GetDisplayName(providerName)} 模型列表失败: {ex.Message}，将使用本地预定义模型。");
            }
        }

        // 显示该 Provider 的模型列表
        var allModels = await _providers.GetModelsAsync(providerName);

        if (allModels.Count == 0)
        {
            // 没有预定义模型，让用户手动输入
            var values = Ui.ShowForm($"{ProviderHelper.GetDisplayName(providerName)} 没有可用模型", new[]
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

            await _providers.SetSelectionAsync(providerName, modelName);
            Writer.WriteSuccess($"已确认 LLM: {ProviderHelper.GetDisplayName(providerName)} - {modelName}");
            return true;
        }

        // 显示模型列表，让用户选择（默认第一个）
        var modelChosen = Ui.Choose($"{ProviderHelper.GetDisplayName(providerName)} 可用模型（回车确认，未选则默认第一个）",
            allModels.Select(m =>
            {
                var isSelected = selectedFullName == $"{providerName}:{m}" ? " (已选)" : "";
                return $"{m}{isSelected}";
            }).ToList());

        string selectedModel;
        if (modelChosen is null)
        {
            // 用户直接关闭，使用第一个模型
            selectedModel = allModels[0];
            Writer.WriteInfo($"未选择，使用默认模型: {selectedModel}");
        }
        else
        {
            selectedModel = allModels[modelChosen.Value];
        }

        await _providers.SetSelectionAsync(providerName, selectedModel);
        Writer.WriteSuccess($"已确认 LLM: {ProviderHelper.GetDisplayName(providerName)} - {selectedModel}");

        return true;
    }

    /// <summary>
    /// 为 Provider 添加自定义模型
    /// </summary>
    /// <param name="args">可选的 provider 和 model 参数</param>
    private async Task<bool> ExecuteAddAsync(string[] args)
    {
        Writer.WriteLine();

        var providers = await _providers.GetProvidersAsync();
        if (providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return true;
        }

        string providerName;
        string modelName;

        if (args.Length >= 2)
        {
            providerName = args[0].ToLower();
            modelName = args[1];

            if (!providers.Any(p => p.Name == providerName))
            {
                Writer.WriteError($"Provider '{providerName}' 不存在");
                return true;
            }
        }
        else
        {
            // 编号菜单 → Choose 对话框
            var chosen = Ui.Choose("选择 Provider",
                providers.Select(p => ProviderHelper.GetDisplayName(p.Name)).ToList());
            if (chosen is null) return true; // 用户取消

            providerName = providers[chosen.Value].Name;

            var values = Ui.ShowForm("添加自定义模型", new[]
            {
                new FormField("模型名称")
            });
            if (values is null) return true; // 用户取消

            modelName = values[0].Trim();
        }

        if (string.IsNullOrEmpty(modelName))
        {
            Writer.WriteError("模型名称不能为空");
            return true;
        }

        try
        {
            var existing = await _providers.GetModelsAsync(providerName);
            if (existing.Contains(modelName))
            {
                Writer.WriteInfo($"模型 '{modelName}' 已存在于 {ProviderHelper.GetDisplayName(providerName)}");
                return true;
            }

            await _providers.AddModelAsync(providerName, modelName);
            Writer.WriteSuccess($"已为 {ProviderHelper.GetDisplayName(providerName)} 添加模型: {modelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return true;
    }

    /// <summary>
    /// 更新自定义模型名称
    /// </summary>
    /// <param name="args">未使用的参数</param>
    private async Task<bool> ExecuteUpdateAsync(string[] args)
    {
        Writer.WriteLine();

        var providers = await _providers.GetProvidersAsync();
        if (providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider");
            return true;
        }

        var providersWithCustom = providers.Where(p => p.CustomModels?.Count > 0).ToList();
        if (providersWithCustom.Count == 0)
        {
            Writer.WriteInfo("没有自定义模型可更新");
            return true;
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择包含自定义模型的 Provider",
            providersWithCustom.Select(p => ProviderHelper.GetDisplayName(p.Name)).ToList());
        if (chosen is null) return true; // 用户取消

        var provider = providersWithCustom[chosen.Value];

        // 编号菜单 → Choose 对话框
        var modelChosen = Ui.Choose($"{ProviderHelper.GetDisplayName(provider.Name)} 的自定义模型",
            provider.CustomModels.ToList());
        if (modelChosen is null) return true; // 用户取消

        var oldModelName = provider.CustomModels[modelChosen.Value];

        // 字段非必填（Required: false）+ 初始值为现值；清空提交仍按原逻辑报错
        var values = Ui.ShowForm($"更新模型 {oldModelName}", new[]
        {
            new FormField("新的模型名称", InitialValue: oldModelName, Required: false)
        });
        if (values is null) return true; // 用户取消

        var newModelName = values[0].Trim();

        if (string.IsNullOrEmpty(newModelName))
        {
            Writer.WriteError("模型名称不能为空");
            return true;
        }

        try
        {
            await _providers.UpdateModelAsync(provider.Name, oldModelName, newModelName);
            Writer.WriteSuccess($"已更新模型: {oldModelName} -> {newModelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return true;
    }

    /// <summary>
    /// 删除自定义模型
    /// </summary>
    /// <param name="args">未使用的参数</param>
    private async Task<bool> ExecuteDeleteAsync(string[] args)
    {
        Writer.WriteLine();

        var providers = await _providers.GetProvidersAsync();
        if (providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider");
            return true;
        }

        var providersWithCustom = providers.Where(p => p.CustomModels?.Count > 0).ToList();
        if (providersWithCustom.Count == 0)
        {
            Writer.WriteInfo("没有自定义模型可删除");
            return true;
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择包含自定义模型的 Provider",
            providersWithCustom.Select(p => ProviderHelper.GetDisplayName(p.Name)).ToList());
        if (chosen is null) return true; // 用户取消

        var provider = providersWithCustom[chosen.Value];

        // 编号菜单 → Choose 对话框
        var modelChosen = Ui.Choose($"{ProviderHelper.GetDisplayName(provider.Name)} 的自定义模型",
            provider.CustomModels.ToList());
        if (modelChosen is null) return true; // 用户取消

        var modelName = provider.CustomModels[modelChosen.Value];

        // 危险操作：默认"否"
        if (!Ui.Confirm("删除模型", $"确定要删除模型 '{modelName}' 吗？", defaultValue: false))
        {
            Writer.WriteInfo("已取消");
            return true;
        }

        try
        {
            await _providers.DeleteModelAsync(provider.Name, modelName);
            Writer.WriteSuccess($"已删除模型: {modelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return true;
    }

    /// <summary>
    /// 切换当前使用的模型
    /// </summary>
    /// <param name="args">可选的 provider:model 参数</param>
    private async Task<bool> ExecuteSwitchAsync(string[] args)
    {
        var providers = await _providers.GetProvidersAsync();
        if (providers.Count == 0)
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
            if (!providers.Any(p => p.Name == parts[0]))
            {
                Writer.WriteError($"Provider '{parts[0]}' 不存在");
                return true;
            }

            try
            {
                await _providers.SetSelectionAsync(parts[0], parts[1]);
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

        var providerList = providers.ToList();
        var (currentProviderId, currentModelId) = _providers.GetSelection();
        var selectedFullName = currentProviderId is null ? null : $"{currentProviderId}:{currentModelId}";

        // 编号菜单 → Choose 对话框
        var providerChosen = Ui.Choose("选择 Provider",
            providerList.Select(p =>
            {
                var selected = currentProviderId == p.Name ? " (当前)" : "";
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
                await _providers.RefreshModelsAsync(selectedProvider.Name, cts.Token);
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

        var allModels = await _providers.GetModelsAsync(selectedProvider.Name);

        if (allModels.Count > 0)
        {
            // 编号菜单 → Choose 对话框，末位附加"手动输入其他模型"选项
            var modelChosen = Ui.Choose($"{ProviderHelper.GetDisplayName(selectedProvider.Name)} 可用模型",
                allModels.Select(m =>
                {
                    var selected = selectedFullName == $"{selectedProvider.Name}:{m}" ? " (已选)" : "";
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
                await _providers.SetSelectionAsync(selectedProvider.Name, modelName);
                Writer.WriteSuccess($"已切换模型: {selectedProvider.Name}:{modelName}");
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
                await _providers.SetSelectionAsync(selectedProvider.Name, modelName);
                Writer.WriteSuccess($"已切换模型: {selectedProvider.Name}:{modelName}");
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
