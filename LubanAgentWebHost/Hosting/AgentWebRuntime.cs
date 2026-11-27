/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： AgentWebRuntime
*描述：WebHost 进程级运行时：根容器、按用户惰性初始化、回合取消登记表
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// WebHost 进程级运行时。
/// </summary>
/// <remarks>
/// SAEA 的控制器由 <c>Activator.CreateInstance</c> 创建并永久缓存（单例、无状态），
/// 因此所有进程级状态都挂在本类上，控制器只做参数校验与转发。
/// </remarks>
public sealed class AgentWebRuntime : IAsyncDisposable
{
    private readonly AgentHostServices _host;
    private readonly ConcurrentDictionary<string, Lazy<Task>> _userPreparation = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionCreationGates = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();

    private AgentWebRuntime(AgentHostServices host, WebHostSettings settings)
    {
        _host = host;
        Settings = settings;
        Tokens = JwtTokenService.LoadOrCreate(
            string.IsNullOrWhiteSpace(settings.JwtKeyPath) ? AgentHome.WebHostJwtKeyFile : settings.JwtKeyPath!);
        Auth = new UserAuthService(
            Tokens,
            TimeSpan.FromMinutes(settings.SessionTokenMinutes),
            TimeSpan.FromHours(settings.SessionMaxHours));
        UserId = string.IsNullOrWhiteSpace(settings.UserId)
            ? LocalUserDefaults.DefaultUserId
            : settings.UserId.Trim();
        Runs = new AgentRunHub(userId => _host.Root.CreateUserScope(userId), log: Log);
    }

    /// <summary>当前进程的运行时（<see cref="Program"/> 启动时创建）。</summary>
    public static AgentWebRuntime Current { get; private set; } = null!;

    /// <summary>启动参数。</summary>
    public WebHostSettings Settings { get; }

    /// <summary>
    /// Web 浏览器登录令牌（HS256 JWT）的签发与校验。
    /// </summary>
    public JwtTokenService Tokens { get; }

    /// <summary>
    /// 用户注册、登录与内置管理员维护。
    /// </summary>
    public UserAuthService Auth { get; }

    /// <summary>宿主身份（单机单用户）。</summary>
    public string UserId { get; }

    /// <summary>入口短码表：二维码只编码 <c>/e/{code}</c>，宿主内映射回带令牌的地址。</summary>
    public EntryLinkService EntryLinks { get; } = new();

    /// <summary>进程关闭令牌。</summary>
    public CancellationToken ShutdownToken => _shutdown.Token;

    /// <summary>进行中轮次的登记中心。</summary>
    public AgentRunHub Runs { get; }

    /// <summary>
    /// 取该用户的「会话补建」互斥闸门。
    /// </summary>
    /// <remarks>
    /// 一轮对话未指定会话时会「复用最近会话，没有则新建」；并发请求同时走到这一步会各建一条同标题空会话。
    /// 闸门保证同一用户同一时刻只有一个请求在「查最近 + 补建」区间内执行。
    /// </remarks>
    /// <param name="userId">用户标识。</param>
    /// <returns>该用户专属的信号量（进程内复用，不释放）。</returns>
    public SemaphoreSlim GetSessionCreationGate(string userId)
        => _sessionCreationGates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    /// <summary>创建并登记运行时。</summary>
    /// <param name="host">已构建的宿主服务。</param>
    /// <param name="settings">启动参数。</param>
    /// <returns>运行时。</returns>
    public static AgentWebRuntime Create(AgentHostServices host, WebHostSettings settings)
    {
        var runtime = new AgentWebRuntime(host, settings);
        Current = runtime;
        return runtime;
    }

    /// <summary>按用户派生根作用域；调用方负责释放。</summary>
    /// <param name="userId">用户标识。</param>
    /// <returns>已回填身份的作用域容器。</returns>
    public DiContainer CreateRequestScope(string userId) => _host.Root.CreateUserScope(userId);

    /// <summary>
    /// 确保该用户的工作区与插件已初始化（每个用户只执行一次）。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <returns>初始化任务。</returns>
    public async Task EnsureUserReadyAsync(string userId)
    {
        var lazy = _userPreparation.GetOrAdd(
            userId, key => new Lazy<Task>(() => PrepareUserAsync(key), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // 初始化失败不缓存：否则该用户后续请求（含健康检查）会永久 503，只能重启进程
            _userPreparation.TryRemove(new KeyValuePair<string, Lazy<Task>>(userId, lazy));
            throw;
        }
    }

    private async Task PrepareUserAsync(string userId)
    {
        await using var scope = _host.Root.CreateUserScope(userId);
        var workspaceManager = scope.GetRequiredService<IWorkspaceManager>();

        // WebHost 无人值守：授权提示一律放行（与桌面端启动装配同口径）
        if (workspaceManager is WorkspaceManager concrete)
        {
            concrete.AuthorizationPrompt = _ => Task.FromResult(true);
        }

        var notices = new List<string>();
        // 首启工作区落在 Web 自己的数据目录下，与机器级工作区根目录白名单（默认 ~/.luban-agent）保持一致
        await AgentHostBuilder.InitializeWorkspaceAsync(
            scope,
            notices,
            Path.Combine(AgentHome.WorkspacesDirectory, "default"));
        foreach (var notice in notices)
        {
            Log(notice);
        }

        // worktree 启动对账：登记与磁盘不一致时只记日志（不自动修改）
        try
        {
            var reconciler = scope.GetService<WorktreeReconciler>();
            if (reconciler is not null)
            {
                foreach (var message in await reconciler.RunAsync())
                {
                    Log("worktree 对账: " + message);
                }
            }
        }
        catch (Exception ex)
        {
            Log("worktree 启动对账失败：" + ex.Message);
        }

        // A2A：工作区确定后按工作区并集配置启动服务端/客户端角色（未启用时为 no-op）
        try
        {
            var lifecycle = scope.GetService<IA2ALifecycle>();
            if (lifecycle is not null)
            {
                await lifecycle.StartAsync(workspaceManager.CurrentWorkspace?.RootPath);
            }
        }
        catch (Exception ex)
        {
            Log("启动 A2A 失败：" + ex.Message);
        }

        await PluginBootstrap.InitializeAsync(scope, Log, PluginRoots());

        // 插件页面镜像：输出目录可能被重建或覆盖，启动时整棵重建（幂等）
        try
        {
            await WebPluginService.RebuildMirrorsAsync(scope).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log("插件页面镜像重建失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 需要扫描的插件根：Web 自己的插件目录 + 客户端（桌面端）下发的插件目录。
    /// </summary>
    /// <returns>去重后的插件根集合。</returns>
    private IReadOnlyList<string> PluginRoots()
    {
        var roots = new List<string>
        {
            WebPluginPaths.ResolveWebPluginsRoot(Settings.WebPluginsDir),
            WebPluginPaths.ResolveDesktopPluginsRoot(Settings.PluginsDir)
        };

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>取消指定会话正在进行的回合。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="runId">本轮标识；非空时只有登记的仍是本轮才取消。</param>
    /// <returns>是否确有回合被取消。</returns>
    public bool CancelRun(string userId, string sessionId, string? runId = null)
        => Runs.Cancel(userId, sessionId, runId);

    /// <summary>输出宿主诊断信息（标准输出由启动器读取，仅作为日志）。</summary>
    /// <param name="message">文案。</param>
    public static void Log(string message) => Console.WriteLine($"[webhost] {message}");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // 先让各订阅者泵收到关闭信号，再等轮次收尾（超时只记日志）
        _shutdown.Cancel();
        await Runs.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        await _host.DisposeAsync().ConfigureAwait(false);
    }
}