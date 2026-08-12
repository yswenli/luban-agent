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
using LubanAgent.App;

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
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public ProviderCommand(ConfigManager configManager, IConfiguration configuration,
        ITuiOutputWriter writer, ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
    }

    /// <summary>
    /// 显示命令帮助信息
    /// </summary>
    public override Task ExecuteAsync()
    {
        Writer.WriteLine();
        Writer.WriteHeader("Provider 管理命令");
        Writer.WriteLine("  provider -list    - 列出所有 Provider");
        Writer.WriteLine("  provider -add     - 添加 Provider");
        Writer.WriteLine("  provider -update  - 更新 Provider");
        Writer.WriteLine("  provider -delete  - 删除 Provider");
        Writer.WriteLine("  provider -switch  - 切换当前 Provider");
        Writer.WriteLine("  简写: /p -l, /p -a, /p -u, /p -d, /p -s");
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
        // 选择 Provider 类型（编号菜单 → Choose 对话框）
        var options = BuiltinProviders
            .Select(p => p.DisplayName)
            .Append("自定义 OpenAI 兼容 API")
            .ToList();
        var chosen = Ui.Choose("添加 Provider", options);
        if (chosen is null) return Task.FromResult(true); // 用户取消

        var choiceIndex = chosen.Value + 1; // 保持与原 1 起始编号一致的后续逻辑

        string providerName;
        string apiKey;
        string? baseUrl = null;

        if (choiceIndex == BuiltinProviders.Length + 1)
        {
            var values = Ui.ShowForm("自定义 Provider", new[]
            {
                new FormField("Provider 名称", InitialValue: "custom"),
                new FormField("API Key", IsPassword: true),
                new FormField("API Base URL", Required: false)
            });
            if (values is null) return Task.FromResult(true);

            providerName = string.IsNullOrWhiteSpace(values[0]) ? "custom" : values[0].Trim().ToLower();
            apiKey = values[1];
            baseUrl = string.IsNullOrWhiteSpace(values[2]) ? null : values[2].Trim();
        }
        else
        {
            var (name, displayName, needCustomEndpoint, needCustomApiKey, warning) = BuiltinProviders[choiceIndex - 1];
            providerName = name;

            if (!string.IsNullOrEmpty(warning))
            {
                Ui.Notify(displayName, warning);
            }

            var defaultUrl = name switch
            {
                "azure" => "https://your-resource.openai.azure.com",
                "ollama" => "http://localhost:11434/v1",
                _ => ""
            };

            var fields = new List<FormField>
            {
                new FormField($"{displayName} API Key", IsPassword: !needCustomApiKey)
            };
            if (needCustomEndpoint)
            {
                fields.Add(new FormField("API 地址", Required: false, InitialValue: defaultUrl));
            }

            var values = Ui.ShowForm($"添加 {displayName}", fields);
            if (values is null) return Task.FromResult(true);

            apiKey = needCustomApiKey ? values[0].Trim() : values[0];

            if (needCustomEndpoint)
            {
                baseUrl = string.IsNullOrWhiteSpace(values[1]) ? defaultUrl : values[1].Trim();
            }
            else
            {
                baseUrl = SelectEndpoint(providerName);
            }
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Writer.WriteError("API Key 不能为空");
            return Task.FromResult(false);
        }

        try
        {
            ConfigManager.AddProvider(providerName, apiKey, baseUrl);

            var displayName = GetProviderDisplayName(providerName);
            var models = ProviderHelper.GetModels(providerName);

            Writer.WriteSuccess($"Provider '{displayName}' 已添加并保存");

            if (models.Count > 0)
            {
                Writer.WriteInfo($"  支持的模型: {string.Join(", ", models.Take(5))}{(models.Count > 5 ? "..." : "")}");
            }
            else
            {
                Writer.WriteInfo("  提示: 该 Provider 没有预设模型，请使用 /model -add 添加自定义模型");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderCommand 添加 Provider 异常", ex, providerName);
            Writer.WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 选择 API 地址（多 endpoint 时弹选择框；取消/无效回落到第一个，保持原行为）
    /// </summary>
    private string? SelectEndpoint(string providerName)
    {
        var endpoints = ProviderHelper.GetEndpoints(providerName);
        if (endpoints.Count == 0) return null;
        if (endpoints.Count == 1) return endpoints[0].Url;

        var chosen = Ui.Choose(
            $"{ProviderHelper.GetDisplayName(providerName)} API 地址选择",
            endpoints.Select(e => $"{e.Description} ({e.Url})").ToList());

        return chosen is { } i && i >= 0 && i < endpoints.Count
            ? endpoints[i].Url
            : endpoints[0].Url; // 取消/无效保持原行为：回落到第一个
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
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return Task.FromResult(true);
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择要更新的 Provider",
            providers.Select(p => GetProviderDisplayName(p.Name)).ToList());
        if (chosen is null) return Task.FromResult(true); // 用户取消

        var provider = providers[chosen.Value];
        var displayName = GetProviderDisplayName(provider.Name);

        Writer.WriteLine();
        Writer.WriteLine($"更新 {displayName}:");
        Writer.WriteLine($"  当前 API Key: {MaskApiKey(provider.ApiKey)}");
        Writer.WriteLine($"  当前 Base URL: {provider.BaseUrl ?? "(默认)"}");

        // 留空保持原值：字段非必填，初始值为现值；清空则回落到现值
        var values = Ui.ShowForm($"更新 {displayName}", new[]
        {
            new FormField("新的 API Key (留空保持不变)", IsPassword: true, InitialValue: provider.ApiKey, Required: false),
            new FormField("新的 Base URL (留空保持不变)", InitialValue: provider.BaseUrl, Required: false)
        });
        if (values is null) return Task.FromResult(true); // 用户取消

        var newApiKey = values[0].Trim();
        if (string.IsNullOrEmpty(newApiKey))
            newApiKey = provider.ApiKey;

        var newBaseUrl = values[1].Trim();
        if (string.IsNullOrEmpty(newBaseUrl))
            newBaseUrl = provider.BaseUrl;

        try
        {
            ConfigManager.AddProvider(provider.Name, newApiKey, newBaseUrl);
            Writer.WriteSuccess($"Provider '{displayName}' 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderCommand 更新 Provider 异常", ex, provider.Name);
            Writer.WriteError(ex.Message);
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
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            Writer.WriteError("暂无配置的 Provider");
            return Task.FromResult(true);
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择要删除的 Provider",
            providers.Select(p => GetProviderDisplayName(p.Name)).ToList());
        if (chosen is null) return Task.FromResult(true); // 用户取消

        var provider = providers[chosen.Value];
        var displayName = GetProviderDisplayName(provider.Name);

        // 危险操作：默认"否"
        if (!Ui.Confirm("删除 Provider", $"确定要删除 {displayName} 吗？", defaultValue: false))
        {
            Writer.WriteInfo("已取消");
            return Task.FromResult(true);
        }

        try
        {
            providers.RemoveAt(chosen.Value);
            ConfigManager.Save();

            // 如果当前选择的模型属于被删除的 Provider，清除选择
            if (ConfigManager.SelectedModel?.StartsWith($"{provider.Name}:") == true)
            {
                ConfigManager.SetSelectedModel("");
                Writer.WriteInfo("  注意: 已清除当前选择的模型（因为该模型属于被删除的 Provider）");
            }

            Writer.WriteSuccess($"Provider '{displayName}' 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderCommand 删除 Provider 异常", ex, provider.Name);
            Writer.WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 列出所有已配置的 Provider
    /// </summary>
    /// <returns>是否已处理</returns>
    private Task<bool> ExecuteListAsync()
    {
        var providers = ConfigManager.Providers;
        if (providers.Count == 0)
        {
            Writer.WriteInfo("已配置的 Provider: (暂无)");
            return Task.FromResult(true);
        }

        var rows = providers
            .Select(p =>
            {
                var displayName = GetProviderDisplayName(p.Name);
                var isCurrent = ConfigManager.SelectedModel?.StartsWith(p.Name + ":") == true ? " (当前)" : "";
                return (IReadOnlyList<string>)new[]
                {
                    $"{displayName}{isCurrent}",
                    MaskApiKey(p.ApiKey),
                    string.IsNullOrEmpty(p.BaseUrl) ? "(默认)" : p.BaseUrl!
                };
            })
            .ToList();

        Ui.ShowTable("已配置的 Provider", new[] { "Provider", "API Key", "Base URL" }, rows);

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
            Writer.WriteError("暂无配置的 Provider，请先使用 provider add 添加");
            return true;
        }

        string providerName;

        // 支持直接通过参数指定 Provider 名称
        if (args.Length > 0)
        {
            providerName = args[0].ToLower();
            if (!ConfigManager.HasProvider(providerName))
            {
                Writer.WriteError($"Provider '{providerName}' 不存在");
                return true;
            }
        }
        else
        {
            // 编号菜单 → Choose 对话框
            var chosen = Ui.Choose("选择要切换到的 Provider",
                providers.Select(p =>
                {
                    var isCurrent = ConfigManager.SelectedModel?.StartsWith(p.Name + ":") == true ? " (当前)" : "";
                    return $"{GetProviderDisplayName(p.Name)}{isCurrent}";
                }).ToList());
            if (chosen is null) return true; // 用户取消

            providerName = providers[chosen.Value].Name;
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
                Writer.WriteInfo($"刷新 {GetProviderDisplayName(providerName)} 模型列表超时，将使用本地预定义模型。");
            }
            catch (Exception ex)
            {
                Writer.WriteInfo($"刷新 {GetProviderDisplayName(providerName)} 模型列表失败: {ex.Message}，将使用本地预定义模型。");
            }
        }

        var allModels = ProviderHelper.GetAllModels(providerName, provider?.CustomModels);

        // 如果 Provider 没有预定义模型，让用户手动输入模型名称
        if (allModels.Count == 0)
        {
            var values = Ui.ShowForm($"{GetProviderDisplayName(providerName)} 没有可用模型", new[]
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

            ConfigManager.SetSelectedModel($"{providerName}:{modelName}");
            Writer.WriteSuccess($"已切换到 {GetProviderDisplayName(providerName)}，模型: {modelName}");
            return true;
        }

        // 编号菜单 → Choose 对话框
        var modelChosen = Ui.Choose($"{GetProviderDisplayName(providerName)} 可用模型",
            allModels.Select(m =>
            {
                var isSelected = ConfigManager.SelectedModel == $"{providerName}:{m}" ? " (已选)" : "";
                return $"{m}{isSelected}";
            }).ToList());
        if (modelChosen is null) return true; // 用户取消

        var selectedModel = allModels[modelChosen.Value];
        ConfigManager.SetSelectedModel($"{providerName}:{selectedModel}");
        Writer.WriteSuccess($"已切换到 {GetProviderDisplayName(providerName)}，模型: {selectedModel}");

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
