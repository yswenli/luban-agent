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
using LuBan.AIAgent.Configuration;
using Microsoft.Extensions.Configuration;

namespace LubanAgent.Commands;

/// <summary>
/// Provider 管理命令
/// </summary>
public class ProviderCommand : CommandBase
{
    public override string Name => "provider";

    public override string Description => "管理 AI Provider（-list/-add/-update/-delete/-switch）";

    public ProviderCommand(ConfigManager configManager, IConfiguration configuration)
        : base(configManager, configuration)
    {
    }

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

    private Task<bool> ExecuteAddAsync(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("选择 Provider 类型:");
        Console.WriteLine("  1. OpenAI");
        Console.WriteLine("  2. Azure OpenAI");
        Console.WriteLine("  3. DeepSeek");
        Console.WriteLine("  4. Kimi (Moonshot)");
        Console.WriteLine("  5. 智谱 GLM");
        Console.WriteLine("  6. 通义千问");
        Console.WriteLine("  7. 豆包");
        Console.WriteLine("  8. Claude");
        Console.WriteLine("  9. Google Gemini");
        Console.WriteLine("  10. Ollama (本地)");
        Console.WriteLine("  11. 自定义 OpenAI 兼容 API");
        Console.Write("请选择 (1-11): ");

        var choice = Console.ReadLine()?.Trim();

        string providerName;
        string apiKey;
        string? baseUrl = null;

        switch (choice)
        {
            case "1":
                providerName = "openai";
                Console.Write("请输入 OpenAI API Key: ");
                apiKey = ReadPassword();
                break;

            case "2":
                providerName = "azure";
                Console.Write("请输入 Azure OpenAI API Key: ");
                apiKey = ReadPassword();
                Console.Write("请输入 Azure OpenAI Endpoint (如 https://your-resource.openai.azure.com): ");
                baseUrl = Console.ReadLine()?.Trim();
                break;

            case "3":
                providerName = "deepseek";
                Console.Write("请输入 DeepSeek API Key: ");
                apiKey = ReadPassword();
                baseUrl = "https://api.deepseek.com";
                break;

            case "4":
                providerName = "kimi";
                Console.Write("请输入 Kimi API Key: ");
                apiKey = ReadPassword();
                baseUrl = "https://api.moonshot.cn";
                break;

            case "5":
                providerName = "glm";
                Console.Write("请输入智谱 API Key: ");
                apiKey = ReadPassword();
                baseUrl = "https://open.bigmodel.cn";
                break;

            case "6":
                providerName = "qwen";
                Console.Write("请输入通义千问 API Key: ");
                apiKey = ReadPassword();
                baseUrl = "https://dashscope.aliyuncs.com";
                break;

            case "7":
                providerName = "doubao";
                Console.Write("请输入豆包 API Key: ");
                apiKey = ReadPassword();
                break;

            case "8":
                providerName = "claude";
                Console.Write("请输入 Claude API Key: ");
                apiKey = ReadPassword();
                baseUrl = "https://api.anthropic.com";
                break;

            case "9":
                providerName = "gemini";
                Console.Write("请输入 Google AI API Key: ");
                apiKey = ReadPassword();
                break;

            case "10":
                providerName = "ollama";
                apiKey = "ollama";
                Console.Write("请输入 Ollama 服务地址 (默认 http://localhost:11434): ");
                baseUrl = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(baseUrl))
                    baseUrl = "http://localhost:11434";
                break;

            case "11":
                Console.WriteLine();
                Console.Write("请输入 Provider 名称: ");
                providerName = Console.ReadLine()?.Trim()?.ToLower() ?? "custom";
                Console.Write("请输入 API Key: ");
                apiKey = ReadPassword();
                Console.Write("请输入 API Base URL: ");
                baseUrl = Console.ReadLine()?.Trim();
                break;

            default:
                WriteError("无效选择");
                return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            WriteError("API Key 不能为空");
            return Task.FromResult(false);
        }

        try
        {
            ConfigManager.AddProvider(providerName, apiKey, baseUrl);

            var displayName = ProviderModels.GetDisplayName(providerName);
            var models = ProviderModels.GetModels(providerName);

            WriteSuccess($"Provider '{displayName}' 已添加并保存");

            if (models.Count > 0)
            {
                Console.WriteLine($"  支持的模型: {string.Join(", ", models.Take(5))}{(models.Count > 5 ? "..." : "")}");
            }
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

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
            Console.WriteLine($"  {i + 1}. {ProviderModels.GetDisplayName(p.Name)}");
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
        Console.WriteLine($"更新 {ProviderModels.GetDisplayName(provider.Name)}:");
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
            WriteSuccess($"Provider '{ProviderModels.GetDisplayName(provider.Name)}' 已更新");
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

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
            Console.WriteLine($"  {i + 1}. {ProviderModels.GetDisplayName(p.Name)}");
        }

        Console.Write("请选择 (1-{0}): ", providers.Count);
        var choice = Console.ReadLine()?.Trim();

        if (!int.TryParse(choice, out var index) || index < 1 || index > providers.Count)
        {
            WriteError("无效选择");
            return Task.FromResult(true);
        }

        var provider = providers[index - 1];
        
        Console.Write($"确定要删除 {ProviderModels.GetDisplayName(provider.Name)} 吗？(y/N): ");
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
            
            if (ConfigManager.SelectedModel?.StartsWith($"{provider.Name}:") == true)
            {
                ConfigManager.SetSelectedModel("");
                Console.WriteLine("  注意: 已清除当前选择的模型（因为该模型属于被删除的 Provider）");
            }
            
            WriteSuccess($"Provider '{ProviderModels.GetDisplayName(provider.Name)}' 已删除");
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    private Task<bool> ExecuteListAsync()
    {
        Console.WriteLine();
        Console.WriteLine("已配置的 Provider:");

        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            Console.WriteLine("  (暂无)");
        }
        else
        {
            foreach (var p in providers)
            {
                var displayName = ProviderModels.GetDisplayName(p.Name);
                var maskedKey = MaskApiKey(p.ApiKey);
                var isCurrent = ConfigManager.SelectedModel?.StartsWith(p.Name + ":") == true ? " (当前)" : "";
                Console.WriteLine($"  - {displayName}{isCurrent}");
                Console.WriteLine($"      API Key: {maskedKey}");
                if (!string.IsNullOrEmpty(p.BaseUrl))
                    Console.WriteLine($"      Base URL: {p.BaseUrl}");
            }
        }

        return Task.FromResult(true);
    }

    private Task<bool> ExecuteSwitchAsync(string[] args)
    {
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return Task.FromResult(true);
        }

        string providerName;

        if (args.Length > 0)
        {
            providerName = args[0].ToLower();
            if (!ConfigManager.HasProvider(providerName))
            {
                WriteError($"Provider '{providerName}' 不存在");
                return Task.FromResult(true);
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
                Console.WriteLine($"  {i + 1}. {ProviderModels.GetDisplayName(p.Name)}{isCurrent}");
            }

            Console.Write("请选择 (1-{0}): ", providers.Count);
            var choice = Console.ReadLine()?.Trim();

            if (!int.TryParse(choice, out var index) || index < 1 || index > providers.Count)
            {
                WriteError("无效选择");
                return Task.FromResult(true);
            }

            providerName = providers[index - 1].Name;
        }

        var allModels = ConfigManager.GetAllModels(providerName);
        if (allModels.Count == 0)
        {
            Console.WriteLine();
            Console.Write($"该 Provider 没有可用模型，请输入模型名称: ");
            var modelName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(modelName))
            {
                WriteError("模型名称不能为空");
                return Task.FromResult(true);
            }

            ConfigManager.SetSelectedModel($"{providerName}:{modelName}");
            WriteSuccess($"已切换到 {ProviderModels.GetDisplayName(providerName)}，模型: {modelName}");
            return Task.FromResult(true);
        }

        Console.WriteLine();
        Console.WriteLine($"{ProviderModels.GetDisplayName(providerName)} 可用模型:");
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
            return Task.FromResult(true);
        }

        var selectedModel = allModels[modelIndex - 1];
        ConfigManager.SetSelectedModel($"{providerName}:{selectedModel}");
        WriteSuccess($"已切换到 {ProviderModels.GetDisplayName(providerName)}，模型: {selectedModel}");

        return Task.FromResult(true);
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            return "****";
        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }
}
