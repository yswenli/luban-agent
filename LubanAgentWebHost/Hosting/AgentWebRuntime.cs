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
    private readonly ConcurrentDictionary<string, RunRegistration> _runs = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();

    private AgentWebRuntime(AgentHostServices host, WebHostSettings settings)
    {
        _host = host;
        Settings = settings;
        UserId = string.IsNullOrWhiteSpace(settings.UserId)
            ? LocalUserDefaults.DefaultUserId
            : settings.UserId.Trim();
    }

    /// <summary>当前进程的运行时（<see cref="Program"/> 启动时创建）。</summary>
    public static AgentWebRuntime Current { get; private set; } = null!;

    /// <summary>启动参数。</summary>
    public WebHostSettings Settings { get; }

    /// <summary>宿主身份（单机单用户）。</summary>
    public string UserId { get; }

    /// <summary>进程关闭令牌。</summary>
    public CancellationToken ShutdownToken => _shutdown.Token;

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
        await AgentHostBuilder.InitializeWorkspaceAsync(scope, notices);
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

        await PluginBootstrap.InitializeAsync(scope, Log, Settings.PluginsDir);
    }

    /// <summary>登记一次回合的取消源；同会话已有回合时先取消旧回合。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>本轮取消源与本轮标识（调用方在回合结束后 <see cref="EndRun"/>）。</returns>
    public (CancellationTokenSource Cts, string RunId) BeginRun(string userId, string sessionId)
    {
        var key = RunKey(userId, sessionId);
        var cts = new CancellationTokenSource();
        var runId = Guid.NewGuid().ToString("N");
        if (_runs.TryRemove(key, out var previous))
        {
            TryCancel(previous.Cts);
            previous.Cts.Dispose();
        }

        _runs[key] = new RunRegistration(runId, cts);
        return (cts, runId);
    }

    /// <summary>取消指定会话正在进行的回合。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="runId">本轮标识；非空时只有登记的仍是本轮才取消。</param>
    /// <returns>是否确有回合被取消。</returns>
    public bool CancelRun(string userId, string sessionId, string? runId = null)
    {
        if (!_runs.TryGetValue(RunKey(userId, sessionId), out var run))
        {
            return false;
        }

        // 取消请求迟到（回合已换代）：按标识拒绝，避免误伤刚开的新回合
        if (!string.IsNullOrWhiteSpace(runId) && !string.Equals(run.RunId, runId, StringComparison.Ordinal))
        {
            return false;
        }

        TryCancel(run.Cts);
        return true;
    }

    /// <summary>回合结束：移除登记（仅当登记项仍是本次的取消源）。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cts">本轮取消源。</param>
    public void EndRun(string userId, string sessionId, CancellationTokenSource cts)
    {
        var key = RunKey(userId, sessionId);
        if (_runs.TryGetValue(key, out var current) && ReferenceEquals(current.Cts, cts))
        {
            _runs.TryRemove(key, out _);
        }

        cts.Dispose();
    }

    private static string RunKey(string userId, string sessionId) => $"{userId}\u0001{sessionId}";

    private static void TryCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已被回合收尾释放，忽略
        }
    }

    /// <summary>输出宿主诊断信息（标准输出由启动器读取，仅作为日志）。</summary>
    /// <param name="message">文案。</param>
    public static void Log(string message) => Console.WriteLine($"[webhost] {message}");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        foreach (var pair in _runs)
        {
            TryCancel(pair.Value.Cts);
            pair.Value.Cts.Dispose();
        }

        _runs.Clear();
        _shutdown.Dispose();
        await _host.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>一次回合的登记项。</summary>
    /// <param name="RunId">本轮标识（随 <c>session</c> 帧下发，取消时回传）。</param>
    /// <param name="Cts">本轮取消源。</param>
    private sealed record RunRegistration(string RunId, CancellationTokenSource Cts);
}