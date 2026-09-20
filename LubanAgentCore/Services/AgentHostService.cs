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
*描述：Agent 宿主服务：封装 Agent 创建与流式对话，输出 UI 无关的 StreamEvent 序列。
*
*****************************************************************************/
namespace LubanAgentCore.Services;

/// <summary>
/// Agent 宿主服务：封装 Agent 创建与流式对话，输出 UI 无关的 StreamEvent 序列。
/// CLI 和 GUI 共用此服务。
/// </summary>
public class AgentHostService
{
    private readonly IServiceProvider _services;
    private LuBanAgent? _agent;
    private WorkspaceInfo? _workspace;

    public IServiceProvider Services => _services;

    public AgentHostService(IServiceProvider services)
    {
        _services = services;
    }

    public bool IsInitialized => _agent != null;

    /// <summary>
    /// 重置 Agent（置空当前实例），下次对话时按最新配置重建。
    /// 用于模型切换等配置变更后让新配置生效。
    /// </summary>
    public void Reset()
    {
        if (_agent is IDisposable oldAgent)
        {
            try { oldAgent.Dispose(); }
            catch { /* 忽略释放异常，避免影响后续重建 */ }
        }
        _agent = null;
    }

    /// <summary>
    /// 初始化 Agent（在首次对话前调用）。
    /// </summary>
    public async Task InitializeAsync()
    {
        var workspaceManager = _services.GetRequiredService<IWorkspaceManager>();
        _workspace = workspaceManager.CurrentWorkspace
            ?? throw new InvalidOperationException("未设置当前工作区");

        var agentFactory = _services.GetRequiredService<ILuBanAgentFactory>();
        var ruleEngine = _services.GetRequiredService<RuleEngine>();
        var pluginRegistry = _services.GetRequiredService<ToolPluginRegistry>();
        var skillRegistry = _services.GetRequiredService<SkillRegistry>();
        var mcpRegistry = _services.GetRequiredService<MCPRegistry>();
        var configManager = _services.GetRequiredService<ConfigManager>();

        var modelName = configManager.SelectedModel
            ?? throw new InvalidOperationException("未选择模型");

        // 加载工作区级组件
        skillRegistry.LoadFromWorkspace(_workspace.RootPath);
        mcpRegistry.LoadFromWorkspace(_workspace.RootPath);
        ruleEngine.LoadFromWorkspace(_workspace.RootPath);

        // 选择 Profile
        AgentProfile profile = _workspace.Type == "Rag"
            ? new RagAgentProfile(_workspace)
            : new NormalAgentProfile();

        var newAgent = await profile.CreateAgentAsync(
            agentFactory, modelName, _workspace,
            ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);
        // 释放旧 Agent 实例，避免重建时连接/句柄等资源泄漏
        if (_agent is IDisposable oldAgent)
        {
            try { oldAgent.Dispose(); }
            catch { /* 忽略释放异常，避免影响新 Agent 使用 */ }
        }
        _agent = newAgent;
    }

    /// <summary>
    /// 流式对话，输出 StreamEvent 序列。
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
        => RunStreamingAsync(input, Array.Empty<string>(), confirmHandler, permissionMode, onPlannedAction, onUserDenied, ct);

    /// <summary>
    /// 运行一轮对话（带附件路径）。
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <param name="attachmentPaths">附件文件路径列表</param>
    /// <param name="confirmHandler">工具确认处理器（返回 ConfirmResult）</param>
    /// <param name="permissionMode">权限模式</param>
    /// <param name="onPlannedAction">Plan 模式计划项回调</param>
    /// <param name="onUserDenied">用户明确拒绝工具调用时的回调；宿主应在此取消本轮令牌以终止对话</param>
    /// <param name="ct">取消令牌</param>
    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string input,
        IReadOnlyList<string> attachmentPaths,
        Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult> confirmHandler,
        ToolPermissionMode permissionMode,
        Action<string, IReadOnlyDictionary<string, object?>>? onPlannedAction = null,
        Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
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

        var hasImage = processed.Any(a => a.Info.Kind == AttachmentKind.Image);
        var probe = VisionCapabilityProbe.Create(modelId, hasImage);

        if (!probe.IsProbing)
        {
            await foreach (var evt in RunCoreAsync(input, processed, confirmHandler, permissionMode, onPlannedAction, onUserDenied, ct))
                yield return evt;
            yield break;
        }

        // 首次带图调用：由 LLM 实际结果回填模型视觉能力（成功记 true；命中「图片不支持」错误记 false，仅记录不重发）
        Exception? streamError = null;
        var completed = false;

        var enumerator = RunCoreAsync(input, processed, confirmHandler, permissionMode, onPlannedAction, onUserDenied, ct)
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

    private async IAsyncEnumerable<StreamEvent> RunCoreAsync(
        string input,
        IReadOnlyList<ProcessedAttachment> processed,
        Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult> confirmHandler,
        ToolPermissionMode permissionMode,
        Action<string, IReadOnlyDictionary<string, object?>>? onPlannedAction,
        Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 确保 Agent 与当前工作区匹配：切换工作区后必须重新初始化，
        // 否则会复用上一工作区的 Profile（如普通工作区），
        // 导致 RAG 知识库不加载检索工具组/RAG 系统提示词，表现与常规工作区无异。
        var currentWorkspace = _services.GetRequiredService<IWorkspaceManager>().CurrentWorkspace;
        if (_agent == null || (currentWorkspace != null && _workspace?.WorkspaceId != currentWorkspace.WorkspaceId))
        {
            await InitializeAsync();
        }

        if (_agent == null)
            throw new InvalidOperationException("Agent 初始化失败，请先调用 InitializeAsync");

        var context = _services.GetRequiredService<ToolConfirmationContext>();

        // 统一装配（与 CLI 共用 ConfigureForTurn）：模式策略由框架分发，
        // 回调仅在需要人工确认时被调用；ConfirmResult → bool 在此适配
        context.ConfigureForTurn(
            permissionMode,
            ct,
            (toolName, args) =>
            {
                var result = confirmHandler(toolName, args);
                if (result == ConfirmResult.AllowAll)
                    context.AllowThisTurn(toolName);
                return result != ConfirmResult.Deny;
            },
            onPlannedAction,
            onUserDenied);

        try
        {
            var updates = processed.Count == 0
                ? _agent!.RunStreamingAsync(input, ct)
                : _agent!.RunStreamingAsync(input, processed, ct);
            await foreach (var update in updates)
            {
                if (update.Contents is null) continue;

                foreach (var content in update.Contents)
                {
                    // 编排进度：规划与节点执行均为长耗时非流式过程，
                    // 转为进度事件供 UI 实时反馈（否则此阶段内容区长时间空白）
                    if (content is OrchestrationProgressContent progress)
                    {
                        yield return new OrchestrationProgressEvent(
                            progress.EventType,
                            progress.NodeId,
                            progress.Message,
                            progress.NodeResult?.Elapsed.TotalSeconds,
                            progress.NodeResult?.Error,
                            progress.Activity);
                        continue;
                    }

                    // 思考过程
                    if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    {
                        yield return new ThinkingDeltaEvent(reasoning.Text);
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
                            args);
                        continue;
                    }

                    // 工具结果
                    if (content is FunctionResultContent functionResult)
                    {
                        if (functionResult.Exception is not null)
                        {
                            yield return new ToolCallFailedEvent(
                                functionResult.CallId,
                                functionResult.Exception.Message);
                        }
                        else if (ToolResult.IsUserCancelled(functionResult.Result))
                        {
                            // 拒绝与取消在结果层不可区分：用中性文案，宿主聊天流已分别提示
                            yield return new ToolCallFailedEvent(functionResult.CallId, "工具调用被拒绝或已取消");
                        }
                        else
                        {
                            yield return new ToolCallCompletedEvent(
                                functionResult.CallId,
                                SummarizeResult(functionResult.Result));
                        }
                        continue;
                    }

                    // 正文回复
                    if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    {
                        yield return new TextDeltaEvent(text.Text);
                        continue;
                    }

                    // 错误
                    if (content is ErrorContent error)
                    {
                        yield return new ErrorEvent(error.Message);
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
