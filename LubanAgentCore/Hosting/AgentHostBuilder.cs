/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Hosting
*文件名： AgentHostBuilder
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/29
*描述：Agent 宿主构建器：提供 DI 容器构建与工作区初始化，供 TUI/GUI 共用。
*
*=================================================
*修改标记
*修改时间：2026/7/29
*修改人： yswenli
*版本号： V1.0.0.0
*描述：Agent 宿主构建器：提供 DI 容器构建与工作区初始化，供 TUI/GUI 共用。
*
*****************************************************************************/

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
    public static AgentHostServices Build(
        IConfiguration configuration,
        OnnxEmbeddingGenerator? embedder = null,
        ModelManager? modelManager = null,
        Action<DiContainer>? configureServices = null,
        bool migrateLegacyUserData = true)
    {
        var services = new DiContainer();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging(builder => builder.AddLuBanFileLogger());

        // ── root：身份无关的进程级单例 ─────────────────────────────────
        services.AddSingleton<SessionRepository>();
        services.AddSingleton<SessionMessageRepository>();
        services.AddSingleton<WorkspaceRepository>();
        services.AddSingleton<LlmWikiFileRepository>();
        services.AddSingleton<LlmWikiChunkRepository>();

        // 管理服务只依赖最小仓储缝（便于脱离数据库做单测），由具体仓储解析
        services.AddSingleton<ISessionWorkspaceQuery>(sp => sp.GetRequiredService<SessionRepository>());
        services.AddSingleton<ISessionMessageQuery>(sp => sp.GetRequiredService<SessionMessageRepository>());
        services.AddSingleton<ILlmWikiFileStore>(sp => sp.GetRequiredService<LlmWikiFileRepository>());
        services.AddSingleton<ILlmWikiChunkStore>(sp => sp.GetRequiredService<LlmWikiChunkRepository>());
        services.AddSingleton<WorktreeSessionRepository>();
        services.AddSingleton<GitProcess>();
        services.AddSingleton<UserContextRegistry>();

        services.AddSingleton<ILocalMemoryStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LocalMemoryOptions>>().Value;
            return new SqliteLocalMemoryStore(ResolveMemoryDbPath(opts));
        });

        services.AddLuBanAgentDefaults(configuration);
        services.AddLuBanAgentHost();

        // 产品侧厂商连接器（云效）：框架只保留「抽象连接器 + 连接器管理」，
        // 厂商实现由宿主显式注册（含提供方工厂与凭据键别名）。
        services.AddAliyunDevOpsConnector(configuration);

        // ── 插件宿主：贡献记账 + 作用域访问桥 + 宿主实现 ───────────────────
        // 三者必须为单例：框架 PluginManager 为单例并经根容器延迟解析 IPluginHost，
        // 其构造链不得含 scoped 服务（SkillRegistry / MCPRegistry 均按会话 scope 隔离，
        // 只能经 PluginHostContext 的会话 scope 触达）。
        services.AddSingleton<PluginHostRegistry>();
        services.AddSingleton<PluginHostContext>();
        // UI 线程封送：默认就地执行；桌面宿主用 Replace 覆盖为平台实现。
        services.TryAddSingleton<IPluginUiDispatcher, ImmediatePluginUiDispatcher>();
        services.AddSingleton<AgentPluginHost>();
        services.AddSingleton<IPluginHost>(sp => sp.GetRequiredService<AgentPluginHost>());
        services.AddSingleton<IPluginSessionContributor>(sp => sp.GetRequiredService<AgentPluginHost>());

        // ── user scope：身份与用户级服务 ──────────────────────────────
        services.AddScoped<UserContextState>();
        services.AddScoped<LuBan.AIAgent.Abstractions.IUserContext>(sp =>
            new UserContext(sp.GetRequiredService<UserContextState>()));
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
        // 连接器声明写入器：产品实现覆盖框架默认（TryAddScoped），统一「写入 → 失效 → 通知」链路
        services.AddScoped<IConnectorConfigWriter>(sp => sp.GetRequiredService<ConfigManager>());

        services.AddScoped<IWorktreeCoordinator, WorktreeSessionManager>();
        services.AddScoped<WorktreeReconciler>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<IAgentsMdInitializer, AgentsMdInitializer>();

        // ── 框架宿主接缝：产品实现（后注册者生效，覆盖框架 TryAdd 默认）─────
        services.AddScoped<IAgentWorkspaceResolver, CoreWorkspaceResolver>();
        services.AddScoped<IAgentProfileResolver, CoreAgentProfileResolver>();
        services.AddScoped<IAgentUserResolver, CoreAgentUserResolver>();
        services.AddScoped<IAgentModelSelection>(sp => sp.GetRequiredService<ConfigManager>());
        services.AddScoped<IModelCapabilityGate, VisionModelCapabilityGate>();
        services.AddScoped<IAgentAttachmentOrchestrator, CoreAttachmentOrchestrator>();
        services.AddScoped<ISessionRuntimeInitializer, CoreSessionRuntimeInitializer>();
        services.AddScoped<IAgentSessionExtension, PluginSessionExtension>();
        services.AddScoped<IAgentSessionExtension, ConnectorSessionExtension>();

        // ── 管理服务：CLI 与 Codex 共用的唯一业务入口（Task 13 建契约，14–19 填业务）──
        services.AddScoped<IProviderModelManagementService, ProviderModelManagementService>();
        services.AddScoped<ISkillManagementService, SkillManagementService>();
        services.AddScoped<IRuleManagementService, RuleManagementService>();
        services.AddScoped<IMcpManagementService, McpManagementService>();
        services.AddScoped<IConnectorManagementService, ConnectorManagementService>();
        services.AddScoped<IWorkspaceManagementService, WorkspaceManagementService>();
        services.AddScoped<IChatSessionService, ChatSessionService>();
        services.AddScoped<ILlmWikiManagementService, LlmWikiManagementService>();
        services.AddScoped<IPluginManagementService, PluginManagementService>();
        services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();

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

        var root = services.Build();

        Logger.SetLogger(root.GetRequiredService<ILoggerFactory>());
        Logger.SetSerializer(LuBanLoggingServiceExtensions.CreateLuBanSerializer());

        // 历史单用户数据归属归一化（会话/工作区 UserId: null/''/'default' → 默认用户）
        // 远程客户端不持有本地库，跳过以免创建只写不读的 SQLite 文件。
        if (migrateLegacyUserData) LegacyUserDataMigrator.Migrate();

        // 进程常驻用户作用域：CLI/Codex 单用户身份在此绑定；session scope 由 SessionRuntimeManager 从 root 派生并回填身份
        var userScope = root.CreateUserScope(LocalUserDefaults.DefaultUserId);

        return new AgentHostServices(root, userScope);
    }

    /// <summary>
    /// 初始化工作区。
    /// </summary>
    public static async Task InitializeWorkspaceAsync(DiContainer sp, List<string> notices)
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
        /// <summary>
        /// 当前工作区标识，每次读取都委托给构造时传入的取值函数（可为空）。
        /// </summary>
        public string? CurrentWorkspaceId => getWorkspaceId();
    }
}
