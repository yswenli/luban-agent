using LuBan.AIAgent;
using LuBan.AIAgent.Abstractions;
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
using LubanAgentCore.Configuration;
using LubanAgentCore.Repositories;
using LubanAgentCore.Services;

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
    public static AgentHostServices BuildServiceProvider(
        IConfiguration configuration,
        OnnxEmbeddingGenerator? embedder = null,
        ModelManager? modelManager = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging(builder => builder.AddLuBanFileLogger());

        // ── root：身份无关的进程级单例 ─────────────────────────────────
        services.AddSingleton<SessionRepository>();
        services.AddSingleton<SessionMessageRepository>();
        services.AddSingleton<WorkspaceRepository>();
        services.AddSingleton<LlmWikiFileRepository>();
        services.AddSingleton<LlmWikiChunkRepository>();
        services.AddSingleton<WorktreeSessionRepository>();
        services.AddSingleton<GitProcess>();
        services.AddSingleton<SessionRuntimeManager>();
        services.AddSingleton<UserContextRegistry>();
        services.AddSingleton<ICommandCatalog, CommandCatalog>();

        services.AddSingleton<ILocalMemoryStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LocalMemoryOptions>>().Value;
            return new SqliteLocalMemoryStore(ResolveMemoryDbPath(opts));
        });

        services.AddLuBanAgent(configuration);

        // ── user scope：身份与用户级服务 ──────────────────────────────
        services.AddScoped<UserContextState>();
        services.AddScoped<LuBan.AIAgent.Abstractions.IUserContext>(sp =>
            new UserContext(sp.GetRequiredService<UserContextState>()));
        services.AddScoped<SessionContextState>();
        services.AddScoped<ISessionContext, SessionContext>();
        services.AddScoped<IWorkspaceContext, WorkspaceContext>();
        services.AddScoped<IAgentReloadTrigger, SessionRuntimeReloadTrigger>();

        services.AddScoped<Microsoft.Extensions.AI.IChatClient>(sp =>
        {
            var cm = sp.GetRequiredService<ConfigManager>();
            var opts = sp.GetRequiredService<IOptions<LuBanAgentOptions>>().Value;
            return LuBan.AIAgent.Errors.ChatClientResilience.Wrap(
                new RequestDumpChatClient(cm.CreateChatClient()), opts);
        });

        // 按用户隔离的 shared state：Scoped 门面转发到 UserContextRegistry 的 per-user 桶
        services.AddScoped(sp => sp.GetRequiredService<UserContextRegistry>()
            .GetOrCreate(sp.GetRequiredService<LuBan.AIAgent.Abstractions.IUserContext>().UserId).Config);
        services.AddScoped(sp => sp.GetRequiredService<UserContextRegistry>()
            .GetOrCreate(sp.GetRequiredService<LuBan.AIAgent.Abstractions.IUserContext>().UserId).ProviderCache);
        services.AddScoped(sp => sp.GetRequiredService<UserContextRegistry>()
            .GetOrCreate(sp.GetRequiredService<LuBan.AIAgent.Abstractions.IUserContext>().UserId).ProviderService);
        services.AddScoped(sp => sp.GetRequiredService<UserContextRegistry>()
            .GetOrCreate(sp.GetRequiredService<LuBan.AIAgent.Abstractions.IUserContext>().UserId).ModelCapabilityCache);
        services.AddScoped(sp => sp.GetRequiredService<UserContextRegistry>()
            .GetOrCreate(sp.GetRequiredService<LuBan.AIAgent.Abstractions.IUserContext>().UserId).ModelCapabilityService);
        services.AddScoped(sp => sp.GetRequiredService<UserContextRegistry>()
            .GetOrCreate(sp.GetRequiredService<LuBan.AIAgent.Abstractions.IUserContext>().UserId).ProviderRouter);
        services.AddScoped(sp => sp.GetRequiredService<UserContextRegistry>()
            .GetOrCreate(sp.GetRequiredService<LuBan.AIAgent.Abstractions.IUserContext>().UserId).SessionManager);
        services.AddScoped(sp => sp.GetRequiredService<UserContextRegistry>()
            .GetOrCreate(sp.GetRequiredService<LuBan.AIAgent.Abstractions.IUserContext>().UserId).WorkspaceManager);
        services.AddScoped<IAppConfigReader>(sp => sp.GetRequiredService<ConfigManager>());

        services.AddScoped<LuBanAgentFactory>();
        services.AddScoped<ILuBanAgentFactory>(sp => sp.GetRequiredService<LuBanAgentFactory>());

        services.AddScoped<IWorktreeCoordinator, WorktreeSessionManager>();
        services.AddScoped<WorktreeReconciler>();
        services.AddScoped<AgentHostService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<IAgentsMdInitializer, AgentsMdInitializer>();

        services.AddScoped<IWorkspaceContextProvider>(sp =>
            new DelegateWorkspaceContextProvider(() => sp.GetRequiredService<IWorkspaceManager>().CurrentWorkspace?.WorkspaceId));

        if (embedder != null)
        {
            services.AddSingleton<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(
                new SerializingEmbeddingGenerator(embedder));
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
            services.AddSingleton<KnowledgeTaskService>();
            if (modelManager != null) services.AddSingleton(modelManager);
        }

        configureServices?.Invoke(services);

        var root = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        Logger.SetLogger(root.GetRequiredService<ILoggerFactory>());
        Logger.SetSerializer(LuBanLoggingServiceExtensions.CreateLuBanSerializer());

        // 历史单用户数据归属归一化（会话/工作区 UserId: null/''/'default' → 默认用户）
        LegacyUserDataMigrator.Migrate();

        // 进程常驻用户作用域：CLI/Codex 单用户身份在此绑定；session scope 由 SessionRuntimeManager 从 root 派生并回填身份
        var userScope = root.CreateUserScope(LocalUserDefaults.DefaultUserId);

        return new AgentHostServices(root, userScope);
    }

    /// <summary>
    /// 初始化工作区。
    /// </summary>
    public static async Task InitializeWorkspaceAsync(IServiceProvider sp, List<string> notices)
    {
        try
        {
            var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();

            // 启动默认进入当前用户最近活跃的工作区（GetUserWorkspacesAsync 已按 LastActiveAt DESC 排序）。
            // 仅在当前用户没有任何工作区时，才以当前目录隐式创建工作区，保证首次启动可用。
            var existing = (await workspaceManager.GetUserWorkspacesAsync()).FirstOrDefault();
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
