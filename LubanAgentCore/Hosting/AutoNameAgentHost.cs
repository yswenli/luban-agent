/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： AutoNameAgentHost
*描述：IAgentHost 装饰器：在一轮对话真正开始前触发一次会话自动命名
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// <see cref="IAgentHost"/> 装饰器：把会话自动命名的触发点收敛到“三端共用的回合入口”。
/// </summary>
/// <remarks>
/// <para>
/// Web、TUI、桌面端最终都经 <see cref="IAgentHost.RunStreamingAsync"/> 发起一轮对话，
/// 因此在这里判定一次即可覆盖三端，不必让每个界面各写一套。
/// </para>
/// <para>
/// 远程客户端（<c>HttpAgentHost</c>）不装饰：远端模式的实际回合在宿主进程里跑，
/// 由宿主侧的装饰器触发，避免同一轮在两端各触发一次。
/// </para>
/// </remarks>
public sealed class AutoNameAgentHost(IAgentHost inner, DiContainer services) : IAgentHost
{
    /// <summary>
    /// 已进入命名流程的会话：挡住多端并发/重试造成的重复调用（同一会话命中一次即不再进入）。
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> Pending = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsInitialized => inner.IsInitialized;

    /// <inheritdoc />
    public DiContainer Services => inner.Services;

    /// <inheritdoc />
    public string? CurrentSessionId => inner.CurrentSessionId;

    /// <inheritdoc />
    public Task InitializeAsync(string sessionId, CancellationToken ct = default, AgentBuildOverrides? overrides = null)
        => inner.InitializeAsync(sessionId, ct, overrides);

    /// <inheritdoc />
    public void Reset(string? sessionId = null) => inner.Reset(sessionId);

    /// <inheritdoc />
    public IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(AgentRunRequest request, CancellationToken ct = default)
    {
        TryStartAutoName(request);
        return inner.RunStreamingAsync(request, ct);
    }

    /// <summary>
    /// 起一个后台命名任务：不阻塞、不随本轮取消而中断；失败只记日志。
    /// </summary>
    /// <remarks>
    /// 依赖在起任务前解析成实例：后台任务可能在本轮作用域释放之后才跑完，
    /// 解析出的都是进程级对象（仓储 / 用户桶门面），不受作用域释放影响。
    /// </remarks>
    private void TryStartAutoName(AgentRunRequest request)
    {
        var sessionId = request.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(request.Input)) return;

        var runner = services.GetService<SessionAutoNameRunner>();
        if (runner is null) return;

        var userId = services.GetService<LuBan.AIAgent.Abstractions.IUserContext>()?.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = LocalUserDefaults.DefaultUserId;
        }

        if (!Pending.TryAdd(sessionId, 0)) return;

        var text = request.Input;
        _ = Task.Run(async () =>
        {
            try
            {
                await runner.RunAsync(sessionId, userId!, text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn($"会话自动命名失败：sessionId={sessionId}, error={ex.Message}");
            }
            finally
            {
                Pending.TryRemove(sessionId, out _);
            }
        });
    }
}