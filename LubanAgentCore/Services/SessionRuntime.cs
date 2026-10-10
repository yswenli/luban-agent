/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： SessionRuntime
*版本号： V1.0.0.0
*唯一标识：会话运行时
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：单个会话的运行时：拥有会话级 DI scope、回合队列与串行运行循环，
*每个回合在会话临时目录作用域内执行，保证同会话内按序处理、临时文件彼此隔离。
*
*****************************************************************************/
using System.Threading.Channels;

namespace LubanAgentCore.Services;

/// <summary>
/// 单个会话的运行时。拥有一个会话级 <see cref="IServiceScope"/>、回合队列与串行运行循环。
/// 同一会话的所有回合按入队顺序串行执行；不同会话各自独立，互不干扰。
/// </summary>
public sealed class SessionRuntime : IAsyncDisposable
{
    private readonly IServiceScope _scope;
    private readonly Channel<RuntimeTurn> _queue = Channel.CreateUnbounded<RuntimeTurn>();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private int _running;
    private int _disposed;

    /// <summary>会话标识。</summary>
    public string SessionId { get; }

    /// <summary>会话作用域内的上下文（由本会话 scope 的 <see cref="SessionContextState"/> 派生）。</summary>
    public ISessionContext SessionContext { get; }

    /// <summary>当前是否正在执行某个回合。</summary>
    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>最近活动时间（用于空闲卸载）。</summary>
    public DateTimeOffset LastActive { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 创建会话运行时。
    /// </summary>
    /// <param name="scope">会话级 DI scope。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="sessionContext">会话上下文。</param>
    public SessionRuntime(IServiceScope scope, string sessionId, ISessionContext sessionContext)
    {
        _scope = scope;
        SessionId = sessionId;
        SessionContext = sessionContext;
    }

    /// <summary>启动运行循环（幂等）。</summary>
    public void Start() => _loop ??= Task.Run(ProcessLoopAsync);

    /// <summary>入队一个回合。</summary>
    /// <param name="turn">回合。</param>
    /// <param name="cancellationToken">入队取消令牌。</param>
    public ValueTask EnqueueAsync(RuntimeTurn turn, CancellationToken cancellationToken = default)
    {
        LastActive = DateTimeOffset.UtcNow;
        return _queue.Writer.WriteAsync(turn, cancellationToken);
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            await foreach (var turn in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                Interlocked.Exchange(ref _running, 1);
                try
                {
                    using var tempScope = TempDirectory.Push(
                        new StaticTempDirectoryProvider(SessionContext.ResolveTempDirectory()));
                    await turn.RunAsync(SessionContext, turn.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 回合被取消（Esc/新会话），静默跳过，保持循环存活
                }
                catch (Exception ex)
                {
                    Logger.Error($"会话运行时回合执行失败: {SessionId}", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                    LastActive = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 运行循环被 Dispose 取消
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _queue.Writer.TryComplete();
        _cts.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch { }
        }
        _cts.Dispose();
        _scope.Dispose();
    }
}

/// <summary>
/// 一个待执行的会话回合：承载文本、执行委托与本轮取消令牌。
/// 执行委托由宿主提供（调用 Agent 流式运行并把事件推入该会话事件流）。
/// </summary>
public sealed class RuntimeTurn
{
    /// <summary>本轮输入文本。</summary>
    public required string Text { get; init; }

    /// <summary>实际执行委托；入参为本会话上下文与本轮取消令牌。</summary>
    public required Func<ISessionContext, CancellationToken, Task> RunAsync { get; init; }

    /// <summary>本轮取消令牌。</summary>
    public CancellationToken CancellationToken { get; init; }
}