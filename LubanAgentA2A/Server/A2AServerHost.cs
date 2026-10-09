/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Server
*文件名： A2AServerHost
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A 服务端宿主，基于 ASP.NET Core + A2A SDK 把本机 Agent 暴露为 A2A HTTP 端点
*
*****************************************************************************/
using System.Net;
using System.Net.Sockets;

using LuBan.AIAgent.A2A;
using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.A2A.Models;

using LubanAgentA2A.InProcess;
using LubanAgentA2A.Mapping;
using LubanAgentA2A.Runtime;
using LubanAgentA2A.Security;
using LubanAgentA2A.Storage;

using A2A.AspNetCore;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sdk = A2A;

namespace LubanAgentA2A.Server;

/// <summary>
/// A2A 服务端宿主。<see cref="StartAsync"/> 时按 <see cref="A2ARuntimeState.Options"/> 中
/// 解析后的服务端参数自建一个轻量 <see cref="WebApplication"/>（不接管外层 Generic Host），注册
/// <see cref="InternalSubAgentHandlerAdapter"/> 与外层容器的 <c>IServiceScopeFactory</c>，
/// 挂载 JSON-RPC 端点（<c>MapA2A</c>）与 well-known AgentCard（<c>MapWellKnownAgentCard</c>）。
/// <para>仅允许绑定回环地址；<see cref="A2AServerOptions.Port"/> 为 0 时预先选一个空闲回环端口，
/// 以保证 AgentCard 内接口地址与真实绑定端口一致。</para>
/// </summary>
public sealed class A2AServerHost : IA2AServerHost
{
    private readonly A2ARuntimeState _state;
    private readonly IA2AAgentRegistry _registry;
    private readonly IServiceScopeFactory _outerScopeFactory;
    private readonly ILoggerFactory? _outerLoggerFactory;
    private readonly ILogger<A2AServerHost>? _logger;

    private WebApplication? _app;
    private string? _endpoint;

    /// <summary>
    /// 创建服务端宿主实例。
    /// </summary>
    /// <param name="state">运行时状态（提供解析后的服务端配置）。</param>
    /// <param name="registry">Agent 注册表（提供对外描述符与技能）。</param>
    /// <param name="outerScopeFactory">外层容器作用域工厂（适配器据此解析本地代理服务）。</param>
    /// <param name="outerLoggerFactory">外层日志工厂（可选，用于把内层日志转发到外层，避免污染 TUI 控制台）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public A2AServerHost(
        A2ARuntimeState state,
        IA2AAgentRegistry registry,
        IServiceScopeFactory outerScopeFactory,
        ILoggerFactory? outerLoggerFactory = null,
        ILogger<A2AServerHost>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(outerScopeFactory);

        _state = state;
        _registry = registry;
        _outerScopeFactory = outerScopeFactory;
        _outerLoggerFactory = outerLoggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsRunning => _app is not null;

    /// <inheritdoc />
    public string? Endpoint => _endpoint;

    /// <inheritdoc />
    public IReadOnlyCollection<A2AAgentDescriptor> GetDescriptors() => _registry.GetAll();

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_app is not null)
        {
            return;
        }

        var server = _state.Options.Server
            ?? throw new InvalidOperationException("未配置 A2A 服务端角色（Server 为空），无法启动服务端。");
        if (!IsLoopbackHost(server.Host))
        {
            throw new InvalidOperationException(
                $"A2A 服务端仅允许绑定回环地址（127.0.0.1 / ::1 / localhost），当前配置：{server.Host}。");
        }

        var port = server.Port > 0 ? server.Port : ResolveFreeLoopbackPort();
        var baseUrl = BuildBaseUrl(server.Host, port);
        var card = BuildAgentCard(server, baseUrl);

        var builder = WebApplication.CreateSlimBuilder();
        ConfigureLogging(builder);

        // 先注册任务存储，令 AddA2AAgent 内部 TryAddSingleton<ITaskStore, InMemoryTaskStore>() 跳过；
        // AddA2AAgent 用 AddSingleton<IAgentHandler, THandler>()（非 TryAdd），故其后再覆盖注册外层适配器实例。
        builder.Services.AddSingleton<Sdk.ITaskStore, SqlSugarA2ATaskStore>();
        builder.Services.AddA2AAgent<InternalSubAgentHandlerAdapter>(
            card,
            opts => opts.AutoAppendHistory = true);

        var adapter = new InternalSubAgentHandlerAdapter(
            _outerScopeFactory,
            _outerLoggerFactory?.CreateLogger<InternalSubAgentHandlerAdapter>());
        builder.Services.AddSingleton<Sdk.IAgentHandler>(adapter);

        builder.WebHost.UseUrls(baseUrl);

        var app = builder.Build();
        app.UseMiddleware<ApiKeyAuthMiddleware>(server.RequireApiKey, server.ApiKeys.ToArray(), server.Path);
        app.MapA2A(server.Path);
        app.MapWellKnownAgentCard(card);

        try
        {
            await app.StartAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _app = app;
        _endpoint = ResolveEndpoint(app) ?? baseUrl;
        _logger?.LogInformation("A2A 服务端已启动：{Endpoint}（agent={Agent}）", _endpoint, server.AgentName);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        var app = _app;
        if (app is null)
        {
            return;
        }

        _app = null;
        _endpoint = null;

        await app.StopAsync(ct).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        _logger?.LogInformation("A2A 服务端已停止");
    }

    private void ConfigureLogging(WebApplicationBuilder builder)
    {
        // 桌面/TUI 宿主下禁止内层默认控制台输出污染界面；适配器日志由外层工厂直接构造。
        builder.Logging.ClearProviders();
    }

    private static string? ResolveEndpoint(WebApplication app)
    {
        var feature = app.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>();
        return feature?.Addresses.FirstOrDefault();
    }

    private static string BuildBaseUrl(string host, int port)
    {
        var normalized = host == "localhost" ? "127.0.0.1" : host.Trim('[', ']');
        return normalized.Contains(':')
            ? $"http://[{normalized}]:{port}"
            : $"http://{normalized}:{port}";
    }

    private static int ResolveFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool IsLoopbackHost(string host)
        => host is "127.0.0.1" or "::1" or "[::1]" or "localhost";

    private A2AAgentDescriptor BuildServerDescriptor(A2AServerOptions server, string baseUrl)
    {
        var skills = _registry.GetAll()
            .SelectMany(d => d.Skills ?? new List<A2ASkillDescriptor>())
            .ToList();

        var descriptor = new A2AAgentDescriptor
        {
            Name = server.AgentName,
            Description = server.AgentDescription,
            Version = server.AgentVersion,
            Interfaces = new List<A2AInterface>
            {
                new()
                {
                    Url = baseUrl,
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                },
            },
            Skills = skills,
            DefaultInputModes = new List<string> { "text/plain" },
            DefaultOutputModes = new List<string> { "text/plain" },
            SupportsExtendedCard = false,
            Extensions = new List<string> { "luban://a2a/ext/input-required" },
        };

        if (server.RequireApiKey)
        {
            descriptor.SecuritySchemes = new Dictionary<string, A2ASecurityScheme>
            {
                ["apiKey"] = new() { Type = "apiKey", Name = "X-Api-Key", In = "header" },
            };
            descriptor.SecurityRequirements = new List<Dictionary<string, List<string>>>
            {
                new() { ["apiKey"] = new List<string>() },
            };
        }

        return descriptor;
    }

    private Sdk.AgentCard BuildAgentCard(A2AServerOptions server, string baseUrl)
        => BuildServerDescriptor(server, baseUrl).ToSdk();
}