using LuBan.AIAgent;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.LocalMemory;
using LuBan.AIAgent.Retrieval;
using LuBan.AIAgent.Wiki;
using LuBan.Logging;
using LuBan.Orm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LubanAgentCore.Agents;
using LubanAgentCore.Commands;
using LubanAgentCore.Repositories;

namespace LubanAgentCore.Hosting;

/// <summary>
/// Agent 宿主构建器：提供 DI 容器构建与工作区初始化，供 TUI/GUI 共用。
/// </summary>
public static class AgentHostBuilder
{
    /// <summary>
    /// 构建配置。
    /// </summary>
    public static IConfiguration BuildConfiguration(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var builder = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        return builder.Build();
    }

    /// <summary>
    /// 构建服务容器。
    /// </summary>
    /// <param name="configuration">配置</param>
    /// <param name="embedder">嵌入生成器（可选）</param>
    /// <param name="modelManager">模型管理器（可选）</param>
    /// <param name="configureServices">额外的服务配置委托（可选）</param>
    public static IServiceProvider BuildServiceProvider(
        IConfiguration configuration,
        OnnxEmbeddingGenerator? embedder = null,
        ModelManager? modelManager = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var configPath = ConfigManager.GetDefaultConfigPath();
        var configManager = new ConfigManager(configPath);
        configManager.Load();
        services.AddSingleton(configManager);
        services.AddSingleton<IAppConfigReader>(configManager);

        services.AddLogging(builder => builder.AddLuBanFileLogger());

        services.AddScoped<Microsoft.Extensions.AI.IChatClient>(sp =>
        {
            var cm = sp.GetRequiredService<ConfigManager>();
            var opts = sp.GetRequiredService<IOptions<LuBanAgentOptions>>().Value;
            return LuBan.AIAgent.Errors.ChatClientResilience.Wrap(
                new RequestDumpChatClient(cm.CreateChatClient()), opts);
        });

        services.AddSingleton<IProviderRouter>(sp =>
        {
            var cm = sp.GetRequiredService<ConfigManager>();
            var providers = new Dictionary<string, (OpenAI.OpenAIClient, OpenAI.OpenAIClientOptions?)>();
            foreach (var p in cm.Providers)
            {
                if (!string.IsNullOrEmpty(p.ApiKey))
                {
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
                    var openAIClient = new OpenAI.OpenAIClient(credential, clientOptions);
                    providers[p.Name] = (openAIClient, clientOptions);
                }
            }
            return new LuBanChatClient(providers);
        });

        services.AddSingleton<ILocalMemoryStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LocalMemoryOptions>>().Value;
            return new SqliteLocalMemoryStore(ResolveMemoryDbPath(opts));
        });

        services.AddLuBanAgent(configuration);

        // 用户/工作区/会话上下文（会话级作用域；P0 由会话作用域承载工作区级）
        var userId = Environment.UserName ?? "default";
        services.AddSingleton<IUserContext>(new UserContext(Environment.UserName));
        services.AddScoped<SessionContextState>();
        services.AddScoped<ISessionContext, SessionContext>();
        services.AddScoped<IWorkspaceContext, WorkspaceContext>();

        // Provider 可变状态（刷新得到的模型列表）按用户隔离，keyed 注册；门面实例同样 keyed
        services.AddKeyedSingleton<ProviderCache>(userId);
        services.AddKeyedSingleton<ProviderService>(userId,
            (sp, key) => new ProviderService(sp.GetRequiredKeyedService<ProviderCache>(key!)));

        // 模型多模态能力登记（可变状态）按用户隔离，keyed 注册；门面实例同样 keyed
        services.AddKeyedSingleton<ModelCapabilityCache>(userId);
        services.AddKeyedSingleton<ModelCapabilityService>(userId,
            (sp, key) => new ModelCapabilityService(sp.GetRequiredKeyedService<ModelCapabilityCache>(key!)));

        services.AddScoped<LuBanAgentFactory>();
        services.AddScoped<ILuBanAgentFactory>(sp => sp.GetRequiredService<LuBanAgentFactory>());

        // worktree 协调：git 封装无状态（单例，依赖单例 ProcessRunner），协调器按会话作用域
        services.AddSingleton<GitProcess>();
        services.AddScoped<IWorktreeCoordinator, WorktreeSessionManager>();
        services.AddSingleton<WorktreeReconciler>();

        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<SessionRuntimeManager>();
        services.AddSingleton<AgentHostService>();
        services.AddSingleton<AttachmentService>();
        services.AddSingleton<IAgentsMdInitializer, AgentsMdInitializer>();

        services.AddSingleton<SessionRepository>();
        services.AddSingleton<SessionMessageRepository>();
        services.AddSingleton<WorkspaceRepository>();
        services.AddSingleton<LlmWikiFileRepository>();
        services.AddSingleton<LlmWikiChunkRepository>();
        services.AddSingleton<WorktreeSessionRepository>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<ICommandCatalog, CommandCatalog>();
        services.AddSingleton<IWorkspaceContextProvider>(
            sp => new DelegateWorkspaceContextProvider(() => sp.GetRequiredService<IWorkspaceManager>().CurrentWorkspace?.WorkspaceId));

        if (embedder != null)
        {
            // 嵌入器本身单例（ONNX 会话昂贵），用串行装饰器包一层供多会话共享，避免并发同时推理
            services.AddSingleton<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(
                new SerializingEmbeddingGenerator(embedder));
            // 工作区内容相关服务按会话作用域：每会话一份，避免跨会话/跨工作区串扰
            services.AddScoped<IVectorStore, SqliteVectorStore>();
            services.AddScoped<IRetrievalService>(sp => new RetrievalService(
                sp.GetRequiredService<IVectorStore>(),
                sp.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(),
                sp.GetRequiredService<IOptions<LuBanAgentOptions>>()));
            services.AddScoped<IWikiContext, WorkspaceWikiContext>();
            services.AddScoped<IWikiService>(sp => new WikiService(
                sp.GetRequiredService<IRetrievalService>(),
                sp.GetRequiredService<IWikiContext>(),
                sp.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.Tools.Wiki));
            // 知识任务为应用级全局表（跨工作区统计/串行写库）：保持单例，任务体内部自建 scope 解析 scoped 服务
            services.AddSingleton<KnowledgeTaskService>();
            if (modelManager != null) services.AddSingleton(modelManager);
        }

        // 调用额外的服务配置委托
        configureServices?.Invoke(services);

        var sp = services.BuildServiceProvider(new ServiceProviderOptions
        {
            // 会话/工作区上下文均为 scoped；开启校验以在启动期暴露 captive dependency（单例误捕获 scoped）
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        Logger.SetLogger(sp.GetRequiredService<ILoggerFactory>());
        Logger.SetSerializer(LuBanLoggingServiceExtensions.CreateLuBanSerializer());

        // 容器构建后回填 ConfigManager，使其能解析 keyed 的按用户实例级服务
        configManager.AttachServices(sp);

        // 用全局配置初始化按用户的能力登记表，并注册持久化回调
        sp.GetRequiredKeyedService<ModelCapabilityCache>(userId)
            .Initialize(configManager.ModelCapabilities, configManager.PersistModelCapability);

        return sp;
    }

    /// <summary>
    /// 初始化工作区。
    /// </summary>
    public static async Task InitializeWorkspaceAsync(IServiceProvider sp, List<string> notices)
    {
        try
        {
            var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
            var workspaceRepo = sp.GetRequiredService<WorkspaceRepository>();
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();

            // 启动默认进入最近活跃的工作区（GetAllAsync 已按 LastActiveAt DESC 排序）。
            // 仅在库中没有任何工作区时，才以当前目录隐式创建工作区，保证首次启动可用。
            var existing = (await workspaceRepo.GetAllAsync()).FirstOrDefault();
            string workspaceId;
            if (existing == null)
            {
                var ws = await workspaceManager.CreateWorkspaceAsync(cwd, type: WorkspaceTypes.Normal);
                workspaceId = ws.WorkspaceId;
                notices.Add($"已创建工作区: {ws.Name} ({ws.RootPath})");
            }
            else
            {
                workspaceId = existing.WorkspaceId;
                notices.Add($"当前工作区: {existing.Name} ({existing.RootPath})");
            }

            // 设置为当前工作区
            await workspaceManager.SetCurrentAsync(workspaceId);

            // 如果工作区未授权，触发授权确认弹窗
            var current = workspaceManager.CurrentWorkspace;
            if (current != null && !current.IsAuthorized)
            {
                var authorized = await workspaceManager.EnsureAuthorizedAsync(current);
                if (authorized)
                {
                    notices.Add("工作区已授权");
                }
                else
                {
                    notices.Add("工作区未授权，文件操作将受限");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("工作区初始化失败", ex);
            notices.Add($"工作区初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析本地记忆库路径：优先使用配置项，留空则回退到用户数据目录默认路径。
    /// </summary>
    /// <param name="options">本地记忆配置选项，可为 null。</param>
    /// <returns>记忆库绝对路径。</returns>
    private static string ResolveMemoryDbPath(LocalMemoryOptions? options)
    {
        var dbPath = options?.DatabasePath;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dbPath = Path.Combine(appData, "LuBan", "AIAgent", "localmemory.db");
        }
        return Path.GetFullPath(dbPath);
    }

    private sealed class DelegateWorkspaceContextProvider(Func<string?> getWorkspaceId)
        : IWorkspaceContextProvider
    {
        public string? CurrentWorkspaceId => getWorkspaceId();
    }
}
