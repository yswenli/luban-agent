/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent
*文件名： Program
*版本号： V1.0.0.0
*唯一标识：程序主入口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：程序主入口
*
*****************************************************************************/
namespace LubanAgent;

/// <summary>
/// 程序入口
/// </summary>
class Program
{
    /// <summary>
    /// 程序主入口
    /// </summary>
    static async Task Main(string[] args)
    {
        ConsoleUtil.PrintName();

        var configuration = BuildConfiguration(args);
        // 先将全局配置设置到 ConfigUtil，确保 LuBanOrm 静态构造时能从程序目录加载 appsettings.json
        configuration.InitConfigUtil();
        
        // 初始化 ProviderHelper，从配置文件加载 Provider 配置
        ProviderHelper.Initialize(configuration);
        
        DatabaseInitializer.Initialize();

        var (embedder, modelManager) = await PrepareRetrievalAsync(configuration);
        using var serviceProvider = BuildServiceProvider(configuration, embedder, modelManager);

        // 隐式创建/恢复工作区
        try
        {
            // 使用绝对路径，避免 GetByRootPathAsync 查询与 CreateWorkspaceAsync 存储路径不一致
            var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
            var workspaceRepo = serviceProvider.GetRequiredService<WorkspaceRepository>();
            var workspaceManager = serviceProvider.GetRequiredService<IWorkspaceManager>();
            var existing = await workspaceRepo.GetByRootPathAsync(cwd);
            if (existing == null)
            {
                var ws = await workspaceManager.CreateWorkspaceAsync(cwd, type: "Normal");
                await workspaceManager.SetCurrentAsync(ws.WorkspaceId);
                Console.WriteLine($"已创建工作区: {ws.Name} ({ws.RootPath})");
            }
            else
            {
                await workspaceManager.SetCurrentAsync(existing.WorkspaceId);
                Console.WriteLine($"当前工作区: {existing.Name} ({existing.RootPath})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"工作区初始化失败: {ex.Message}");
        }

        var appService = serviceProvider.GetRequiredService<ConsoleAppService>();

        // 支持命令行参数直接执行命令，例如：LuBanAgent /se -s 新会话
        if (args.Length > 0 && IsDirectCommand(args[0]))
        {
            await appService.RunDirectAsync(args);
        }
        else
        {
            await appService.RunAsync();
        }
    }

    /// <summary>
    /// 判断参数是否为直接执行命令（以 / 开头且非配置参数）。
    /// </summary>
    /// <param name="arg">首个命令行参数。</param>
    /// <returns>是直接命令返回 true，否则 false。</returns>
    private static bool IsDirectCommand(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return false;
        // 以 / 开头且长度大于 1（排除单纯的 /）
        // 排除配置参数（如 --key=value 形式）
        return arg.StartsWith('/') && arg.Length > 1 && !arg.StartsWith("--");
    }

    private static IConfiguration BuildConfiguration(string[] args)
    {
        // 优先使用程序所在目录的 appsettings.json，确保在其他目录启动时也能正确加载配置
        var baseDir = AppContext.BaseDirectory;
        var builder = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        return builder.Build();
    }

    private static async Task<(OnnxEmbeddingGenerator? embedder, ModelManager? modelManager)> PrepareRetrievalAsync(IConfiguration configuration)
    {
        var retrieval = configuration.GetSection("LuBanAgent:Tools:Retrieval").Get<RetrievalToolOptions>() ?? new RetrievalToolOptions();
        if (!retrieval.Enabled) return (null, null);
        var spec = EmbeddingModelCatalog.Find(retrieval.ModelId);
        if (spec == null)
        {
            Console.WriteLine($"未知的嵌入模型：{retrieval.ModelId}，检索功能已禁用");
            return (null, null);
        }
        var mm = new ModelManager(spec);
        if (mm.IsModelReady()) return (new OnnxEmbeddingGenerator(mm.ModelDirectory, spec), mm);
        var ok = await ConsoleUtil.RunWithStatusAsync<bool>(
            async (update, ct) => await mm.EnsureModelAsync(update, ct),
            "准备本地的嵌入模型…");
        if (!ok || !mm.IsModelReady())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"本地嵌入模型 {spec.ModelId} 未就绪，检索功能已禁用");
            Console.WriteLine($"请将模型包放到: {mm.LocalZipPath}");
            Console.ResetColor();
            Console.WriteLine();
            return (null, null);
        }
        return (new OnnxEmbeddingGenerator(mm.ModelDirectory, spec), mm);
    }

    private static ServiceProvider BuildServiceProvider(IConfiguration configuration, OnnxEmbeddingGenerator? embedder, ModelManager? modelManager)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var configPath = ConfigManager.GetDefaultConfigPath();
        var configManager = new ConfigManager(configPath);
        configManager.Load();
        services.AddSingleton(configManager);
        services.AddSingleton<IAppConfigReader>(configManager);

        // 注册 LuBan 文件日志
        services.AddLogging(builder => builder.AddLuBanFileLogger());

        // 注册 IChatClient，使用 ConfigManager 动态创建
        services.AddScoped<IChatClient>(sp =>
        {
            var cm = sp.GetRequiredService<ConfigManager>();
            return cm.CreateChatClient();
        });

        // 注册 IProviderRouter
        services.AddSingleton<IProviderRouter>(sp =>
        {
            var cm = sp.GetRequiredService<ConfigManager>();
            var providers = new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase);
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
                    providers[p.Name] = openAIClient.GetChatClient("default").AsIChatClient();
                }
            }
            return new LuBanChatClient(providers);
        });

        // 注册 ILocalMemoryStore（由 CLI 提供 SQLite 实现）
        services.AddSingleton<LuBan.AIAgent.LocalMemory.ILocalMemoryStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LuBan.AIAgent.Configuration.LocalMemoryOptions>>().Value;
            var dbPath = opts.DatabasePath;
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dbPath = Path.Combine(appData, "LuBan", "AIAgent", "localmemory.db");
            }
            return new LubanAgent.Infrastructure.SqliteLocalMemoryStore(dbPath);
        });

        services.AddLuBanAgent(configuration);

        services.AddScoped<LuBan.AIAgent.LuBanAgentFactory>();
        services.AddScoped<LuBan.AIAgent.ILuBanAgentFactory>(sp => sp.GetRequiredService<LuBan.AIAgent.LuBanAgentFactory>());
        services.AddScoped<LuBan.AIAgent.Orchestration.SubAgentFactory>();
        services.AddScoped<LuBan.AIAgent.Orchestration.ContextStore>();
        services.AddScoped<LuBan.AIAgent.Orchestration.DagScheduler>();
        services.AddScoped<LuBan.AIAgent.Orchestration.Planner.TemplateTaskPlanner>();
        services.AddScoped<LuBan.AIAgent.Orchestration.Planner.LlmTaskPlanner>();
        services.AddScoped<LuBan.AIAgent.Orchestration.Planner.CompositeTaskPlanner>();
        services.AddScoped<LuBan.AIAgent.Orchestration.Planner.ITaskPlanner>(sp => sp.GetRequiredService<LuBan.AIAgent.Orchestration.Planner.CompositeTaskPlanner>());
        services.AddScoped<LuBan.AIAgent.Orchestration.IOrchestrator, LuBan.AIAgent.Orchestration.Orchestrator>();

        services.AddSingleton<ISessionManager, SessionManager>();

        // 注册工作区服务
        services.AddSingleton<SessionRepository>();
        services.AddSingleton<SessionMessageRepository>();
        services.AddSingleton<WorkspaceRepository>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<LuBan.AIAgent.LocalMemory.IWorkspaceContextProvider>(
            new DelegateWorkspaceContextProvider(() => WorkspaceManager.Current?.WorkspaceId));

        if (embedder != null)
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embedder);
            services.AddSingleton<IVectorStore, SqliteVectorStore>();
            services.AddSingleton<IRetrievalService>(sp => new RetrievalService(
                sp.GetRequiredService<IVectorStore>(),
                sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                sp.GetRequiredService<IOptions<LuBanAgentOptions>>()));
            if (modelManager != null) services.AddSingleton(modelManager);
        }

        services.AddSingleton<ConsoleAppService>();

        var sp = services.BuildServiceProvider();

        // 注入 ILoggerFactory 和 STJ 序列化器给 static Logger
        Logger.SetLogger(sp.GetRequiredService<ILoggerFactory>());
        Logger.SetSerializer(LuBanLoggingServiceExtensions.CreateLuBanSerializer());

        return sp;
    }

    private sealed class DelegateWorkspaceContextProvider(Func<string?> getWorkspaceId)
        : LuBan.AIAgent.LocalMemory.IWorkspaceContextProvider
    {
        public string? CurrentWorkspaceId => getWorkspaceId();
    }
}
