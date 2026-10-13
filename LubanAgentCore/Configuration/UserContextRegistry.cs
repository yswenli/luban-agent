/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： UserContextRegistry
*版本号： V1.0.0.0
*创建时间：2026/10/13
*描述：按用户维护进程级共享状态（配置、Provider 缓存、能力登记、会话/工作区管理器）
*
*****************************************************************************/
using System.Collections.Concurrent;
using LuBan.AIAgent.Configuration;
using LubanAgentCore.Hosting;

namespace LubanAgentCore.Configuration;

/// <summary>单个用户的进程级共享状态桶（跨该用户所有会话 scope 共享）。</summary>
public sealed class UserBucket
{
    /// <summary>用户标识。</summary>
    public required string UserId { get; init; }

    /// <summary>该用户的配置管理器（独立配置文件）。</summary>
    public required ConfigManager Config { get; init; }

    /// <summary>该用户的 Provider 可变缓存。</summary>
    public required ProviderCache ProviderCache { get; init; }

    /// <summary>该用户的 Provider 门面。</summary>
    public required ProviderService ProviderService { get; init; }

    /// <summary>该用户的模型能力登记缓存。</summary>
    public required ModelCapabilityCache ModelCapabilityCache { get; init; }

    /// <summary>该用户的模型能力门面。</summary>
    public required ModelCapabilityService ModelCapabilityService { get; init; }

    /// <summary>该用户的 Provider 路由（多 Provider 聊天客户端）。</summary>
    public required IProviderRouter ProviderRouter { get; init; }

    /// <summary>该用户绑定的会话管理器（可变状态按用户隔离）。</summary>
    public required ISessionManager SessionManager { get; init; }

    /// <summary>该用户绑定的工作区管理器。</summary>
    public required IWorkspaceManager WorkspaceManager { get; init; }
}

/// <summary>
/// 按用户维护进程级共享状态（配置、Provider 缓存、能力登记、会话/工作区管理器）。
/// 单例注册；由 Scoped 门面工厂按当前用户解析。
/// </summary>
public sealed class UserContextRegistry
{
    private readonly IServiceProvider _root;
    private readonly ConcurrentDictionary<string, Lazy<UserBucket>> _buckets = new(StringComparer.Ordinal);

    /// <summary>创建用户状态注册表。</summary>
    /// <param name="root">根级容器（用于解析身份无关的仓储/内置目录等单例）。</param>
    public UserContextRegistry(IServiceProvider root) => _root = root;

    /// <summary>取（或创建）指定用户的状态桶；用户为空时使用本地默认用户。</summary>
    public UserBucket GetOrCreate(string? userId)
    {
        var key = string.IsNullOrWhiteSpace(userId) ? LocalUserDefaults.DefaultUserId : userId.Trim();
        return _buckets
            .GetOrAdd(key, k => new Lazy<UserBucket>(() => Build(k), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    private UserBucket Build(string userId)
    {
        // Provider 缓存 → 门面 → 配置（配置需要门面以合并内置/刷新/自定义模型）
        var providerCache = new ProviderCache();
        var providerService = new ProviderService(providerCache);

        var config = new ConfigManager(ConfigManager.GetDefaultConfigPath(userId), providerService);
        config.Load();

        var capabilityCache = new ModelCapabilityCache();
        capabilityCache.Initialize(config.ModelCapabilities, config.PersistModelCapability);
        var capabilityService = new ModelCapabilityService(capabilityCache);

        var providerRouter = BuildProviderRouter(config);

        var sessionManager = new SessionManager(userId);
        var workspaceManager = new WorkspaceManager(
            userId,
            _root.GetRequiredService<WorkspaceRepository>(),
            _root.GetRequiredService<SessionRepository>(),
            sessionManager,
            _root.GetRequiredService<BuiltinSkillCatalog>(),
            _root.GetRequiredService<BuiltinRuleCatalog>());

        return new UserBucket
        {
            UserId = userId,
            Config = config,
            ProviderCache = providerCache,
            ProviderService = providerService,
            ModelCapabilityCache = capabilityCache,
            ModelCapabilityService = capabilityService,
            ProviderRouter = providerRouter,
            SessionManager = sessionManager,
            WorkspaceManager = workspaceManager
        };
    }

    private static IProviderRouter BuildProviderRouter(ConfigManager config)
    {
        var providers = new Dictionary<string, (OpenAI.OpenAIClient, OpenAI.OpenAIClientOptions?)>();
        foreach (var p in config.Providers)
        {
            if (string.IsNullOrEmpty(p.ApiKey)) continue;

            var clientOptions = new OpenAI.OpenAIClientOptions
            {
                // 禁用 SDK 内置重试，由 ResilientChatClient 统一重试（与 ConfigManager.CreateChatClient 一致）
                RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0)
            };
            if (p.NetworkTimeoutSeconds.HasValue)
                clientOptions.NetworkTimeout = TimeSpan.FromSeconds(p.NetworkTimeoutSeconds.Value);
            if (!string.IsNullOrEmpty(p.BaseUrl))
                clientOptions.Endpoint = new Uri(p.BaseUrl);

            var credential = new System.ClientModel.ApiKeyCredential(p.ApiKey);
            providers[p.Name] = (new OpenAI.OpenAIClient(credential, clientOptions), clientOptions);
        }
        return new LuBanChatClient(providers);
    }
}