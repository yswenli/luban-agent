/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Runtime
*文件名： A2ARuntimeLifecycle
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A 运行时生命周期实现，解析工作区并集配置并驱动服务端/客户端角色注册
*
*****************************************************************************/
using LuBan.AIAgent.A2A;
using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.A2A.Models;
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;

using LubanAgentA2A.Client;
using LubanAgentA2A.Configuration;
using LubanAgentA2A.Executor;
using LubanAgentA2A.InProcess;
using LubanAgentA2A.Security;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LubanAgentA2A.Runtime;

/// <summary>
/// A2A 运行时生命周期。由宿主在工作区确定后调用 <see cref="StartAsync"/>：
/// <list type="number">
/// <item>以 appsettings 的 <see cref="A2AOptions"/> 为基线，与工作区 <c>.luban-agent/a2a/*.json</c> 并集解析出有效配置；</item>
/// <item>写回 <see cref="A2ARuntimeState"/>，登记本地进程内 Agent（供内部子代理经 A2A 抽象执行）；</item>
/// <item>有 server 角色时启动 <see cref="IA2AServerHost"/>；</item>
/// <item>每个 client 角色建立 <see cref="RemoteSubAgentExecutor"/> 并注册到运行时映射（可选注册为 SubAgent 角色）。</item>
/// </list>
/// </summary>
public sealed class A2ARuntimeLifecycle : IA2ALifecycle
{
    private readonly IOptions<A2AOptions> _baseline;
    private readonly A2ARuntimeState _state;
    private readonly IA2AAgentRegistry _registry;
    private readonly IA2AServerHost _serverHost;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SubAgentRoleRegistry _roleRegistry;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<A2ARuntimeLifecycle>? _logger;

    private readonly List<HttpClient> _clients = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 创建 A2A 生命周期实例。
    /// </summary>
    /// <param name="baseline">appsettings 绑定的框架基线与总开关。</param>
    /// <param name="state">运行时状态。</param>
    /// <param name="registry">进程内 Agent 注册表。</param>
    /// <param name="serverHost">服务端宿主。</param>
    /// <param name="scopeFactory">外层容器作用域工厂（本地句柄适配器据此解析 Scoped 服务）。</param>
    /// <param name="roleRegistry">SubAgent 角色注册表。</param>
    /// <param name="loggerFactory">日志工厂（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public A2ARuntimeLifecycle(
        IOptions<A2AOptions> baseline,
        A2ARuntimeState state,
        IA2AAgentRegistry registry,
        IA2AServerHost serverHost,
        IServiceScopeFactory scopeFactory,
        SubAgentRoleRegistry roleRegistry,
        ILoggerFactory? loggerFactory = null,
        ILogger<A2ARuntimeLifecycle>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(serverHost);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(roleRegistry);

        _baseline = baseline;
        _state = state;
        _registry = registry;
        _serverHost = serverHost;
        _scopeFactory = scopeFactory;
        _roleRegistry = roleRegistry;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsRunning => _state.IsStarted;

    /// <inheritdoc />
    public async Task StartAsync(string? workspaceRoot, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var normalizedRoot = NormalizeRoot(workspaceRoot);

            // 已按同一工作区启动：幂等短路。工作区变化时先停旧运行时，再按新根重建，
            // 避免换工作区后沿用旧根与旧角色（本地 agent 名 / 工作区角色 / 远端执行器）。
            if (_state.IsStarted && string.Equals(_state.WorkspaceRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_state.IsStarted)
            {
                _logger?.LogInformation("A2A 工作区已变化，重建运行时：{Old} → {New}", _state.WorkspaceRoot, normalizedRoot);
                await StopCoreAsync(ct).ConfigureAwait(false);
            }

            var effective = Agent2AgentConfigResolver.Resolve(_baseline.Value, workspaceRoot);
            _state.Update(effective);
            _state.SetWorkspaceRoot(normalizedRoot);

            if (!effective.Enabled)
            {
                _logger?.LogInformation("A2A 未启用（Enabled=false），跳过角色注册。");
                return;
            }

            var agentName = string.IsNullOrWhiteSpace(effective.Server?.AgentName)
                ? "luban-agent"
                : effective.Server!.AgentName;
            _state.SetLocalAgentName(agentName);

            RegisterLocalAgent(agentName, effective.Server, workspaceRoot);

            if (effective.HasServerRole)
            {
                await _serverHost.StartAsync(ct).ConfigureAwait(false);
                _logger?.LogInformation("A2A 服务端角色已启动：{Endpoint}", _serverHost.Endpoint);
            }

            foreach (var client in effective.Clients)
            {
                RegisterClient(client);
            }

            _state.SetStarted(true);
            _logger?.LogInformation(
                "A2A 已启动：agent={Agent}，server={Server}，clients={Clients}",
                agentName, effective.HasServerRole, effective.Clients.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_state.IsStarted)
            {
                return;
            }

            await StopCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 停止服务端/客户端并复位运行时状态。调用方须已持有 <see cref="_gate"/>，
    /// 以便 <see cref="StartAsync"/> 在锁内安全重建。
    /// </summary>
    private async Task StopCoreAsync(CancellationToken ct)
    {
        await _serverHost.StopAsync(ct).ConfigureAwait(false);

        foreach (var client in _clients)
        {
            client.Dispose();
        }
        _clients.Clear();

        _state.ClearRemoteExecutors();
        _state.SetWorkspaceRoot(null);
        _state.SetStarted(false);
        _logger?.LogInformation("A2A 已停止。");
    }

    /// <summary>
    /// 规范化工作区根：空白返回 null；否则取绝对路径，用于跨调用比较。
    /// </summary>
    private static string? NormalizeRoot(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }
        try
        {
            return Path.GetFullPath(workspaceRoot);
        }
        catch
        {
            return workspaceRoot;
        }
    }

    /// <summary>
    /// 登记本地进程内 Agent：解析工作区角色（修复 <c>SubAgentRoleRegistry</c> 生产未加载的缺口），
    /// 并以角色技能构建描述符，令服务端 AgentCard 与内部编排均可感知角色。
    /// </summary>
    private void RegisterLocalAgent(string agentName, A2AServerOptions? server, string? workspaceRoot)
    {
        TryLoadWorkspaceRoles(workspaceRoot);

        var skills = _roleRegistry.GetAllRoles()
            .Select(role => new A2ASkillDescriptor
            {
                Id = role.Name,
                Name = role.Name,
                Description = null,
                Tags = role.DefaultToolGroups is { Count: > 0 } ? new List<string>(role.DefaultToolGroups) : null,
            })
            .ToList();

        var descriptor = new A2AAgentDescriptor
        {
            Name = agentName,
            Description = server?.AgentDescription,
            Version = server?.AgentVersion ?? "1.0.0",
            Skills = skills,
            DefaultInputModes = new List<string> { "text/plain" },
            DefaultOutputModes = new List<string> { "text/plain" },
            SupportsExtendedCard = false,
            Extensions = new List<string> { "luban://a2a/ext/input-required" },
        };

        var handler = new InternalSubAgentHandlerAdapter(
            _scopeFactory,
            _loggerFactory?.CreateLogger<InternalSubAgentHandlerAdapter>());
        _registry.Register(descriptor, handler);
    }

    /// <summary>
    /// 从工作区加载自定义角色。失败仅记录，不阻断 A2A 启动。
    /// </summary>
    private void TryLoadWorkspaceRoles(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return;
        }

        try
        {
#pragma warning disable IL2026 // 角色 JSON 反序列化依赖反射；LubanAgentA2A 非 trimmable
            var count = _roleRegistry.LoadFromWorkspace(workspaceRoot!);
#pragma warning restore IL2026
            _logger?.LogInformation("已从工作区加载 {Count} 个 SubAgent 角色。", count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "从工作区加载 SubAgent 角色失败，使用内置角色。");
        }
    }

    /// <summary>
    /// 注册一个远端客户端角色：建立带鉴权头的 <see cref="HttpClient"/> 与客户端/执行器，
    /// 写入运行时映射；<c>registerAsSubAgent=true</c> 时同时注册为 SubAgent 角色以便编排选中。
    /// </summary>
    private void RegisterClient(A2AClientOptions client)
    {
        if (string.IsNullOrWhiteSpace(client.Name) || string.IsNullOrWhiteSpace(client.Endpoint))
        {
            _logger?.LogWarning("跳过无效的 A2A 客户端配置（name/endpoint 为空）。");
            return;
        }

        var http = new HttpClient();
        if (!string.IsNullOrEmpty(client.ApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyAuthMiddleware.HeaderName, client.ApiKey);
        }
        _clients.Add(http);

        var adapter = new A2AClientAdapter(
            client.Name,
            client.Endpoint,
            http,
            client.RemoteAgentName,
            _loggerFactory?.CreateLogger<A2AClientAdapter>());
        _state.SetRemoteExecutor(client.Name, new RemoteSubAgentExecutor(adapter));

        if (client.RegisterAsSubAgent)
        {
            _roleRegistry.Register(new SubAgentRole
            {
                Name = client.Name,
                SystemPromptTemplate = $"你是一个远端 A2A 代理「{client.Name}」的调度入口，请把以下任务转交远端处理。任务：{{prompt}}",
                DefaultToolGroups = new List<string>(),
            });
        }

        _logger?.LogInformation(
            "A2A 客户端角色已注册：name={Name}，endpoint={Endpoint}，registerAsSubAgent={Register}",
            client.Name, client.Endpoint, client.RegisterAsSubAgent);
    }
}