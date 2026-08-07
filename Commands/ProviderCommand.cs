/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： ProviderCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/28
*描述：Provider 管理命令（支持 add/update/delete 子命令）
*
*****************************************************************************/
namespace LubanAgent.Commands;

/// <summary>
/// Provider 管理命令，支持添加、更新、删除、列表和切换 Provider
/// </summary>
/// <remarks>
/// 支持的子命令:
/// - list: 列出所有已配置的 Provider
/// - add: 添加新的 Provider
/// - update: 更新现有 Provider 的 API Key 或 Base URL
/// - delete: 删除 Provider
/// - switch: 切换当前使用的 Provider 和模型
/// 
/// 注意: OpenAI 兼容的 API 需要 BaseUrl 包含完整的 API 版本路径（如 /v1），
/// SDK 会在 Endpoint 后直接拼接 /chat/completions
/// </remarks>
public class ProviderCommand : CommandBase
{
    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "provider";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "管理 AI Provider（-list/-add/-update/-delete/-switch）";

    /// <summary>
    /// 创建 ProviderCommand 实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    public ProviderCommand(ConfigManager configManager, IConfiguration configuration)
        : base(configManager, configuration)
    {
    }

    /// <summary>
    /// 显示命令帮助信息
    /// </summary>
    public override Task ExecuteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Provider 管理命令:");
        Console.WriteLine("  provider -list    - 列出所有 Provider");
        Console.WriteLine("  provider -add     - 添加 Provider");
        Console.WriteLine("  provider -update  - 更新 Provider");
        Console.WriteLine("  provider -delete  - 删除 Provider");
        Console.WriteLine("  provider -switch  - 切换当前 Provider");
        Console.WriteLine("  简写: /p -l, /p -a, /p -u, /p -d, /p -s");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行子命令
    /// </summary>
    /// <param name="args">子命令参数</param>
    /// <returns>是否已处理</returns>
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
    /// 添加新的 Provider，通过交互式菜单选择类型并输入凭据
    /// </summary>
    /// <param name="args">未使用的参数</param>
    /// <returns>是否已处理</returns>
    private static readonly (string Name, string DisplayName, bool NeedCustomEndpoint, bool NeedCustomApiKey, string? Warning)[] BuiltinProviders =
    {
        ("openai", "OpenAI", false, false, null),
        ("azure", "Azure OpenAI", true, false, null),
        ("deepseek", "DeepSeek", false, false, null),
        ("kimi", "Kimi (Moonshot)", false, false, null),
        ("glm", "智谱 GLM", false, false, null),
        ("qwen", "通义千问", false, false, null),
        ("doubao", "豆包", false, false, null),
        ("claude", "Claude", false, false, "注意: Claude 使用 Anthropic Messages API，与 OpenAI 格式不同。\n如需使用 Claude，请通过第三方代理（如 one-api）转换为 OpenAI 兼容格式。"),
        ("gemini", "Google Gemini", false, false, null),
        ("ollama", "Ollama (本地)", true, true, null),
        ("minimax", "MiniMax", false, false, null),
        ("ark", "字节方舟 (火山引擎)", false, false, null),
        ("bailian", "阿里百炼", false, false, null),
        ("hunyuan", "腾讯混元", false, false, null),
        ("mimo", "小米 MiMo", false, false, null),
    };

    private Task<bool> ExecuteAddAsync(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("选择 Provider 类型:");
        for (int i = 0; i < BuiltinProviders.Length; i++)
        {
            var (_, displayName, _, _, _) = BuiltinProviders[i];
            Console.WriteLine($"  {i + 1}. {displayName}");
        }
        Console.WriteLine($"  {BuiltinProviders.Length + 1}. 自定义 OpenAI 兼容 API");
        Console.Write($"请选择 (1-{BuiltinProviders.Length + 1}): ");

        var choice = Console.ReadLine()?.Trim();
        if (!int.TryParse(choice, out var choiceIndex) || choiceIndex < 1 || choiceIndex > BuiltinProviders.Length + 1)
        {
            WriteError("无效选择");
            return Task.FromResult(false);
        }

        string providerName;
        string apiKey;
        string? baseUrl = null;

        if (choiceIndex == BuiltinProviders.Length + 1)
        {
            Console.WriteLine();
            Console.Write("请输入 Provider 名称: ");
            providerName = Console.ReadLine()?.Trim()?.ToLower() ?? "custom";
            Console.Write("请输入 API Key: ");
            apiKey = ReadPassword();
            Console.Write("请输入 API Base URL: ");
            baseUrl = Console.ReadLine()?.Trim();
        }
        else
        {
            var (name, displayName, needCustomEndpoint, needCustomApiKey, warning) = BuiltinProviders[choiceIndex - 1];
            providerName = name;

            if (!string.IsNullOrEmpty(warning))
            {
                Console.WriteLine();
                Console.WriteLine(warning);
            }

            Console.Write($"请输入 {displayName} API Key: ");
            apiKey = needCustomApiKey ? (Console.ReadLine()?.Trim() ?? "") : ReadPassword();

            if (needCustomEndpoint)
            {
                var defaultUrl = name switch
                {
                    "azure" => "https://your-resource.openai.azure.com",
                    "ollama" => "http://localhost:11434/v1",
                    _ => ""
                };
                Console.Write($"请输入 API 地址 (默认 {defaultUrl}): ");
                baseUrl = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(baseUrl))
                    baseUrl = defaultUrl;
            }
            else
            {
                baseUrl = SelectEndpoint(providerName);
            }
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            WriteError("API Key 不能为空");
            return Task.FromResult(false);
        }

        try
        {
            ConfigManager.AddProvider(providerName, apiKey, baseUrl);

            var displayName = GetProviderDisplayName(providerName);
            var models = ProviderHelper.GetModels(providerName);

            WriteSuccess($"Provider '{displayName}' 已添加并保存");

            if (models.Count > 0)
            {
                Console.WriteLine($"  支持的模型: {string.Join(", ", models.Take(5))}{(models.Count > 5 ? "..." : "")}");
            }
            else
            {
                Console.WriteLine("  提示: 该 Provider 没有预设模型，请使用 /model -add 添加自定义模型");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderCommand 添加 Provider 异常", ex, providerName);
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    private static string? SelectEndpoint(string providerName)
    {
        var endpoints = ProviderHelper.GetEndpoints(providerName);
        if (endpoints.Count == 0)
            return null;

        if (endpoints.Count == 1)
            return endpoints[0].Url;

        Console.WriteLine();
        Console.WriteLine($"{ProviderHelper.GetDisplayName(providerName)} API 地址选择:");
        for (int i = 0; i < endpoints.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {endpoints[i].Description} ({endpoints[i].Url})");
        }
        Console.Write($"请选择 (1-{endpoints.Count}): ");
        var epChoice = Console.ReadLine()?.Trim();
        if (int.TryParse(epChoice, out var epIndex) && epIndex >= 1 && epIndex <= endpoints.Count)
        {
            return endpoints[epIndex - 1].Url;
        }
        return endpoints[0].Url;
    }

    /// <summary>
    /// 获取 Provider 显示名称，优先使用库中的定义，否则使用自定义名称
    /// </summary>
    private static string GetProviderDisplayName(string providerName)
    {
        return ProviderHelper.GetDisplayName(providerName);
    }

    /// <summary>
    /// 更新现有 Provider 的 API Key 或 Base URL
    /// </summary>
    /// <param name="args">未使用的参数</param>
    /// <returns>是否已处理</returns>
    private Task<bool> ExecuteUpdateAsync(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("选择要更新的 Provider:");
        
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return Task.FromResult(true);
        }

        for (int i = 0; i < providers.Count; i++)
        {
            var p = providers[i];
            Console.WriteLine($"  {i + 1}. {GetProviderDisplayName(p.Name)}");
        }

        Console.Write("请选择 (1-{0}): ", providers.Count);
        var choice = Console.ReadLine()?.Trim();

        if (!int.TryParse(choice, out var index) || index < 1 || index > providers.Count)
        {
            WriteError("无效选择");
            return Task.FromResult(true);
        }

        var provider = providers[index - 1];
        Console.WriteLine();
        Console.WriteLine($"更新 {GetProviderDisplayName(provider.Name)}:");
        Console.WriteLine($"  当前 API Key: {MaskApiKey(provider.ApiKey)}");
        Console.WriteLine($"  当前 Base URL: {provider.BaseUrl ?? "(默认)"}");
        Console.WriteLine();

        Console.Write("请输入新的 API Key (留空保持不变): ");
        var newApiKey = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(newApiKey))
            newApiKey = provider.ApiKey;

        Console.Write("请输入新的 Base URL (留空保持不变): ");
        var newBaseUrl = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(newBaseUrl))
            newBaseUrl = provider.BaseUrl;

        try
        {
            ConfigManager.AddProvider(provider.Name, newApiKey, newBaseUrl);
            WriteSuccess($"Provider '{GetProviderDisplayName(provider.Name)}' 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderCommand 更新 Provider 异常", ex, provider.Name);
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 删除指定的 Provider
    /// </summary>
    /// <param name="args">未使用的参数</param>
    /// <returns>是否已处理</returns>
    private Task<bool> ExecuteDeleteAsync(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("选择要删除的 Provider:");
        
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            WriteError("暂无配置的 Provider");
            return Task.FromResult(true);
        }

        for (int i = 0; i < providers.Count; i++)
        {
            var p = providers[i];
            Console.WriteLine($"  {i + 1}. {GetProviderDisplayName(p.Name)}");
        }

        Console.Write("请选择 (1-{0}): ", providers.Count);
        var choice = Console.ReadLine()?.Trim();

        if (!int.TryParse(choice, out var index) || index < 1 || index > providers.Count)
        {
            WriteError("无效选择");
            return Task.FromResult(true);
        }

        var provider = providers[index - 1];
        
        Console.Write($"确定要删除 {GetProviderDisplayName(provider.Name)} 吗？(y/N): ");
        var confirm = Console.ReadLine()?.Trim().ToLower();
        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("已取消");
            return Task.FromResult(true);
        }

        try
        {
            providers.RemoveAt(index - 1);
            ConfigManager.Save();
            
            // 如果当前选择的模型属于被删除的 Provider，清除选择
            if (ConfigManager.SelectedModel?.StartsWith($"{provider.Name}:") == true)
            {
                ConfigManager.SetSelectedModel("");
                Console.WriteLine("  注意: 已清除当前选择的模型（因为该模型属于被删除的 Provider）");
            }
            
            WriteSuccess($"Provider '{GetProviderDisplayName(provider.Name)}' 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderCommand 删除 Provider 异常", ex, provider.Name);
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 列出所有已配置的 Provider
    /// </summary>
    /// <returns>是否已处理</returns>
    private Task<bool> ExecuteListAsync()
    {
        Console.WriteLine();
        Console.WriteLine("已配置的 Provider:");

        var providers = ConfigManager.Providers;
        Console.ForegroundColor = ConsoleColor.Green;
        try
        {
            if (providers.Count == 0)
            {
                Console.WriteLine("  (暂无)");
            }
            else
            {
                foreach (var p in providers)
                {
                    var displayName = GetProviderDisplayName(p.Name);
                    var maskedKey = MaskApiKey(p.ApiKey);
                    var isCurrent = ConfigManager.SelectedModel?.StartsWith(p.Name + ":") == true ? " (当前)" : "";
                    Console.WriteLine($"  - {displayName}{isCurrent}");
                    Console.WriteLine($"      API Key: {maskedKey}");
                    if (!string.IsNullOrEmpty(p.BaseUrl))
                        Console.WriteLine($"      Base URL: {p.BaseUrl}");
                }
            }
        }
        finally
        {
            Console.ResetColor();
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 切换当前使用的 Provider 和模型
    /// </summary>
    /// <param name="args">可选的 Provider 名称参数</param>
    /// <returns>是否已处理</returns>
    private async Task<bool> ExecuteSwitchAsync(string[] args)
    {
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return true;
        }

        string providerName;

        // 支持直接通过参数指定 Provider 名称
        if (args.Length > 0)
        {
            providerName = args[0].ToLower();
            if (!ConfigManager.HasProvider(providerName))
            {
                WriteError($"Provider '{providerName}' 不存在");
                return true;
            }
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("选择要切换到的 Provider:");
            for (int i = 0; i < providers.Count; i++)
            {
                var p = providers[i];
                var isCurrent = ConfigManager.SelectedModel?.StartsWith(p.Name + ":") == true ? " (当前)" : "";
                Console.WriteLine($"  {i + 1}. {GetProviderDisplayName(p.Name)}{isCurrent}");
            }

            Console.Write("请选择 (1-{0}): ", providers.Count);
            var choice = Console.ReadLine()?.Trim();

            if (!int.TryParse(choice, out var index) || index < 1 || index > providers.Count)
            {
                WriteError("无效选择");
                return true;
            }

            providerName = providers[index - 1].Name;
        }

        var provider = ConfigManager.GetProvider(providerName);

        // 先刷新该 Provider 的模型列表（容错、不阻塞）
        if (provider != null && !string.IsNullOrEmpty(provider.ApiKey))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await ProviderHelper.RefreshModelsAsync(providerName, provider.ApiKey, provider.BaseUrl, cts.Token);
            }
            catch (OperationCanceledException)
            {
                WriteInfo($"刷新 {GetProviderDisplayName(providerName)} 模型列表超时，将使用本地预定义模型。");
            }
            catch (Exception ex)
            {
                WriteInfo($"刷新 {GetProviderDisplayName(providerName)} 模型列表失败: {ex.Message}，将使用本地预定义模型。");
            }
        }

        var allModels = ProviderHelper.GetAllModels(providerName, provider?.CustomModels);
        
        // 如果 Provider 没有预定义模型，让用户手动输入模型名称
        if (allModels.Count == 0)
        {
            Console.WriteLine();
            Console.Write($"该 Provider 没有可用模型，请输入模型名称: ");
            var modelName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(modelName))
            {
                WriteError("模型名称不能为空");
                return true;
            }

            ConfigManager.SetSelectedModel($"{providerName}:{modelName}");
            WriteSuccess($"已切换到 {GetProviderDisplayName(providerName)}，模型: {modelName}");
            return true;
        }

        Console.WriteLine();
        Console.WriteLine($"{GetProviderDisplayName(providerName)} 可用模型:");
        for (int i = 0; i < allModels.Count; i++)
        {
            var isSelected = ConfigManager.SelectedModel == $"{providerName}:{allModels[i]}" ? " (已选)" : "";
            Console.WriteLine($"  {i + 1}. {allModels[i]}{isSelected}");
        }

        Console.Write("请选择模型 (1-{0}): ", allModels.Count);
        var modelChoice = Console.ReadLine()?.Trim();

        if (!int.TryParse(modelChoice, out var modelIndex) || modelIndex < 1 || modelIndex > allModels.Count)
        {
            WriteError("无效选择");
            return true;
        }

        var selectedModel = allModels[modelIndex - 1];
        ConfigManager.SetSelectedModel($"{providerName}:{selectedModel}");
        WriteSuccess($"已切换到 {GetProviderDisplayName(providerName)}，模型: {selectedModel}");

        return true;
    }

    /// <summary>
    /// 将 API Key 脱敏显示，只显示前4位和后4位
    /// </summary>
    /// <param name="apiKey">原始 API Key</param>
    /// <returns>脱敏后的字符串</returns>
    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            return "****";
        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }
}
