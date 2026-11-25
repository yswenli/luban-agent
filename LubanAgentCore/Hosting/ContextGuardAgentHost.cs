/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： ContextGuardAgentHost
*描述：IAgentHost 装饰器：起跑前按 token 预算压缩上下文，超长失败且本轮无产出时压缩后兜底重跑
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// <see cref="IAgentHost"/> 装饰器：把「上下文超长」的兜底收敛到三端共用的回合入口。
/// </summary>
/// <remarks>
/// <para>
/// 两处介入：
/// 1. 起跑前按会话上下文预算预检，估算超出预算就先压缩，避免把必然失败的请求发出去；
/// 2. 首调仍被判超长（且本轮零事件产出）时，压缩后重跑一次，用户不必手动清空对话。
/// </para>
/// <para>
/// 远程客户端（<c>HttpAgentHost</c>）不装饰：远端模式的实际回合在宿主进程里跑，
/// 由宿主侧装饰器处理，避免同一轮两端各压一次。
/// </para>
/// </remarks>
public sealed class ContextGuardAgentHost(IAgentHost inner, DiContainer services) : IAgentHost
{
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
    public async IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sessionId = request.SessionId;
        ContextCompactor? compactor = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            try
            {
                compactor = services.GetService<ContextCompactor>();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 解析压缩器会顺带创建聊天客户端（例如「请先选择模型」）。
                // 这里不能把可恢复的起跑错误变成抛异常：退回内层宿主，由它产出原有错误事件。
                Logger.Warn($"上下文压缩器不可用，本轮跳过压缩守卫：error={ex.Message}");
            }
        }

        if (compactor is null)
        {
            await foreach (var passthrough in inner.RunStreamingAsync(request, ct).ConfigureAwait(false))
            {
                yield return passthrough;
            }

            yield break;
        }

        var budget = await ResolveBudgetAsync(sessionId!, ct).ConfigureAwait(false);
        var tokens = await EstimateAsync(compactor, sessionId!, request.Input, ct).ConfigureAwait(false);

        // ── 起跑前预检：估算已超预算，先压缩再发请求 ──
        if (budget is > 0 && tokens > budget.Value)
        {
            yield return ContextCompactedEvent.Started(ContextCompactTriggers.Preflight, budget);

            var preflight = await TryCompactAsync(
                compactor, sessionId!, budget, request.Input, ContextCompactTriggers.Preflight, ct).ConfigureAwait(false);
            if (preflight is not null)
            {
                yield return ContextCompactedEvent.Done(ContextCompactTriggers.Preflight, preflight);
            }
            else
            {
                // 压缩失败仍照常发请求：让模型自己报超长，走下面的兜底分支，而不是把历史悄悄丢掉
                yield return ContextCompactedEvent.Failed(ContextCompactTriggers.Preflight, budget);
            }
        }

        var retried = false;
        while (true)
        {
            var state = new AttemptState();
            await foreach (var evt in RunOnceAsync(request, state, ct).ConfigureAwait(false))
            {
                yield return evt;
            }

            if (!state.Overflow)
            {
                yield break;
            }

            // 已重跑过一次仍超长：压缩救不回来，把错误交回调用方
            if (retried)
            {
                yield return new ErrorEvent(state.Message ?? "上下文超出模型长度上限");
                yield break;
            }

            retried = true;
            yield return ContextCompactedEvent.Started(ContextCompactTriggers.Overflow, budget);

            var result = await TryCompactAsync(
                compactor, sessionId!, budget, request.Input, ContextCompactTriggers.Overflow, ct).ConfigureAwait(false);
            if (result is null)
            {
                // 先说明压缩没成功，再回传原始错误：否则用户只看到「正在压缩…」后直接报错
                yield return ContextCompactedEvent.Failed(ContextCompactTriggers.Overflow, budget);
                yield return new ErrorEvent(state.Message ?? "上下文超出模型长度上限");
                yield break;
            }

            yield return ContextCompactedEvent.Done(ContextCompactTriggers.Overflow, result);
        }
    }

    /// <summary>
    /// 跑一次底层回合：事件按序透传，同时记录「本轮是否已有产出」与「是否可重试的超长失败」。
    /// </summary>
    /// <remarks>
    /// 超长失败可能以两种形态出现：流内 <see cref="ErrorEvent"/>，或直接从流里抛出的
    /// <see cref="AgentApiException"/>。只有本轮尚无任何正文/思考/工具事件时才允许吞掉错误重跑，
    /// 否则会把已渲染的半成品再输出一遍。
    /// </remarks>
    private async IAsyncEnumerable<AgentStreamEvent> RunOnceAsync(
        AgentRunRequest request,
        AttemptState state,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var enumerator = inner.RunStreamingAsync(request, ct).GetAsyncEnumerator(ct);
        while (true)
        {
            AgentStreamEvent? current = null;
            var finished = false;
            try
            {
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    current = enumerator.Current;
                }
                else
                {
                    finished = true;
                }
            }
            // 已产出正文/思考/工具事件时不再吞异常重跑：与流内 ErrorEvent 同一判据，
            // 否则重跑会把半成品与完整正文各输出一遍；此时异常照常抛给调用方。
            catch (Exception ex) when (!state.Produced && FindContextOverflow(ex) is { } api)
            {
                state.Overflow = true;
                state.Message = api.Error.FriendlyMessage;
                finished = true;
            }

            if (finished)
            {
                yield break;
            }

            if (current is ErrorEvent error && !state.Produced && IsOverflowMessage(error.Message))
            {
                state.Overflow = true;
                state.Message = error.Message;
                yield break;
            }

            if (current is TextDeltaEvent or ThinkingDeltaEvent
                or ToolCallStartedEvent or ToolCallCompletedEvent or ToolCallFailedEvent)
            {
                state.Produced = true;
            }

            yield return current!;
        }
    }

    /// <summary>读取会话当前生效的上下文预算；解析失败不影响主流程。</summary>
    private async Task<int?> ResolveBudgetAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var settings = services.GetService<ISessionGenerationSettings>();
            return settings is null
                ? null
                : (await settings.ResolveContextBudgetAsync(sessionId, ct).ConfigureAwait(false))?.Threshold;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取上下文预算失败，跳过预检压缩：sessionId={sessionId}, error={ex.Message}");
            return null;
        }
    }

    /// <summary>估算当前上下文 token；估算失败按 0 处理（不预检）。</summary>
    private static async Task<int> EstimateAsync(
        ContextCompactor compactor, string sessionId, string? input, CancellationToken ct)
    {
        try
        {
            return await compactor.EstimateTokensAsync(sessionId, input, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn($"上下文 token 估算失败，跳过预检压缩：sessionId={sessionId}, error={ex.Message}");
            return 0;
        }
    }

    /// <summary>执行压缩；失败只记日志，返回 <c>null</c> 由调用方决定是否继续原流程。</summary>
    private static async Task<ContextCompactionResult?> TryCompactAsync(
        ContextCompactor compactor,
        string sessionId,
        int? budget,
        string? input,
        string trigger,
        CancellationToken ct)
    {
        try
        {
            return await compactor.CompactAsync(sessionId, budget, input, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn($"上下文压缩失败（{trigger}）：sessionId={sessionId}, error={ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 沿内层异常链查找「上下文超长」分类结果。
    /// </summary>
    /// <remarks>
    /// 框架把 LLM 失败包成 <see cref="AgentApiException"/>，但经过回合运行时/通道/聚合包装后
    /// 外层可能已不是它，因此按内层链向下找，而不是只认最外层类型。
    /// </remarks>
    private static AgentApiException? FindContextOverflow(Exception? ex, int depth = 0)
    {
        if (ex is null || depth > 3)
        {
            return null;
        }

        if (ex is AgentApiException api)
        {
            return api.Error.Category == AgentApiErrorCategory.ContextLengthExceeded ? api : null;
        }

        return FindContextOverflow(ex.InnerException, depth + 1);
    }

    /// <summary>判断错误文案是否为上下文超长（框架给的是固定中文文案，另兼容英文原文）。</summary>
    private static bool IsOverflowMessage(string? message)
        => !string.IsNullOrEmpty(message)
            && (message.Contains("上下文超出模型长度上限", StringComparison.Ordinal)
                || message.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase)
                || message.Contains("maximum context length", StringComparison.OrdinalIgnoreCase)
                || message.Contains("too many tokens", StringComparison.OrdinalIgnoreCase));

    /// <summary>单次底层回合的产出状态。</summary>
    private sealed class AttemptState
    {
        /// <summary>本轮是否已产生正文/思考/工具事件（有产出就不再重跑，避免重复输出）。</summary>
        public bool Produced { get; set; }

        /// <summary>本轮是否以「可重试的上下文超长」结束。</summary>
        public bool Overflow { get; set; }

        /// <summary>超长错误文案（供二次失败时回传）。</summary>
        public string? Message { get; set; }
    }
}