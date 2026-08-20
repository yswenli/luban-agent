using LuBan.AIAgent;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.LocalMemory;
using LuBan.AIAgent.Retrieval;
using LuBan.Logging;
using LuBan.Orm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            return cm.CreateChatClient();
        });

        services.AddSingleton<IProviderRouter>(sp =>
        {
            var cm = sp.GetRequiredService<ConfigManager>();
            var providers = new Dictionary<string, (OpenAI.OpenAIClient, OpenAI.OpenAIClientOptions?)>();
            foreach (var p in cm.Providers)
            {
                if (!string.IsNullOrEmpty(p.ApiKey))
                {
                    var clientOptions = new OpenAI.OpenAIClientOptions();
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
            var dbPath = opts.DatabasePath;
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dbPath = Path.Combine(appData, "LuBan", "AIAgent", "localmemory.db");
            }
            return new SqliteLocalMemoryStore(dbPath);
        });

        services.AddLuBanAgent(configuration);

        services.AddScoped<LuBanAgentFactory>();
        services.AddScoped<ILuBanAgentFactory>(sp => sp.GetRequiredService<LuBanAgentFactory>());

        services.AddSingleton<ISessionManager, SessionManager>();

        services.AddSingleton<SessionRepository>();
        services.AddSingleton<SessionMessageRepository>();
        services.AddSingleton<WorkspaceRepository>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<IWorkspaceContextProvider>(
            new DelegateWorkspaceContextProvider(() => WorkspaceManager.Current?.WorkspaceId));

        if (embedder != null)
        {
            services.AddSingleton<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(embedder);
            services.AddSingleton<IVectorStore, SqliteVectorStore>();
            services.AddSingleton<IRetrievalService>(sp => new RetrievalService(
                sp.GetRequiredService<IVectorStore>(),
                sp.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(),
                sp.GetRequiredService<IOptions<LuBanAgentOptions>>()));
            if (modelManager != null) services.AddSingleton(modelManager);
        }

        // 调用额外的服务配置委托
        configureServices?.Invoke(services);

        var sp = services.BuildServiceProvider();

        Logger.SetLogger(sp.GetRequiredService<ILoggerFactory>());
        Logger.SetSerializer(LuBanLoggingServiceExtensions.CreateLuBanSerializer());

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
            var existing = await workspaceRepo.GetByRootPathAsync(cwd);
            string workspaceId;
            if (existing == null)
            {
                var ws = await workspaceManager.CreateWorkspaceAsync(cwd, type: "Normal");
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

    private sealed class DelegateWorkspaceContextProvider(Func<string?> getWorkspaceId)
        : IWorkspaceContextProvider
    {
        public string? CurrentWorkspaceId => getWorkspaceId();
    }
}
