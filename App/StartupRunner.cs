/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： StartupRunner
*版本号： V1.0.0.0
*唯一标识：启动运行器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：封装应用初始化逻辑，供启动向导调用
*
*****************************************************************************/
using LubanAgentCli.App.Services;

namespace LubanAgentCli.App;

/// <summary>
/// 启动运行器。封装应用初始化逻辑，供启动向导调用。
/// </summary>
internal static class StartupRunner
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
    /// 准备嵌入模型（下载进度通过 report 回调报告）。
    /// </summary>
    public static async Task<(OnnxEmbeddingGenerator? embedder, ModelManager? modelManager)> PrepareRetrievalAsync(
        IConfiguration configuration,
        Action<string> report,
        CancellationToken ct)
    {
        var retrieval = configuration.GetSection("LuBanAgent:Tools:Retrieval").Get<RetrievalToolOptions>() ?? new RetrievalToolOptions();
        if (!retrieval.Enabled) return (null, null);
        var spec = EmbeddingModelCatalog.Find(retrieval.ModelId);
        if (spec == null)
        {
            report($"未知的嵌入模型：{retrieval.ModelId}，检索功能已禁用");
            return (null, null);
        }
        var mm = new ModelManager(spec);
        if (mm.IsModelReady()) return (new OnnxEmbeddingGenerator(mm.ModelDirectory, spec), mm);
        var ok = await mm.EnsureModelAsync(report, ct);
        if (!ok || !mm.IsModelReady())
        {
            report($"本地嵌入模型 {spec.ModelId} 未就绪，检索功能已禁用");
            report($"请将模型包放到: {mm.LocalZipPath}");
            return (null, null);
        }
        return (new OnnxEmbeddingGenerator(mm.ModelDirectory, spec), mm);
    }

    /// <summary>
    /// 构建服务容器。
    /// </summary>
    public static IServiceProvider BuildServiceProvider(
        IConfiguration configuration,
        OnnxEmbeddingGenerator? embedder,
        ModelManager? modelManager)
    {
        // 调试模式：启用 TUI 诊断埋点（慢迭代统计、流式内容类型取证等）
        TuiDiag.Enabled = configuration.GetValue<bool>("LuBanAgent:DebugMode");

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var configPath = ConfigManager.GetDefaultConfigPath();
        var configManager = new ConfigManager(configPath);
        configManager.Load();
        services.AddSingleton(configManager);
        services.AddSingleton<IAppConfigReader>(configManager);

        services.AddLogging(builder => builder.AddLuBanFileLogger());

        services.AddScoped<IChatClient>(sp =>
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

        services.AddSingleton<LuBan.AIAgent.LocalMemory.ILocalMemoryStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LuBan.AIAgent.Configuration.LocalMemoryOptions>>().Value;
            var dbPath = opts.DatabasePath;
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dbPath = Path.Combine(appData, "LuBan", "AIAgent", "localmemory.db");
            }
            return new SqliteLocalMemoryStore(dbPath);
        });

        services.AddLuBanAgent(configuration);

        services.AddScoped<LuBan.AIAgent.LuBanAgentFactory>();
        services.AddScoped<LuBan.AIAgent.ILuBanAgentFactory>(sp => sp.GetRequiredService<LuBan.AIAgent.LuBanAgentFactory>());

        services.AddSingleton<ISessionManager, SessionManager>();

        services.AddSingleton<SessionRepository>();
        services.AddSingleton<SessionMessageRepository>();
        services.AddSingleton<WorkspaceRepository>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<TitleService>();
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
        : LuBan.AIAgent.LocalMemory.IWorkspaceContextProvider
    {
        public string? CurrentWorkspaceId => getWorkspaceId();
    }
}
