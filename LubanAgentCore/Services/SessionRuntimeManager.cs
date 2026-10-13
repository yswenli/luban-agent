/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： SessionRuntimeManager
*版本号： V1.0.0.0
*唯一标识：会话运行时管理器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：按 sessionId 维护会话级 DI scope 与运行循环；空闲超时自动卸载。
*
*****************************************************************************/
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace LubanAgentCore.Services;

/// <summary>
/// 会话运行时管理器：按 sessionId 维护会话级 DI scope 与运行循环。
/// 同一会话所有回合串行执行；不同会话拥有各自独立的 scope 与上下文，互不干扰。
/// 空闲超过 <see cref="IdleTimeout"/> 的会话会被自动卸载。
/// </summary>
public sealed class SessionRuntimeManager : IAsyncDisposable
{
    /// <summary>空闲卸载阈值配置键（分钟）。</summary>
    private const string IdleUnloadMinutesKey = "LuBanAgent:Session:IdleUnloadMinutes";

    /// <summary>空闲卸载默认阈值（分钟）。</summary>
    private const int DefaultIdleUnloadMinutes = 30;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly TimeSpan IdleTimeout;
    private readonly IServiceProvider _root;
    private readonly ConcurrentDictionary<string, SessionRuntime> _runtimes = new();
    private readonly Timer _sweeper;
    private int _disposed;

    /// <summary>
    /// 创建会话运行时管理器。
    /// </summary>
    /// <param name="root">根级 DI 容器（用于派生会话级 scope）。</param>
    /// <param name="configuration">配置（提供空闲卸载阈值，默认 30 分钟）。</param>
    public SessionRuntimeManager(IServiceProvider root, IConfiguration configuration)
    {
        _root = root;
        var minutes = configuration.GetValue<int?>(IdleUnloadMinutesKey) ?? DefaultIdleUnloadMinutes;
        IdleTimeout = TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultIdleUnloadMinutes);
        _sweeper = new Timer(_ => SweepIdle(), null, SweepInterval, SweepInterval);
    }

    /// <summary>
    /// 取（或创建）指定会话的运行时。首次调用时创建独立 scope 并填充会话上下文。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="workspaceId">工作区标识。</param>
    /// <param name="workspaceRoot">工作区根路径。</param>
    public SessionRuntime GetOrCreate(string sessionId, string workspaceId, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("会话标识不能为空", nameof(sessionId));

        return _runtimes.GetOrAdd(sessionId, id => CreateRuntime(id, workspaceId, workspaceRoot));
    }

    /// <summary>取已存在的会话运行时；不存在返回 null。</summary>
    /// <param name="sessionId">会话标识。</param>
    public SessionRuntime? Get(string sessionId) => _runtimes.GetValueOrDefault(sessionId);

    private SessionRuntime CreateRuntime(string sessionId, string workspaceId, string workspaceRoot)
    {
        var scope = _root.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<SessionContextState>();
        state.SessionId = sessionId;
        state.WorkspaceId = workspaceId;
        state.WorkspaceRoot = workspaceRoot;
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            state.AllowedRoots.Add(workspaceRoot);

        var ctx = scope.ServiceProvider.GetRequiredService<ISessionContext>();
        var runtime = new SessionRuntime(scope, sessionId, ctx);
        runtime.Start();
        return runtime;
    }

    private void SweepIdle()
    {
        if (Volatile.Read(ref _disposed) == 1) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _runtimes)
        {
            var runtime = kv.Value;
            if (runtime.IsRunning) continue;
            if (now - runtime.LastActive < IdleTimeout) continue;

            if (_runtimes.TryRemove(kv.Key, out var removed))
                _ = DisposeRuntimeAsync(removed);
        }
    }

    private static async Task DisposeRuntimeAsync(SessionRuntime runtime)
    {
        try { await runtime.DisposeAsync(); }
        catch (Exception ex) { Logger.Error($"卸载空闲会话运行时失败: {runtime.SessionId}", ex); }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _sweeper.Dispose();

        foreach (var kv in _runtimes)
        {
            if (_runtimes.TryRemove(kv.Key, out var runtime))
                await runtime.DisposeAsync();
        }
    }
}