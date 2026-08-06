using LubanAgent.Configuration;
using LuBan.AIAgent.Configuration;
using Microsoft.Extensions.AI;

namespace LubanAgent.Services;

public class ConfigManager : IAppConfigReader
{
    private readonly string _configPath;
    private AppConfig _config = new();

    List<ProviderConfigData> IAppConfigReader.Providers =>
        _config.Providers.Select(p => new ProviderConfigData
        {
            Name = p.Name,
            ApiKey = p.ApiKey,
            Endpoint = p.BaseUrl,
            Models = p.CustomModels
        }).ToList();

    string? IAppConfigReader.SelectedModel => _config.SelectedModel;
    List<CustomSkillConfig> IAppConfigReader.CustomSkills => _config.CustomSkills;
    List<CustomRuleConfig> IAppConfigReader.CustomRules => _config.CustomRules;
    List<McpServerConfig> IAppConfigReader.McpServers => _config.McpServers;
    List<string> IAppConfigReader.DisabledBuiltinSkills => _config.DisabledBuiltinSkills;
    List<string> IAppConfigReader.DisabledBuiltinRules => _config.DisabledBuiltinRules;
    List<string> IAppConfigReader.DisabledBuiltinMcpClients => _config.DisabledBuiltinMcpClients;

    public List<ProviderConfig> Providers => _config.Providers;
    public string? SelectedModel => _config.SelectedModel;
    public List<CustomSkillConfig> CustomSkills => _config.CustomSkills;
    public List<CustomRuleConfig> CustomRules => _config.CustomRules;
    public List<McpServerConfig> McpServers => _config.McpServers;
    public List<string> DisabledBuiltinSkills => _config.DisabledBuiltinSkills;
    public List<string> DisabledBuiltinRules => _config.DisabledBuiltinRules;
    public List<string> DisabledBuiltinMcpClients => _config.DisabledBuiltinMcpClients;
    public bool HasSelectedModel => !string.IsNullOrEmpty(SelectedModel);

    public ConfigManager(string configPath)
    {
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    _config = config;
                    foreach (var p in _config.Providers)
                    {
                        if (!string.IsNullOrEmpty(p.Name))
                            p.Name = p.Name.Trim().ToLowerInvariant();
                    }
                    if (!string.IsNullOrEmpty(_config.SelectedModel))
                    {
                        var colonIdx = _config.SelectedModel.IndexOf(':');
                        if (colonIdx > 0)
                            _config.SelectedModel = _config.SelectedModel[..colonIdx].Trim().ToLowerInvariant() + _config.SelectedModel[colonIdx..];
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigManager.Load 加载配置失败", ex, _configPath);
            _config = new AppConfig();
        }
    }

    public void Save()
    {
        try
        {
            var json = _config.ToJson(hasIndentation: true);
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigManager.Save 保存配置失败", ex, _configPath);
            throw new InvalidOperationException($"保存配置失败: {ex.Message}", ex);
        }
    }

    public void AddProvider(string name, string apiKey, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Provider 名称不能为空", nameof(name));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API Key 不能为空", nameof(apiKey));

        name = name.Trim().ToLowerInvariant();
        var existing = Providers.FirstOrDefault(p => p.Name == name);
        if (existing != null)
        {
            existing.ApiKey = apiKey;
            existing.BaseUrl = baseUrl;
        }
        else
        {
            Providers.Add(new ProviderConfig { Name = name, ApiKey = apiKey, BaseUrl = baseUrl });
        }
        Save();
    }

    public void SetSelectedModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("模型不能为空", nameof(model));
        _config.SelectedModel = model;
        Save();
    }

    public void Clear()
    {
        _config = new AppConfig();
        Save();
    }

    public bool HasProvider(string name) => Providers.Any(p => p.Name == name.ToLowerInvariant());

    public ProviderConfig? GetProvider(string name) => Providers.FirstOrDefault(p => p.Name == name.ToLowerInvariant());

    public void AddCustomSkill(CustomSkillConfig skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (string.IsNullOrWhiteSpace(skill.Id))
            throw new ArgumentException("Skill Id 不能为空", nameof(skill));
        skill.Id = skill.Id.ToLowerInvariant();
        if (CustomSkills.Any(s => s.Id == skill.Id))
            throw new InvalidOperationException($"自定义 Skill '{skill.Id}' 已存在");
        CustomSkills.Add(skill);
        Save();
    }

    public void UpdateCustomSkill(CustomSkillConfig skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        var existing = CustomSkills.FirstOrDefault(s => s.Id == skill.Id.ToLowerInvariant());
        if (existing == null)
            throw new InvalidOperationException($"自定义 Skill '{skill.Id}' 不存在");
        existing.Name = skill.Name;
        existing.Description = skill.Description;
        existing.Category = skill.Category;
        existing.PromptTemplate = skill.PromptTemplate;
        existing.Examples = skill.Examples;
        Save();
    }

    public void RemoveCustomSkill(string id)
    {
        var removed = CustomSkills.RemoveAll(s => s.Id == id.ToLowerInvariant());
        if (removed > 0) Save();
    }

    public void SetCustomSkillEnabled(string id, bool enabled)
    {
        var skill = CustomSkills.FirstOrDefault(s => s.Id == id.ToLowerInvariant());
        if (skill == null) throw new InvalidOperationException($"自定义 Skill '{id}' 不存在");
        skill.Enabled = enabled;
        Save();
    }

    public void SetBuiltinSkillEnabled(string id, bool enabled)
    {
        id = id.ToLowerInvariant();
        if (enabled) DisabledBuiltinSkills.Remove(id);
        else if (!DisabledBuiltinSkills.Contains(id)) DisabledBuiltinSkills.Add(id);
        Save();
    }

    public void AddCustomRule(CustomRuleConfig rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.Id))
            throw new ArgumentException("规则 Id 不能为空", nameof(rule));
        rule.Id = rule.Id.ToLowerInvariant();
        if (CustomRules.Any(r => r.Id == rule.Id))
            throw new InvalidOperationException($"自定义规则 '{rule.Id}' 已存在");
        CustomRules.Add(rule);
        Save();
    }

    public void UpdateCustomRule(CustomRuleConfig rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var existing = CustomRules.FirstOrDefault(r => r.Id == rule.Id.ToLowerInvariant());
        if (existing == null) throw new InvalidOperationException($"自定义规则 '{rule.Id}' 不存在");
        existing.Name = rule.Name;
        existing.Description = rule.Description;
        existing.ActionTypePattern = rule.ActionTypePattern;
        existing.TargetPattern = rule.TargetPattern;
        existing.Action = rule.Action;
        existing.Priority = rule.Priority;
        Save();
    }

    public void RemoveCustomRule(string id)
    {
        var removed = CustomRules.RemoveAll(r => r.Id == id.ToLowerInvariant());
        if (removed > 0) Save();
    }

    public void SetCustomRuleEnabled(string id, bool enabled)
    {
        var rule = CustomRules.FirstOrDefault(r => r.Id == id.ToLowerInvariant());
        if (rule == null) throw new InvalidOperationException($"自定义规则 '{id}' 不存在");
        rule.Enabled = enabled;
        Save();
    }

    public void SetBuiltinRuleEnabled(string id, bool enabled)
    {
        id = id.ToLowerInvariant();
        if (enabled) DisabledBuiltinRules.Remove(id);
        else if (!DisabledBuiltinRules.Contains(id)) DisabledBuiltinRules.Add(id);
        Save();
    }

    public void AddMcpServer(McpServerConfig server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (string.IsNullOrWhiteSpace(server.Name))
            throw new ArgumentException("服务器名称不能为空", nameof(server));
        server.Name = server.Name.ToLowerInvariant();
        if (McpServers.Any(s => s.Name == server.Name))
            throw new InvalidOperationException($"MCP 服务器 '{server.Name}' 已存在");
        McpServers.Add(server);
        Save();
    }

    public void UpdateMcpServer(McpServerConfig server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var existing = McpServers.FirstOrDefault(s => s.Name == server.Name.ToLowerInvariant());
        if (existing == null) throw new InvalidOperationException($"MCP 服务器 '{server.Name}' 不存在");
        existing.Description = server.Description;
        existing.Transport = server.Transport;
        existing.Command = server.Command;
        existing.Args = server.Args;
        Save();
    }

    public void RemoveMcpServer(string name)
    {
        var removed = McpServers.RemoveAll(s => s.Name == name.ToLowerInvariant());
        if (removed > 0) Save();
    }

    public void SetMcpServerEnabled(string name, bool enabled)
    {
        var server = McpServers.FirstOrDefault(s => s.Name == name.ToLowerInvariant());
        if (server == null) throw new InvalidOperationException($"MCP 服务器 '{name}' 不存在");
        server.Enabled = enabled;
        Save();
    }

    public void SetBuiltinMcpClientEnabled(string name, bool enabled)
    {
        name = name.ToLowerInvariant();
        if (enabled) DisabledBuiltinMcpClients.Remove(name);
        else if (!DisabledBuiltinMcpClients.Contains(name)) DisabledBuiltinMcpClients.Add(name);
        Save();
    }

    public void AddCustomModel(string providerName, string modelName)
    {
        var provider = GetProvider(providerName);
        if (provider == null) throw new InvalidOperationException($"Provider '{providerName}' 不存在");
        modelName = modelName.Trim();
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("模型名称不能为空", nameof(modelName));
        if (!provider.CustomModels.Contains(modelName))
        {
            provider.CustomModels.Add(modelName);
            Save();
        }
    }

    public void UpdateCustomModel(string providerName, string oldModelName, string newModelName)
    {
        var provider = GetProvider(providerName);
        if (provider == null) throw new InvalidOperationException($"Provider '{providerName}' 不存在");
        var index = provider.CustomModels.IndexOf(oldModelName);
        if (index >= 0)
        {
            provider.CustomModels[index] = newModelName.Trim();
            if (SelectedModel == $"{provider.Name}:{oldModelName}")
                SelectedModel = $"{provider.Name}:{newModelName.Trim()}";
            Save();
        }
    }

    public void RemoveCustomModel(string providerName, string modelName)
    {
        var provider = GetProvider(providerName);
        if (provider == null) throw new InvalidOperationException($"Provider '{providerName}' 不存在");
        if (provider.CustomModels.Remove(modelName))
        {
            if (SelectedModel == $"{provider.Name}:{modelName}")
                SelectedModel = null;
            Save();
        }
    }

    public List<string> GetAllModels(string providerName)
    {
        var provider = GetProvider(providerName);
        if (provider == null) return new List<string>();
        var models = ProviderHelper.GetModels(providerName);
        var allModels = new List<string>(models);
        foreach (var custom in provider.CustomModels)
        {
            if (!allModels.Contains(custom))
                allModels.Add(custom);
        }
        return allModels;
    }

    public IChatClient CreateChatClient()
    {
        if (string.IsNullOrEmpty(SelectedModel))
            throw new InvalidOperationException("请先选择模型");

        var parts = SelectedModel.Split(':', 2);
        if (parts.Length != 2)
            throw new InvalidOperationException($"模型格式错误: {SelectedModel}");

        var providerName = parts[0].ToLowerInvariant();
        var modelName = parts[1];

        var provider = GetProvider(providerName);
        if (provider == null)
            throw new InvalidOperationException($"Provider '{providerName}' 不存在");

        var timeoutSeconds = provider.NetworkTimeoutSeconds ?? 60;
        var clientOptions = new OpenAI.OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        if (!string.IsNullOrEmpty(provider.BaseUrl))
            clientOptions.Endpoint = new Uri(provider.BaseUrl);

        var credential = new System.ClientModel.ApiKeyCredential(provider.ApiKey);
        var openAIClient = new OpenAI.OpenAIClient(credential, clientOptions);
        return openAIClient.GetChatClient(modelName).AsIChatClient();
    }

    public static string GetDefaultConfigPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "LuBan", "AIAgent", "config.json");
    }
}
