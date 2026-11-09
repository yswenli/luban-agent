/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： AgentClientConnection
*描述：一次到 WebHost 的连接（传输层 + 会话/工作区/知识库/状态的 HTTP 实现 + 可选子进程生命周期）
*
*****************************************************************************/

using LubanAgentCore.Configuration;
using LubanAgentCore.Services.Management;

namespace LubanAgentCore.Client;

/// <summary>
/// 客户端到 WebHost 的连接。
/// </summary>
/// <remarks>
/// 会话、工作区、知识库、对话与 A2A 状态都经本对象里的实现转发给 WebHost；
/// 若地址是自动拉起得来的，<see cref="DisposeAsync"/> 会一并结束子进程。
/// </remarks>
public sealed class AgentClientConnection : IAsyncDisposable
{
    private readonly ServerProcessLauncher? _launcher;
    private int _disposed;

    /// <summary>创建连接。</summary>
    /// <param name="client">传输层客户端。</param>
    /// <param name="sessions">会话实现。</param>
    /// <param name="workspaces">工作区实现。</param>
    /// <param name="launcher">自动拉起的子进程；直连既有服务时为 null。</param>
    internal AgentClientConnection(
        AgentServerClient client,
        HttpSessionManager sessions,
        HttpWorkspaceManager workspaces,
        ServerProcessLauncher? launcher)
    {
        Client = client;
        Sessions = sessions;
        Workspaces = workspaces;
        LlmWiki = new HttpLlmWikiManagementService(client);
        A2AStatus = new HttpA2AStatusProvider(client);
        _launcher = launcher;
    }

    /// <summary>服务地址。</summary>
    public Uri BaseAddress => Client.BaseAddress;

    /// <summary>传输层客户端（健康检查、非流式对话等直接用）。</summary>
    public AgentServerClient Client { get; }

    /// <summary>会话管理的 HTTP 实现。</summary>
    public HttpSessionManager Sessions { get; }

    /// <summary>工作区管理的 HTTP 实现。</summary>
    public HttpWorkspaceManager Workspaces { get; }

    /// <summary>LLM Wiki 知识库管理的 HTTP 实现。</summary>
    public HttpLlmWikiManagementService LlmWiki { get; }

    /// <summary>A2A 运行状态的 HTTP 实现（只读）。</summary>
    public HttpA2AStatusProvider A2AStatus { get; }

    /// <summary>
    /// 创建绑定到本连接的宿主实现。
    /// </summary>
    /// <param name="services">客户端本地容器；<see cref="IAgentHost.Services"/> 会原样返回它。</param>
    /// <returns>宿主实现。</returns>
    public HttpAgentHost CreateAgentHost(DiContainer services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new HttpAgentHost(services, Client, Sessions);
    }

    /// <summary>
    /// 用 HTTP 实现替换容器里的会话 / 工作区 / 宿主注册。
    /// </summary>
    /// <remarks>
    /// 必须在容器 <c>Build()</c> 之前调用：容器构建后注册集合不可再变更。
    /// </remarks>
    /// <param name="services">尚未构建的根容器。</param>
    /// <returns>同一容器，便于链式调用。</returns>
    public DiContainer RegisterInto(DiContainer services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(this);
        services.AddSingleton(Client);
        services.AddSingleton(Sessions);
        services.AddSingleton(Workspaces);
        services.AddSingleton<HttpSessionWorkspaceQuery>();
        services.AddSingleton<HttpSessionMessageQuery>();
        services.AddSingleton(LlmWiki);
        services.AddSingleton(A2AStatus);
        services.ReplaceScoped<ISessionManager>(_ => Sessions);
        services.ReplaceScoped<IWorkspaceManager>(_ => Workspaces);
        services.ReplaceScoped<ILlmWikiManagementService>(_ => LlmWiki);
        services.ReplaceScoped<IA2AStatusProvider>(_ => A2AStatus);
        // 查询缝在原注册里是单例：用 ReplaceSingleton 保持生存期一致，避免降为 scoped 后
        // 被单例消费者形成 captive dependency
        services.ReplaceSingleton<ISessionWorkspaceQuery>(sp => sp.GetRequiredService<HttpSessionWorkspaceQuery>());
        services.ReplaceSingleton<ISessionMessageQuery>(sp => sp.GetRequiredService<HttpSessionMessageQuery>());
        // 用户状态桶（凭据、Provider、会话/工作区管理器）：本进程没有数据库，
        // 会话与工作区必须换成 HTTP 实现，否则构造 UserBucket 时即触发「LuBanOrm 未初始化」
        services.ReplaceSingleton<UserContextRegistry>(sp => new UserContextRegistry(sp)
        {
            SessionManagerFactory = _ => Sessions,
            WorkspaceManagerFactory = (_, _) => Workspaces
        });
        services.ReplaceScoped<IAgentHost>(sp => new HttpAgentHost(sp.AsDiContainer(), Client, Sessions));
        return services;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // 先结束宿主子进程再释放 HttpClient：反过来的话，在途回合收尾时的 /agent/cancel
        // 只能打到已释放的客户端上，异常只能降级成告警。
        if (_launcher is not null)
        {
            await _launcher.DisposeAsync().ConfigureAwait(false);
        }

        Client.Dispose();
    }
}