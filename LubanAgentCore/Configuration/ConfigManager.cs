/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*Author：yswenli
*命名空间：LubanAgentCore.Configuration
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
using LuBan.AIAgent.Connectors.AliyunDevOps;
using LuBan.AIAgent.Plugins.Security;
using Microsoft.Extensions.AI;

namespace LubanAgentCore.Configuration;

/// <summary>
/// 应用配置管理器，负责配置文件的加载与保存，并提供 Provider、Skill、规则、MCP 服务器及模型等配置项的读写操作
/// </summary>
public class ConfigManager : IAppConfigReader
{
    private readonly string _configPath;
    private AppConfig _config = new();
    private readonly ProviderService? _providerService;

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
    List<ConnectorConfig> IAppConfigReader.Connectors => _config.Connectors;
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
    /// 连接器声明列表（可用性 + 非敏感参数）。
    /// </summary>
    public List<ConnectorConfig> Connectors => _config.Connectors;

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
    /// <param name="providerService">Provider 门面（可为 null，此时模型列表使用本地回退缓存）</param>
    public ConfigManager(string configPath, ProviderService? providerService = null)
    {
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        _providerService = providerService;
    }

    /// <summary>
    /// 从磁盘加载配置；<b>解析失败时保留磁盘原文件不动</b>（绝不用空模型覆盖用户配置）。
    /// </summary>
    /// <remarks>
    /// 只有两种情况允许随后迁移落盘：① 配置读取成功；② 首次运行（文件本就不存在）。
    /// 解析失败时内存里是空模型，若照常落盘会把 Providers（ApiKey）/ Integrations（PAT）/
    /// SelectedModel / Skills / Rules 全部抹掉。
    /// </remarks>
    public void Load()
    {
        var exists = File.Exists(_configPath);

        // 允许落盘的唯一依据：确实读到过磁盘内容，或本来就是首次运行
        var canPersist = !exists;

        try
        {
            if (exists)
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize(json, ConfigJsonOptions.Pretty.GetTypeInfo(typeof(AppConfig))) as AppConfig;
                if (config != null)
                {
                    _config = config;
                    canPersist = true;

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
                else
                {
                    Logger.Warn($"ConfigManager.Load 反序列化结果为空，已忽略且不覆盖磁盘文件: {_configPath}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigManager.Load 加载配置失败（保留磁盘原文件，不落盘覆盖）", ex, _configPath);
            _config = new AppConfig();
            canPersist = false;
        }

        if (!canPersist)
            return;

        // 配置迁移（Q15 凭据键泛化 + 连接器声明补种）：一次性、失败不阻断启动
        try
        {
            if (MigrateIfNeeded())
            {
                TryBackupOnce();
                Save();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigManager 配置迁移失败", ex, _configPath);
        }
    }

    /// <summary>
    /// 迁移落盘前对既有配置做一次性备份（<c>config.json.bak</c>；已存在则不覆盖）。
    /// </summary>
    private void TryBackupOnce()
    {
        try
        {
            if (!File.Exists(_configPath)) return;

            var backupPath = _configPath + ".bak";
            if (File.Exists(backupPath)) return;

            File.Copy(_configPath, backupPath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"ConfigManager 备份配置失败（继续迁移）: {ex.Message}");
        }
    }

    /// <summary>
    /// 需要落盘的一次性配置迁移。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ① <b>凭据键泛化（Q15）</b>：把历史拼写键（<c>yunxiao_pat</c> / <c>codeup:pat</c> 等）的值
    /// 复制到规范键（<c>connector:&lt;实例名&gt;:token</c>）。旧键<b>保留不删</b>，供读取回退与回滚。
    /// </para>
    /// <para>
    /// ② <b>连接器声明补种</b>：老安装的 <c>config.json</c> 无该实例声明时补一条默认声明（按实例名判重），
    /// 使插件清单里的 <c>mcpServers: ["yunxiao"]</c> 仍能解析；<c>enabled=false</c> 表示「声明可用但
    /// 不默认启用」，避免连接器工具被无差别暴露给所有会话（启用仍由工作区显式开启）。
    /// </para>
    /// </remarks>
    /// <returns>有变动需要保存时返回 <c>true</c>。</returns>
    private bool MigrateIfNeeded()
    {
        var changed = false;

        // ① 凭据键泛化
        foreach (var kvp in _config.Integrations.ToList())
        {
            if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                continue;

            var canonical = CredentialKeys.Normalize(kvp.Key);
            if (string.Equals(canonical, kvp.Key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_config.Integrations.ContainsKey(canonical))
                continue;

            _config.Integrations[canonical] = kvp.Value;
            changed = true;
        }

        // ② 连接器声明补种：按「实例名是否已声明」判重，而非「列表是否为空」——
        //    否则用户只要声明了别的连接器，插件清单里的 mcpServers: ["yunxiao"] 就永久解析不到。
        if (!_config.Connectors.Any(c =>
                string.Equals(c?.Name?.Trim(), YunxiaoConnector.DefaultName, StringComparison.OrdinalIgnoreCase)))
        {
            _config.Connectors.Add(new ConnectorConfig
            {
                Name = YunxiaoConnector.DefaultName,
                Provider = YunxiaoConnector.ProviderKey,
                Enabled = false
            });
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 已登记的模型多模态能力记录（键为 "provider:model" 小写归一化，值为是否支持图片输入）。
    /// </summary>
    public IReadOnlyDictionary<string, bool> ModelCapabilities => _config.ModelCapabilities;

    /// <summary>
    /// 持久化模型能力记录（更新内存配置并保存到磁盘）。
    /// </summary>
    /// <param name="modelId">模型标识（已归一化为小写）。</param>
    /// <param name="supportsVision">是否支持图片输入。</param>
    public void PersistModelCapability(string modelId, bool supportsVision)
    {
        _config.ModelCapabilities[modelId] = supportsVision;
        try
        {
            Save();
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigManager 保存模型能力记录失败", ex, modelId);
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
            var json = JsonSerializer.Serialize(_config, ConfigJsonOptions.Pretty.GetTypeInfo(typeof(AppConfig)));
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // 原子落盘：先写临时文件再替换，避免进程中断/断电留下半截 JSON
            // （半截文件会在下次启动解析失败，若当时仍无条件落盘就会抹掉用户配置）
            var tempPath = _configPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _configPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigManager.Save 保存配置失败", ex, _configPath);
            throw new InvalidOperationException($"保存配置失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 外部集成配置与凭据（云效 PAT / 组织 ID 等），键由调用方归一化。
    /// </summary>
    /// <remarks>
    /// PRD §4.9.3：凭据沿用宿主现有配置存储基线（与 <c>Provider.ApiKey</c> 同基线）。
    /// </remarks>
    public IReadOnlyDictionary<string, string> Integrations => _config.Integrations;

    /// <summary>
    /// 读取外部集成配置项。
    /// </summary>
    /// <param name="key">键（不归一化，由调用方负责）。</param>
    /// <returns>配置值；不存在或为空时返回 <c>null</c>。</returns>
    public string? GetIntegration(string key)
        => !string.IsNullOrWhiteSpace(key) && _config.Integrations.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>
    /// 写入或删除外部集成配置项（立即落盘）。
    /// </summary>
    /// <param name="key">键。</param>
    /// <param name="value">值；为 <c>null</c>/空时删除该键。</param>
    public void SetIntegration(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("集成配置键不能为空", nameof(key));

        if (string.IsNullOrWhiteSpace(value))
            _config.Integrations.Remove(key);
        else
            _config.Integrations[key] = value;

        Save();
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

    /// <summary>选中模型变更时触发（model 为新值，null 表示清空）。</summary>
    public event Action<string?>? SelectedModelChanged;

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
        SelectedModelChanged?.Invoke(model);
    }

    /// <summary>
    /// 清空当前选中的模型（用于删除 Provider 后清理）
    /// </summary>
    public void ClearSelectedModel()
    {
        _config.SelectedModel = null;
        Save();
        SelectedModelChanged?.Invoke(null);
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
        var models = _providerService is not null
            ? _providerService.GetModels(providerName)
            : ProviderHelper.GetModels(providerName, new ProviderCache());
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
            NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            // 禁用 SDK 内置重试（默认 ClientRetryPolicy.Default 会再重试 3 次），
            // 由 LuBan.AIAgent 的 ResilientChatClient 统一重试，避免次数相乘与不可见重试
            RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0)
        };
        if (!string.IsNullOrEmpty(provider.BaseUrl))
            clientOptions.Endpoint = new Uri(provider.BaseUrl);

        var credential = new System.ClientModel.ApiKeyCredential(provider.ApiKey);
        var openAIClient = new OpenAI.OpenAIClient(credential, clientOptions);
        return openAIClient.GetChatClient(modelName).AsIChatClient();
    }

    /// <summary>
    /// 获取配置文件路径。userId 为空时返回历史单用户路径（向后兼容 CommandBase 轻量路径）；
    /// 非空时返回该用户的独立配置文件路径，并在首次访问时从历史单用户配置迁移。
    /// </summary>
    /// <param name="userId">用户标识；为空时用历史路径。</param>
    public static string GetDefaultConfigPath(string? userId = null)
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(appDataPath, "LuBan", "AIAgent");
        if (string.IsNullOrWhiteSpace(userId))
            return Path.Combine(root, "config.json");

        var target = Path.Combine(root, "users", userId, "config.json");
        MigrateLegacyConfig(root, target);
        return target;
    }

    /// <summary>旧单用户配置存在且用户配置不存在时，复制一份作为初始配置。</summary>
    private static void MigrateLegacyConfig(string root, string target)
    {
        try
        {
            var legacy = Path.Combine(root, "config.json");
            if (File.Exists(target) || !File.Exists(legacy)) return;
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.Copy(legacy, target);
        }
        catch (Exception ex)
        {
            Logger.Warn($"迁移旧配置失败: {ex.Message}");
        }
    }
}
