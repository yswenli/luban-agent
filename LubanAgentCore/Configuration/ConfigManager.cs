/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*Author：yswenli
*命名空间：LubanAgent.Configuration
*文件名： ConfigManager
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：应用配置管理器，负责配置的加载、保存及各类配置项的增删改查
*
*****************************************************************************/
using System.Text.Json;
using LuBan.AIAgent.Configuration;
using Microsoft.Extensions.AI;

namespace LubanAgentCore.Configuration;

/// <summary>
/// 应用配置管理器，负责配置文件的加载与保存，并提供 Provider、Skill、规则、MCP 服务器及模型等配置项的读写操作
/// </summary>
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

    /// <summary>
    /// 已配置的 Provider 列表
    /// </summary>
    public List<ProviderConfig> Providers => _config.Providers;

    /// <summary>
    /// 当前选中的模型（格式：providerName:modelName）
    /// </summary>
    public string? SelectedModel => _config.SelectedModel;

    /// <summary>
    /// 自定义 Skill 配置列表
    /// </summary>
    public List<CustomSkillConfig> CustomSkills => _config.CustomSkills;

    /// <summary>
    /// 自定义规则配置列表
    /// </summary>
    public List<CustomRuleConfig> CustomRules => _config.CustomRules;

    /// <summary>
    /// MCP 服务器配置列表
    /// </summary>
    public List<McpServerConfig> McpServers => _config.McpServers;

    /// <summary>
    /// 已禁用的内置 Skill 标识列表
    /// </summary>
    public List<string> DisabledBuiltinSkills => _config.DisabledBuiltinSkills;

    /// <summary>
    /// 已禁用的内置规则标识列表
    /// </summary>
    public List<string> DisabledBuiltinRules => _config.DisabledBuiltinRules;

    /// <summary>
    /// 已禁用的内置 MCP 客户端名称列表
    /// </summary>
    public List<string> DisabledBuiltinMcpClients => _config.DisabledBuiltinMcpClients;

    /// <summary>
    /// 是否已选中模型
    /// </summary>
    public bool HasSelectedModel => !string.IsNullOrEmpty(SelectedModel);

    /// <summary>
    /// 创建 ConfigManager 实例
    /// </summary>
    /// <param name="configPath">配置文件路径</param>
    public ConfigManager(string configPath)
    {
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
    }

    /// <summary>
    /// 从磁盘加载配置，加载失败时使用空配置
    /// </summary>
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

    /// <summary>
    /// 将当前配置序列化并保存到磁盘，目录不存在时自动创建
    /// </summary>
    /// <exception cref="InvalidOperationException">保存失败时抛出</exception>
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

    /// <summary>
    /// 添加或更新 Provider，已存在时更新其 API Key 与 Base URL
    /// </summary>
    /// <param name="name">Provider 名称</param>
    /// <param name="apiKey">API 密钥</param>
    /// <param name="baseUrl">API 基础地址，可省略</param>
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

    /// <summary>
    /// 设置当前选中的模型
    /// </summary>
    /// <param name="model">模型标识（格式：providerName:modelName）</param>
    public void SetSelectedModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("模型不能为空", nameof(model));
        _config.SelectedModel = model;
        Save();
    }

    /// <summary>
    /// 清空全部配置并保存
    /// </summary>
    public void Clear()
    {
        _config = new AppConfig();
        Save();
    }

    /// <summary>
    /// 判断指定名称的 Provider 是否已配置
    /// </summary>
    /// <param name="name">Provider 名称</param>
    /// <returns>已配置时返回 true，否则返回 false</returns>
    public bool HasProvider(string name) => Providers.Any(p => p.Name == name.ToLowerInvariant());

    /// <summary>
    /// 按名称获取 Provider 配置
    /// </summary>
    /// <param name="name">Provider 名称</param>
    /// <returns>Provider 配置；不存在时返回 null</returns>
    public ProviderConfig? GetProvider(string name) => Providers.FirstOrDefault(p => p.Name == name.ToLowerInvariant());

    /// <summary>
    /// 添加自定义 Skill，已存在时抛出异常
    /// </summary>
    /// <param name="skill">Skill 配置</param>
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

    /// <summary>
    /// 更新自定义 Skill 的可编辑字段
    /// </summary>
    /// <param name="skill">包含更新后数据的 Skill 配置</param>
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

    /// <summary>
    /// 删除指定标识的自定义 Skill
    /// </summary>
    /// <param name="id">Skill 标识</param>
    public void RemoveCustomSkill(string id)
    {
        var removed = CustomSkills.RemoveAll(s => s.Id == id.ToLowerInvariant());
        if (removed > 0) Save();
    }

    /// <summary>
    /// 设置自定义 Skill 的启用状态
    /// </summary>
    /// <param name="id">Skill 标识</param>
    /// <param name="enabled">是否启用</param>
    public void SetCustomSkillEnabled(string id, bool enabled)
    {
        var skill = CustomSkills.FirstOrDefault(s => s.Id == id.ToLowerInvariant());
        if (skill == null) throw new InvalidOperationException($"自定义 Skill '{id}' 不存在");
        skill.Enabled = enabled;
        Save();
    }

    /// <summary>
    /// 设置内置 Skill 的启用状态
    /// </summary>
    /// <param name="id">内置 Skill 标识</param>
    /// <param name="enabled">是否启用</param>
    public void SetBuiltinSkillEnabled(string id, bool enabled)
    {
        id = id.ToLowerInvariant();
        if (enabled) DisabledBuiltinSkills.Remove(id);
        else if (!DisabledBuiltinSkills.Contains(id)) DisabledBuiltinSkills.Add(id);
        Save();
    }

    /// <summary>
    /// 添加自定义规则，已存在时抛出异常
    /// </summary>
    /// <param name="rule">规则配置</param>
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

    /// <summary>
    /// 更新自定义规则的可编辑字段
    /// </summary>
    /// <param name="rule">包含更新后数据的规则配置</param>
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

    /// <summary>
    /// 删除指定标识的自定义规则
    /// </summary>
    /// <param name="id">规则标识</param>
    public void RemoveCustomRule(string id)
    {
        var removed = CustomRules.RemoveAll(r => r.Id == id.ToLowerInvariant());
        if (removed > 0) Save();
    }

    /// <summary>
    /// 设置自定义规则的启用状态
    /// </summary>
    /// <param name="id">规则标识</param>
    /// <param name="enabled">是否启用</param>
    public void SetCustomRuleEnabled(string id, bool enabled)
    {
        var rule = CustomRules.FirstOrDefault(r => r.Id == id.ToLowerInvariant());
        if (rule == null) throw new InvalidOperationException($"自定义规则 '{id}' 不存在");
        rule.Enabled = enabled;
        Save();
    }

    /// <summary>
    /// 设置内置规则的启用状态
    /// </summary>
    /// <param name="id">内置规则标识</param>
    /// <param name="enabled">是否启用</param>
    public void SetBuiltinRuleEnabled(string id, bool enabled)
    {
        id = id.ToLowerInvariant();
        if (enabled) DisabledBuiltinRules.Remove(id);
        else if (!DisabledBuiltinRules.Contains(id)) DisabledBuiltinRules.Add(id);
        Save();
    }

    /// <summary>
    /// 添加 MCP 服务器配置，已存在时抛出异常
    /// </summary>
    /// <param name="server">MCP 服务器配置</param>
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

    /// <summary>
    /// 更新 MCP 服务器的可编辑字段
    /// </summary>
    /// <param name="server">包含更新后数据的 MCP 服务器配置</param>
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

    /// <summary>
    /// 删除指定名称的 MCP 服务器配置
    /// </summary>
    /// <param name="name">MCP 服务器名称</param>
    public void RemoveMcpServer(string name)
    {
        var removed = McpServers.RemoveAll(s => s.Name == name.ToLowerInvariant());
        if (removed > 0) Save();
    }

    /// <summary>
    /// 设置 MCP 服务器的启用状态
    /// </summary>
    /// <param name="name">MCP 服务器名称</param>
    /// <param name="enabled">是否启用</param>
    public void SetMcpServerEnabled(string name, bool enabled)
    {
        var server = McpServers.FirstOrDefault(s => s.Name == name.ToLowerInvariant());
        if (server == null) throw new InvalidOperationException($"MCP 服务器 '{name}' 不存在");
        server.Enabled = enabled;
        Save();
    }

    /// <summary>
    /// 设置内置 MCP 客户端的启用状态
    /// </summary>
    /// <param name="name">内置 MCP 客户端名称</param>
    /// <param name="enabled">是否启用</param>
    public void SetBuiltinMcpClientEnabled(string name, bool enabled)
    {
        name = name.ToLowerInvariant();
        if (enabled) DisabledBuiltinMcpClients.Remove(name);
        else if (!DisabledBuiltinMcpClients.Contains(name)) DisabledBuiltinMcpClients.Add(name);
        Save();
    }

    /// <summary>
    /// 为指定 Provider 添加自定义模型
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <param name="modelName">模型名称</param>
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

    /// <summary>
    /// 重命名指定 Provider 的自定义模型，同步更新当前选中模型
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <param name="oldModelName">原模型名称</param>
    /// <param name="newModelName">新模型名称</param>
    public void UpdateCustomModel(string providerName, string oldModelName, string newModelName)
    {
        var provider = GetProvider(providerName);
        if (provider == null) throw new InvalidOperationException($"Provider '{providerName}' 不存在");
        var index = provider.CustomModels.IndexOf(oldModelName);
        if (index >= 0)
        {
            provider.CustomModels[index] = newModelName.Trim();
            if (SelectedModel == $"{provider.Name}:{oldModelName}")
                _config.SelectedModel = $"{provider.Name}:{newModelName.Trim()}";
            Save();
        }
    }

    /// <summary>
    /// 删除指定 Provider 的自定义模型，若其被选中则同时清除选中模型
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <param name="modelName">模型名称</param>
    public void RemoveCustomModel(string providerName, string modelName)
    {
        var provider = GetProvider(providerName);
        if (provider == null) throw new InvalidOperationException($"Provider '{providerName}' 不存在");
        if (provider.CustomModels.Remove(modelName))
        {
            if (SelectedModel == $"{provider.Name}:{modelName}")
                _config.SelectedModel = null;
            Save();
        }
    }

    /// <summary>
    /// 获取指定 Provider 的全部模型（内置预设与用户自定义模型合并）
    /// </summary>
    /// <param name="providerName">Provider 名称</param>
    /// <returns>完整模型列表；Provider 不存在时返回空列表</returns>
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

    /// <summary>
    /// 根据当前选中的模型创建聊天客户端
    /// </summary>
    /// <returns>配置好的 IChatClient 实例</returns>
    /// <exception cref="InvalidOperationException">未选中模型、模型格式错误或 Provider 不存在时抛出</exception>
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

    /// <summary>
    /// 获取默认配置文件路径
    /// </summary>
    /// <returns>本地应用数据目录下的默认配置文件路径</returns>
    public static string GetDefaultConfigPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "LuBan", "AIAgent", "config.json");
    }
}
