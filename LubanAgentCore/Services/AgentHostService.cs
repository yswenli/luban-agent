/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： AgentHostService
*版本号： V1.0.0.0
*唯一标识：Agent 宿主服务
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：Agent 宿主服务：无状态路由器。按 sessionId 把对话路由到该会话的
*运行时（SessionRuntime）串行执行，输出 UI 无关的 StreamEvent 序列。
*每会话的 Agent 实例在其会话 scope 内构建并缓存，不同会话互不干扰。
*
*****************************************************************************/
using System.Threading.Channels;

namespace LubanAgentCore.Services;

/// <summary>
/// Agent 宿主服务：无状态路由器，按 sessionId 路由到对应会话运行时执行，输出 UI 无关的 StreamEvent 序列。
/// CLI 和 GUI 共用此服务。所有 per-session 运行态均在 <see cref="SessionRuntime"/> 的会话 scope 内，服务自身不持有会话状态。
/// </summary>
public class AgentHostService
{
    private readonly IServiceProvider _services;
    private readonly SessionRuntimeManager _runtimes;

    /// <summary>根级服务提供者（供宿主读取全局配置/工作区等）。</summary>
    public IServiceProvider Services => _services;

    /// <summary>
    /// 创建 Agent 宿主服务。
    /// </summary>
    /// <param name="services">当前（用户）作用域服务提供者。</param>
    /// <param name="runtimes">会话运行时管理器（root 单例）。</param>
    public AgentHostService(IServiceProvider services, SessionRuntimeManager runtimes)
    {
        _services = services;
        _runtimes = runtimes;
    }

    /// <summary>当前用户标识（缺失时回退本地默认用户）。</summary>
    private string CurrentUserId
        => _services.GetService<LuBan.AIAgent.Abstractions.IUserContext>()?.UserId is { Length: > 0 } id
            ? id
            : LubanAgentCore.Hosting.LocalUserDefaults.DefaultUserId;

    /// <summary>当前活动会话标识（来自全局会话管理器）。</summary>
    private string? CurrentSessionId
        => _services.GetService<ISessionManager>()?.CurrentSession?.SessionId;

    /// <summary>当前活动会话的运行时（若已创建）。</summary>
    private SessionRuntime? CurrentRuntime
        => CurrentSessionId is { Length: > 0 } id ? _runtimes.Get(id) : null;

    /// <summary>当前会话的 Agent 是否已就绪。</summary>
    public bool IsInitialized => CurrentRuntime?.Agent is not null;

    /// <summary>
    /// 重置当前会话的 Agent（置空缓存），下次对话时按最新配置（如切换后的模型）重建。
    /// 不影响其它会话。
    /// </summary>
    public void Reset()
    {
        if (CurrentSessionId is { Length: > 0 } id)
            Reset(id);
    }

    /// <summary>
    /// 重置指定会话的 Agent（置空缓存），下次对话时按最新配置（如切换后的模型）重建。
    /// 不影响其它会话，可用于后台会话的按 sessionId 重置。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    public void Reset(string sessionId)
    {
        var runtime = _runtimes.Get(sessionId);
        if (runtime is null) return;
        if (runtime.Agent is IDisposable oldAgent)
        {
            try { oldAgent.Dispose(); }
            catch { /* 忽略释放异常，避免影响后续重建 */ }
        }
        runtime.Agent = null;
        runtime.Workspace = null;
    }

    /// <summary>
    /// 初始化当前会话的 Agent（在首次对话前调用）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var sessionId = CurrentSessionId
            ?? throw new InvalidOperationException("未设置当前会话");

        await InitializeAsync(sessionId, ct);
    }

    /// <summary>
    /// 初始化指定会话的 Agent（在首次对话前调用）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task InitializeAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureRuntimeAsync(sessionId, ct);
    }

    /// <summary>
    /// 取（或创建）指定会话的运行时，并确保其 Agent 已按该会话自身绑定的工作区构建。
    /// </summary>
    /// <remarks>
    /// 工作区来源为会话自身绑定的 <c>DbSession.WorkspaceId</c>（见 <see cref="ResolveSessionWorkspaceAsync"/>），
    /// 而非进程级“当前工作区”，避免跨工作区串扰。
    /// </remarks>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task<SessionRuntime> EnsureRuntimeAsync(string sessionId, CancellationToken ct)
    {
        var runtime = _runtimes.Get(sessionId);
        if (runtime?.Agent is not null)
            return runtime;

        var workspace = await ResolveSessionWorkspaceAsync(sessionId, ct);
        runtime = _runtimes.GetOrCreate(sessionId, CurrentUserId, workspace.WorkspaceId, workspace.RootPath);
        await EnsureAgentAsync(runtime, workspace, ct);
        return runtime;
    }

    /// <summary>
    /// 解析指定会话自身绑定的工作区；历史会话未绑定工作区时回退到当前工作区。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task<WorkspaceInfo> ResolveSessionWorkspaceAsync(string sessionId, CancellationToken ct)
    {
        var workspaceManager = _services.GetRequiredService<IWorkspaceManager>();

        var sessionRepo = _services.GetRequiredService<SessionRepository>();
        var dbSession = await sessionRepo.GetBySessionIdAsync(sessionId);
        var workspaceId = dbSession?.WorkspaceId;

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            var ws = await workspaceManager.GetWorkspaceAsync(workspaceId);
            if (ws is not null)
                return ws;
        }

        // 历史会话未绑定工作区（WorkspaceId 为空）或工作区已被删除：回退到当前工作区
        return workspaceManager.CurrentWorkspace
            ?? throw new InvalidOperationException("未设置当前工作区");
    }

    /// <summary>
    /// 流式对话，输出 StreamEvent 序列（当前会话）。
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <param name="confirmHandler">工具确认处理器（返回 ConfirmResult）</param>
    /// <param name="permissionMode">权限模式</param>
    /// <param name="onPlannedAction">Plan 模式计划项回调；Plan 下工具不执行，仅经此回调上报</param>
    /// <param name="onUserDenied">用户明确拒绝工具调用时的回调；宿主应在此取消本轮令牌以终止对话</param>
    /// <param name="ct">取消令牌</param>
    public IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string input,
        Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult> confirmHandler,
        ToolPermissionMode permissionMode,
        Action<string, IReadOnlyDictionary<string, object?>>? onPlannedAction = null,
        Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied = null,
        CancellationToken ct = default)
        => RunStreamingAsync(input, Array.Empty<string>(), confirmHandler, permissionMode, onPlannedAction, onUserDenied, null, ct);

    /// <summary>
    /// 运行一轮对话（带附件路径）。
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <param name="attachmentPaths">附件文件路径列表</param>
    /// <param name="confirmHandler">工具确认处理器（返回 ConfirmResult）</param>
    /// <param name="permissionMode">权限模式</param>
    /// <param name="onPlannedAction">Plan 模式计划项回调</param>
    /// <param name="onUserDenied">用户明确拒绝工具调用时的回调；宿主应在此取消本轮令牌以终止对话</param>
    /// <param name="sessionId">目标会话标识；null 表示当前活动会话</param>
    /// <param name="ct">取消令牌</param>
    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string input,
        IReadOnlyList<string> attachmentPaths,
        Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult> confirmHandler,
        ToolPermissionMode permissionMode,
        Action<string, IReadOnlyDictionary<string, object?>>? onPlannedAction = null,
        Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied = null,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sid = sessionId ?? CurrentSessionId
            ?? throw new InvalidOperationException("未设置当前会话");

        var attachmentService = _services.GetRequiredService<AttachmentService>();
        var configManager = _services.GetRequiredService<ConfigManager>();
        var modelId = configManager.SelectedModel ?? "";

        IReadOnlyList<ProcessedAttachment> processed = Array.Empty<ProcessedAttachment>();
        if (attachmentPaths.Count > 0)
        {
            string? failure = null;
            try
            {
                processed = await attachmentService.ProcessAsync(attachmentPaths, generateThumbnails: false, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failure = $"附件处理失败: {ex.Message}";
            }

            if (failure != null)
            {
                yield return new ErrorEvent(failure);
                yield break;
            }

            if (!attachmentService.CanAttach(modelId, processed))
            {
                yield return new ErrorEvent(attachmentService.GetUnsupportedMessage(modelId, processed));
                yield break;
            }

            foreach (var a in processed)
                yield return new AttachmentAddedEvent(a.Info.FileName, a.Info.MediaType, a.Info.FileSize);
        }

        var runtime = await EnsureRuntimeAsync(sid, ct);

        var hasImage = processed.Any(a => a.Info.Kind == AttachmentKind.Image);
        var probe = VisionCapabilityProbe.Create(modelId, hasImage, _services.GetModelCapabilityCache());

        if (!probe.IsProbing)
        {
            await foreach (var evt in RunInRuntimeAsync(runtime, input, processed, confirmHandler, permissionMode, onPlannedAction, onUserDenied, ct))
                yield return evt;
            yield break;
        }

        // 首次带图调用：由 LLM 实际结果回填模型视觉能力（成功记 true；命中「图片不支持」错误记 false，仅记录不重发）
        Exception? streamError = null;
        var completed = false;

        var enumerator = RunInRuntimeAsync(runtime, input, processed, confirmHandler, permissionMode, onPlannedAction, onUserDenied, ct)
            .GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                StreamEvent current;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        completed = true;
                        break;
                    }
                    current = enumerator.Current;
                }
                catch (Exception ex)
                {
                    streamError = ex;
                    throw;
                }

                if (current is ErrorEvent errorEvent)
                    probe.Observe(errorEvent.Message);

                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            probe.Complete(streamError, completed);
        }
    }

    /// <summary>
    /// 把一轮对话入队到指定会话运行时串行执行，并以 Channel 把回合内产生的事件回传为异步流。
    /// </summary>
    private async IAsyncEnumerable<StreamEvent> RunInRuntimeAsync(
        SessionRuntime runtime,
        string input,
        IReadOnlyList<ProcessedAttachment> processed,
        Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult> confirmHandler,
        ToolPermissionMode permissionMode,
        Action<string, IReadOnlyDictionary<string, object?>>? onPlannedAction,
        Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<StreamEvent>();

        await runtime.EnqueueAsync(new RuntimeTurn
        {
            Text = input,
            CancellationToken = ct,
            RunAsync = async (sessionContext, token) =>
            {
                try
                {
                    await foreach (var evt in RunCoreAsync(runtime, sessionContext, input, processed, confirmHandler, permissionMode, onPlannedAction, onUserDenied, token))
                        channel.Writer.TryWrite(evt);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }
        }, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            yield return evt;
    }

    /// <summary>
    /// 确保指定会话运行时内已按当前工作区构建 Agent。
    /// </summary>
    private static async Task EnsureAgentAsync(SessionRuntime runtime, WorkspaceInfo workspace, CancellationToken ct)
    {
        if (runtime.Agent is not null && runtime.Workspace?.WorkspaceId == workspace.WorkspaceId)
            return;

        var sp = runtime.Services;
        var agentFactory = sp.GetRequiredService<ILuBanAgentFactory>();
        var ruleEngine = sp.GetRequiredService<RuleEngine>();
        var pluginRegistry = sp.GetRequiredService<ToolPluginRegistry>();
        var skillRegistry = sp.GetRequiredService<SkillRegistry>();
        var mcpRegistry = sp.GetRequiredService<MCPRegistry>();
        var configManager = sp.GetRequiredService<ConfigManager>();

        var modelName = configManager.SelectedModel
            ?? throw new InvalidOperationException("未选择模型");

        // 工作区级组件（规则/技能/MCP）由 AgentProfile.CreateAgentAsync 在会话 scope 内统一装载。
        // 注册表已改为 scoped（每会话一份），无需在进入会话时重复装载到进程级注册表。

        // 选择 Profile
        AgentProfile profile = workspace.Type == WorkspaceTypes.LlmWiki
            ? new LlmWikiAgentProfile(workspace)
            : new NormalAgentProfile();

        var newAgent = await profile.CreateAgentAsync(
            agentFactory, modelName, workspace,
            ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);

        // 释放旧 Agent 实例，避免重建时连接/句柄等资源泄漏
        if (runtime.Agent is IDisposable oldAgent)
        {
            try { oldAgent.Dispose(); }
            catch { /* 忽略释放异常，避免影响新 Agent 使用 */ }
        }
        runtime.Agent = newAgent;
        runtime.Workspace = workspace;
    }

    private async IAsyncEnumerable<StreamEvent> RunCoreAsync(
        SessionRuntime runtime,
        ISessionContext sessionContext,
        string input,
        IReadOnlyList<ProcessedAttachment> processed,
        Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult> confirmHandler,
        ToolPermissionMode permissionMode,
        Action<string, IReadOnlyDictionary<string, object?>>? onPlannedAction,
        Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sp = runtime.Services;

        // Agent 已由 EnsureRuntimeAsync 按本会话自身绑定的工作区构建；
        // 运行时绑定即权威来源，不再按进程级“当前工作区”重建（避免跨工作区串扰）。
        var agent = runtime.Agent
            ?? throw new InvalidOperationException("Agent 初始化失败，请先调用 InitializeAsync");

        var context = sp.GetRequiredService<ToolConfirmationContext>();

        // 统一装配（与 CLI 共用 ConfigureForTurn）：模式策略由框架分发，
        // 回调仅在需要人工确认时被调用；授权记忆（本轮/本会话）由框架依据返回值处理。
        // 路径检查器基于本会话上下文（EffectiveRoot/AllowedRoots，含 worktree），实现会话级隔离。
        context.ConfigureForTurn(
            permissionMode,
            ct,
            confirmHandler,
            sessionContext: sessionContext,
            onPlannedAction: onPlannedAction,
            onUserDenied: onUserDenied);

        try
        {
            var updates = processed.Count == 0
                ? agent.RunStreamingAsync(input, ct)
                : agent.RunStreamingAsync(input, processed, ct);
            await foreach (var update in updates)
            {
                if (update.Contents is null) continue;

                // 框架已在每个更新上标注 LLM 轮次号（工具结果边界 +1）：UI 据此按“同一轮回复”归并思考块
                var llmRound = update.AdditionalProperties is { } roundProps
                    && roundProps.TryGetValue(LuBanAgent.LlmRoundKey, out var roundValue)
                    && roundValue is int r
                        ? r
                        : 0;

                foreach (var content in update.Contents)
                {
                    // 编排进度：规划与节点执行均为长耗时非流式过程，
                    // 转为进度事件供 UI 实时反馈（否则此阶段内容区长时间空白）
                    if (content is OrchestrationProgressContent progress)
                    {
                        yield return OrchestrationProgressEvent.Create(
                            progress.EventType, progress.NodeId, progress.Message,
                            progress.ElapsedMs, progress.NodeResult, progress.Activity)
                            with { LlmRound = llmRound };
                        continue;
                    }

                    // 思考过程
                    if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    {
                        yield return new ThinkingDeltaEvent(reasoning.Text) { LlmRound = llmRound };
                        continue;
                    }

                    // 工具调用
                    if (content is FunctionCallContent functionCall)
                    {
                        var args = functionCall.Arguments != null
                            ? new Dictionary<string, object?>(functionCall.Arguments)
                            : new Dictionary<string, object?>();
                        yield return new ToolCallStartedEvent(
                            functionCall.Name,
                            functionCall.CallId,
                            args) { LlmRound = llmRound };
                        continue;
                    }

                    // 工具结果
                    if (content is FunctionResultContent functionResult)
                    {
                        if (functionResult.Exception is not null)
                        {
                            yield return new ToolCallFailedEvent(
                                functionResult.CallId,
                                functionResult.Exception.Message) { LlmRound = llmRound };
                        }
                        else if (ToolResult.IsUserCancelled(functionResult.Result))
                        {
                            // 拒绝与取消在结果层不可区分：用中性文案，宿主聊天流已分别提示
                            yield return new ToolCallFailedEvent(functionResult.CallId, "工具调用被拒绝或已取消") { LlmRound = llmRound };
                        }
                        else
                        {
                            yield return new ToolCallCompletedEvent(
                                functionResult.CallId,
                                SummarizeResult(functionResult.Result)) { LlmRound = llmRound };
                        }
                        continue;
                    }

                    // 正文回复
                    if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    {
                        yield return new TextDeltaEvent(text.Text) { LlmRound = llmRound };
                        continue;
                    }

                    // 错误
                    if (content is ErrorContent error)
                    {
                        yield return new ErrorEvent(error.Message) { LlmRound = llmRound };
                        continue;
                    }

                    // UsageContent 静默跳过
                }
            }
        }
        finally
        {
            context.Reset();
        }
    }

    /// <summary>
    /// 提取工具结果摘要：截断至 200 字符，超长末尾加 "…"。
    /// 处理 string / JsonElement / ToolResult / 其它对象（ToString 兜底）。
    /// </summary>
    /// <param name="result">工具返回值。</param>
    /// <returns>摘要文本；无内容返回 null。</returns>
    private static string? SummarizeResult(object? result)
    {
        if (result is null) return null;

        string? text;
        try
        {
            // 优先处理 JsonElement：ToString 输出紧凑 JSON
            if (result is System.Text.Json.JsonElement je)
            {
                text = je.ValueKind == System.Text.Json.JsonValueKind.String
                    ? je.GetString()
                    : je.ToString();
            }
            // 处理 ToolResult：优先取 Message，其次用 ToString 兜底
            else if (result is LuBan.AIAgent.Abstractions.ToolResult tr)
            {
                text = tr.Message ?? tr.ToString();
            }
            else
            {
                text = result as string ?? result.ToString();
            }
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text)) return null;

        const int MaxLength = 200;
        return text.Length <= MaxLength
            ? text
            : text[..MaxLength] + "…";
    }
}