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
namespace LubanAgent.Commands;

/// <summary>
/// 模型管理命令
/// </summary>
public class ModelCommand : CommandBase
{
    public override string Name => "model";

    public override string Description => "管理模型（-list/-add/-update/-delete/-switch）";

    public ModelCommand(ConfigManager configManager, IConfiguration configuration)
        : base(configManager, configuration)
    {
    }

    public override Task ExecuteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("模型管理命令:");
        Console.WriteLine("  model -list                 - 列出所有可用模型");
        Console.WriteLine("  model -add                  - 为 Provider 添加自定义模型");
        Console.WriteLine("  model -update               - 更新自定义模型名称");
        Console.WriteLine("  model -delete               - 删除自定义模型");
        Console.WriteLine("  model -switch [provider:model] - 切换当前使用的模型");
        Console.WriteLine("  简写: /m -l, /m -a, /m -u, /m -d, /m -s");
        return Task.CompletedTask;
    }

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

    private async Task<bool> ExecuteListAsync()
    {
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            WriteInfo("暂无配置的 Provider，请先使用 provider add 添加");
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
                WriteInfo($"刷新 {p.Name} 模型列表超时，将使用本地预定义模型。");
            }
            catch (Exception ex)
            {
                WriteInfo($"刷新 {p.Name} 模型列表失败: {ex.Message}，将使用本地预定义模型。");
            }
        }

        try
        {
            Console.ForegroundColor = ConsoleColor.Green;

            Console.WriteLine();

            Console.WriteLine("所有可用模型:");
            foreach (var p in providers)
            {
                var displayName = ProviderHelper.GetDisplayName(p.Name);
                Console.WriteLine($"  {displayName}:");

                var allModels = ProviderHelper.GetAllModels(p.Name, p.CustomModels);
                if (allModels.Count == 0)
                {
                    Console.WriteLine("    (无可用模型)");
                }
                else
                {
                    foreach (var model in allModels)
                    {
                        var isSelected = ConfigManager.SelectedModel == $"{p.Name}:{model}";
                        var selected = isSelected ? " (当前)" : "";
                        var custom = p.CustomModels?.Contains(model) == true ? " [自定义]" : "";
                        Console.WriteLine($"    - {model}{selected}{custom}");
                    }
                }
            }

            Console.WriteLine();
        }
        finally
        {
            Console.ResetColor();
        }

        if (!string.IsNullOrEmpty(ConfigManager.SelectedModel))
        {
            WriteSuccess($"当前选择的模型: {ConfigManager.SelectedModel}");
        }
        else
        {
            WriteInfo("当前未选择模型，请使用 model switch 选择");
        }

        return true;
    }

    private Task<bool> ExecuteAddAsync(string[] args)
    {
        Console.WriteLine();

        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            WriteError("暂无配置的 Provider，请先使用 provider add 添加");
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
                WriteError($"Provider '{providerName}' 不存在");
                return Task.FromResult(true);
            }
        }
        else
        {
            Console.WriteLine("选择 Provider:");
            for (int i = 0; i < providers.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {ProviderHelper.GetDisplayName(providers[i].Name)}");
            }

            Console.Write("请选择 (1-{0}): ", providers.Count);
            var choice = Console.ReadLine()?.Trim();

            if (!int.TryParse(choice, out var index) || index < 1 || index > providers.Count)
            {
                WriteError("无效选择");
                return Task.FromResult(true);
            }

            providerName = providers[index - 1].Name;

            Console.Write("请输入模型名称: ");
            modelName = Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(modelName))
        {
            WriteError("模型名称不能为空");
            return Task.FromResult(true);
        }

        try
        {
            var provider = ConfigManager.GetProvider(providerName);
            var existing = ProviderHelper.GetAllModels(providerName, provider?.CustomModels);
            if (existing.Contains(modelName))
            {
                WriteInfo($"模型 '{modelName}' 已存在于 {ProviderHelper.GetDisplayName(providerName)}");
                return Task.FromResult(true);
            }

            ConfigManager.AddCustomModel(providerName, modelName);
            WriteSuccess($"已为 {ProviderHelper.GetDisplayName(providerName)} 添加模型: {modelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    private Task<bool> ExecuteUpdateAsync(string[] args)
    {
        Console.WriteLine();

        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            WriteError("暂无配置的 Provider");
            return Task.FromResult(true);
        }

        var providersWithCustom = providers.Where(p => p.CustomModels?.Count > 0).ToList();
        if (providersWithCustom.Count == 0)
        {
            WriteInfo("没有自定义模型可更新");
            return Task.FromResult(true);
        }

        Console.WriteLine("选择包含自定义模型的 Provider:");
        for (int i = 0; i < providersWithCustom.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {ProviderHelper.GetDisplayName(providersWithCustom[i].Name)}");
        }

        Console.Write("请选择 (1-{0}): ", providersWithCustom.Count);
        var choice = Console.ReadLine()?.Trim();

        if (!int.TryParse(choice, out var index) || index < 1 || index > providersWithCustom.Count)
        {
            WriteError("无效选择");
            return Task.FromResult(true);
        }

        var provider = providersWithCustom[index - 1];

        Console.WriteLine();
        Console.WriteLine($"{ProviderHelper.GetDisplayName(provider.Name)} 的自定义模型:");
        for (int i = 0; i < provider.CustomModels.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {provider.CustomModels[i]}");
        }

        Console.Write("请选择要更新的模型 (1-{0}): ", provider.CustomModels.Count);
        var modelChoice = Console.ReadLine()?.Trim();

        if (!int.TryParse(modelChoice, out var modelIndex) || modelIndex < 1 || modelIndex > provider.CustomModels.Count)
        {
            WriteError("无效选择");
            return Task.FromResult(true);
        }

        var oldModelName = provider.CustomModels[modelIndex - 1];

        Console.Write("请输入新的模型名称: ");
        var newModelName = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(newModelName))
        {
            WriteError("模型名称不能为空");
            return Task.FromResult(true);
        }

        try
        {
            ConfigManager.UpdateCustomModel(provider.Name, oldModelName, newModelName);
            WriteSuccess($"已更新模型: {oldModelName} -> {newModelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    private Task<bool> ExecuteDeleteAsync(string[] args)
    {
        Console.WriteLine();

        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            WriteError("暂无配置的 Provider");
            return Task.FromResult(true);
        }

        var providersWithCustom = providers.Where(p => p.CustomModels?.Count > 0).ToList();
        if (providersWithCustom.Count == 0)
        {
            WriteInfo("没有自定义模型可删除");
            return Task.FromResult(true);
        }

        Console.WriteLine("选择包含自定义模型的 Provider:");
        for (int i = 0; i < providersWithCustom.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {ProviderHelper.GetDisplayName(providersWithCustom[i].Name)}");
        }

        Console.Write("请选择 (1-{0}): ", providersWithCustom.Count);
        var choice = Console.ReadLine()?.Trim();

        if (!int.TryParse(choice, out var index) || index < 1 || index > providersWithCustom.Count)
        {
            WriteError("无效选择");
            return Task.FromResult(true);
        }

        var provider = providersWithCustom[index - 1];

        Console.WriteLine();
        Console.WriteLine($"{ProviderHelper.GetDisplayName(provider.Name)} 的自定义模型:");
        for (int i = 0; i < provider.CustomModels.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {provider.CustomModels[i]}");
        }

        Console.Write("请选择要删除的模型 (1-{0}): ", provider.CustomModels.Count);
        var modelChoice = Console.ReadLine()?.Trim();

        if (!int.TryParse(modelChoice, out var modelIndex) || modelIndex < 1 || modelIndex > provider.CustomModels.Count)
        {
            WriteError("无效选择");
            return Task.FromResult(true);
        }

        var modelName = provider.CustomModels[modelIndex - 1];

        Console.Write($"确定要删除模型 '{modelName}' 吗？(y/N): ");
        var confirm = Console.ReadLine()?.Trim().ToLower();
        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("已取消");
            return Task.FromResult(true);
        }

        try
        {
            ConfigManager.RemoveCustomModel(provider.Name, modelName);
            WriteSuccess($"已删除模型: {modelName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ModelCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    private Task<bool> ExecuteSwitchAsync(string[] args)
    {
        if (ConfigManager.Providers.Count == 0)
        {
            WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return Task.FromResult(true);
        }

        if (args.Length > 0)
        {
            var modelId = string.Join(' ', args);
            if (!modelId.Contains(':'))
            {
                WriteError("模型格式错误，应为 provider:model，例如 openai:gpt-4o");
                return Task.FromResult(true);
            }

            var parts = modelId.Split(':', 2);
            if (!ConfigManager.HasProvider(parts[0]))
            {
                WriteError($"Provider '{parts[0]}' 不存在");
                return Task.FromResult(true);
            }

            try
            {
                ConfigManager.SetSelectedModel(modelId);
                WriteSuccess($"已切换模型: {modelId}");
            }
            catch (Exception ex)
            {
                Logger.Error("ModelCommand 操作异常", ex);
                WriteError(ex.Message);
            }

            return Task.FromResult(true);
        }

        Console.WriteLine();

        var providerList = ConfigManager.Providers.ToList();
        Console.WriteLine("选择 Provider:");
        for (int i = 0; i < providerList.Count; i++)
        {
            var p = providerList[i];
            var displayName = ProviderHelper.GetDisplayName(p.Name);
            var selected = ConfigManager.SelectedModel?.StartsWith(p.Name + ":") == true ? " (当前)" : "";
            Console.WriteLine($"  {i + 1}. {displayName}{selected}");
        }

        Console.Write("请选择 Provider (1-{0}): ", providerList.Count);
        var providerChoice = Console.ReadLine()?.Trim();

        if (!int.TryParse(providerChoice, out var providerIndex) || providerIndex < 1 || providerIndex > providerList.Count)
        {
            WriteError("无效选择");
            return Task.FromResult(true);
        }

        var selectedProvider = providerList[providerIndex - 1];
        var allModels = ProviderHelper.GetAllModels(selectedProvider.Name, selectedProvider.CustomModels);

        Console.WriteLine();
        Console.WriteLine($"{ProviderHelper.GetDisplayName(selectedProvider.Name)} 可用模型:");

        if (allModels.Count > 0)
        {
            for (int i = 0; i < allModels.Count; i++)
            {
                var isSelected = ConfigManager.SelectedModel == $"{selectedProvider.Name}:{allModels[i]}";
                var selected = isSelected ? " (已选)" : "";
                Console.WriteLine($"  {i + 1}. {allModels[i]}{selected}");
            }
            Console.WriteLine($"  {allModels.Count + 1}. 手动输入其他模型");

            Console.Write("请选择模型 (1-{0}): ", allModels.Count + 1);
            var modelChoice = Console.ReadLine()?.Trim();

            if (!int.TryParse(modelChoice, out var modelIndex) || modelIndex < 1 || modelIndex > allModels.Count + 1)
            {
                WriteError("无效选择");
                return Task.FromResult(true);
            }

            string modelName;
            if (modelIndex == allModels.Count + 1)
            {
                Console.Write("请输入模型名称: ");
                modelName = Console.ReadLine()?.Trim() ?? "";
            }
            else
            {
                modelName = allModels[modelIndex - 1];
            }

            if (string.IsNullOrEmpty(modelName))
            {
                WriteError("模型名称不能为空");
                return Task.FromResult(true);
            }

            try
            {
                var fullModel = $"{selectedProvider.Name}:{modelName}";
                ConfigManager.SetSelectedModel(fullModel);
                WriteSuccess($"已切换模型: {fullModel}");
            }
            catch (Exception ex)
            {
                Logger.Error("ModelCommand 操作异常", ex);
                WriteError(ex.Message);
            }
        }
        else
        {
            Console.Write("该 Provider 没有预定义模型，请输入模型名称: ");
            var modelName = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(modelName))
            {
                WriteError("模型名称不能为空");
                return Task.FromResult(true);
            }

            try
            {
                var fullModel = $"{selectedProvider.Name}:{modelName}";
                ConfigManager.SetSelectedModel(fullModel);
                WriteSuccess($"已切换模型: {fullModel}");
            }
            catch (Exception ex)
            {
                Logger.Error("ModelCommand 操作异常", ex);
                WriteError(ex.Message);
            }
        }

        return Task.FromResult(true);
    }
}
