/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.ViewModels
*文件名： ConversationViewModel
*版本号： V1.0.0.0
*唯一标识：会话 ViewModel
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：管理 Agent 生命周期、流式 token 追加与 Block 文档更新；通过 IUiDispatcher
*编组跨线程操作到 UI 线程
*
*****************************************************************************/
using System.Text;

namespace LubanAgentCli.App.ViewModels;

/// <summary>
/// 会话 ViewModel。负责创建 Agent、运行流式对话循环、将 AI 更新编排为 Block 追加。
/// 所有 UI 更新通过 <see cref="IUiDispatcher.Invoke"/> 编组到 UI 线程，
/// 本类不直接持有 View 引用。
/// </summary>
internal sealed class ConversationViewModel
{
    private readonly IServiceProvider _services;
    private readonly IUiDispatcher _dispatcher;
    private readonly ConversationDocument _doc;
    private readonly ConfigManager _configManager;

    private LuBanAgent? _agent;
    private ILuBanAgentFactory? _agentFactory;
    private AgentProfile? _profile;
    private RuleEngine? _ruleEngine;
    private ToolPluginRegistry? _pluginRegistry;
    private SkillRegistry? _skillRegistry;
    private MCPRegistry? _mcpRegistry;
    private WorkspaceInfo? _workspace;
    private string? _modelName;

    private CancellationTokenSource? _currentCts;

    // 流式 token 合批缓冲：agent 线程追加、节流冲刷时取出，替代逐 token Invoke 洪峰
    private readonly object _streamLock = new();
    private readonly StringBuilder _pendingThinking = new();
    private readonly StringBuilder _pendingAnswer = new();
    private FlushThrottle? _streamThrottle;

    // 当前会话的流式状态（每次 RunStreamingAsync 开始重置；
    // 必须是字段而非闭包局部变量——节流器跨会话复用，闭包会把上一会话的状态泄漏到下一会话）
    private ThinkingBlock? _thinkingBlock;
    private bool _thinkingCompleted;

    /// <summary>当前权限模式。</summary>
    public ToolPermissionMode PermissionMode { get; private set; } = ToolPermissionMode.Default;

    /// <summary>权限模式变更事件（订阅者更新页脚等 UI）。</summary>
    public event Action<ToolPermissionMode>? PermissionModeChanged;

    /// <summary>
    /// 循环切换到下一权限模式（Default → Plan → AcceptEdits → BypassPermissions → Default）。
    /// </summary>
    /// <returns>新的权限模式。</returns>
    public ToolPermissionMode CyclePermissionMode()
    {
        PermissionMode = PermissionMode switch
        {
            ToolPermissionMode.Default => ToolPermissionMode.Plan,
            ToolPermissionMode.Plan => ToolPermissionMode.AcceptEdits,
            ToolPermissionMode.AcceptEdits => ToolPermissionMode.BypassPermissions,
            ToolPermissionMode.BypassPermissions => ToolPermissionMode.Default,
            _ => ToolPermissionMode.Default
        };

        PermissionModeChanged?.Invoke(PermissionMode);
        return PermissionMode;
    }

    /// <summary>
    /// 设置权限模式并通知。
    /// </summary>
    /// <param name="mode">目标模式。</param>
    public void SetPermissionMode(ToolPermissionMode mode)
    {
        PermissionMode = mode;
        PermissionModeChanged?.Invoke(mode);
    }

    /// <summary>
    /// 当前权限模式的可读名称。
    /// </summary>
    public string PermissionModeDisplay => PermissionMode switch
    {
        ToolPermissionMode.Default => "default",
        ToolPermissionMode.Plan => "plan",
        ToolPermissionMode.AcceptEdits => "accept-edits",
        ToolPermissionMode.BypassPermissions => "bypass",
        _ => "default"
    };

    /// <summary>当前是否正在运行 agent 对话。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 初始化会话 ViewModel。
    /// </summary>
    /// <param name="services">根级 DI 容器，用于解析 Agent 依赖。</param>
    /// <param name="dispatcher">UI 线程调度器。</param>
    /// <param name="doc">会话文档模型。</param>
    public ConversationViewModel(
        IServiceProvider services,
        IUiDispatcher dispatcher,
        ConversationDocument doc)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _configManager = services.GetRequiredService<ConfigManager>();
    }

    /// <summary>
    /// 初始化 Agent（在首次对话前调用一次）。
    /// </summary>
    public async Task InitializeAsync()
    {
        var workspaceManager = _services.GetRequiredService<IWorkspaceManager>();
        _workspace = workspaceManager.CurrentWorkspace
            ?? throw new InvalidOperationException("未设置当前工作区");

        // 按工作区类型选择 Profile（与 AgiCommand.Execute 一致）
        _profile = _workspace.Type == "Rag"
            ? new RagAgentProfile(_workspace)
            : new NormalAgentProfile();

        _agentFactory = _services.GetRequiredService<ILuBanAgentFactory>();
        _ruleEngine = _services.GetRequiredService<RuleEngine>();
        _pluginRegistry = _services.GetRequiredService<ToolPluginRegistry>();
        _skillRegistry = _services.GetRequiredService<SkillRegistry>();
        _mcpRegistry = _services.GetRequiredService<MCPRegistry>();

        _modelName = _configManager.SelectedModel
            ?? throw new InvalidOperationException("未选择模型（SelectedModel 为 null）");

        // 加载文件级 Skill（项目级 + 用户级）
        var configPath = _workspace.ConfigPath;
        if (configPath != null)
        {
            var skillsDir = Path.Combine(_workspace.RootPath, configPath, "skills");
            _skillRegistry.LoadFromWorkspace(_workspace.RootPath);
        }

        _agent = await _profile.CreateAgentAsync(
            _agentFactory, _modelName, _workspace,
            _ruleEngine, _pluginRegistry, _skillRegistry, _mcpRegistry);

        _dispatcher.Invoke(() =>
            _doc.AppendBlock(new SystemBlock(
                $"模型: {_modelName}  |  工作区: {_workspace.Name}",
                foreground: BlockColors.Success)));
    }

    /// <summary>
    /// 处理用户输入。启动 agent 流式对话，所有输出以 Block 形式追加到文档。
    /// </summary>
    /// <param name="input">用户输入文本。</param>
    public async Task ProcessInputAsync(string input)
    {
        if (IsRunning) return;
        if (string.IsNullOrWhiteSpace(input)) return;
        if (_agent is null) throw new InvalidOperationException("Agent 未初始化");

        IsRunning = true;
        TuiDiag.AgentRunning = true;
        _currentCts = new CancellationTokenSource();

        // 忙碌指示：页脚 spinner 动画（参考 Claude Code 的 waiting 提示），流式结束时停止
        SpinnerService.Start("AI 正在思考… (Esc 取消)");

        try
        {
            // 设置权限模式与确认回调
            SetupConfirmationContext();

            // 追加用户消息
            _dispatcher.Invoke(() => _doc.AppendBlock(new UserMessageBlock(input)));

            await RunStreamingAsync(input, _currentCts.Token);
        }
        catch (OperationCanceledException)
        {
            _dispatcher.Invoke(() =>
                _doc.AppendBlock(new SystemBlock("任务已取消", foreground: BlockColors.Failure)));
        }
        catch (Exception ex)
        {
            Logger.Error("Agent 对话异常", ex);
            _dispatcher.Invoke(() =>
                _doc.AppendBlock(new SystemBlock($"错误: {ex.Message}", foreground: BlockColors.Failure)));
        }
        finally
        {
            IsRunning = false;
            TuiDiag.AgentRunning = false;
            SpinnerService.Stop();
            _currentCts?.Dispose();
            _currentCts = null;
            ResetConfirmationContext();
        }
    }

    /// <summary>
    /// 取消当前对话。
    /// </summary>
    public void Cancel()
    {
        _currentCts?.Cancel();
    }

    /// <summary>
    /// 设置工具确认上下文（每轮对话开始前调用）。
    /// </summary>
    private void SetupConfirmationContext()
    {
        var context = _services.GetRequiredService<ToolConfirmationContext>();
        context.Mode = PermissionMode;
        context.CancellationToken = _currentCts?.Token ?? default;

        // 设置工作区路径检查器：路径在当前工作区内时，非删除类工具免确认
        context.WorkspacePathChecker = path => WorkspaceManager.IsWithinWorkspace(path);

        // BypassPermissions 模式：跳过所有确认，不设置 Callback
        if (PermissionMode == ToolPermissionMode.BypassPermissions)
        {
            return;
        }

        // Plan 模式：收集计划项，不立即确认
        if (PermissionMode == ToolPermissionMode.Plan)
        {
            context.OnPlannedAction = (tool, args) =>
            {
                _dispatcher.Invoke(() =>
                    _doc.AppendBlock(new SystemBlock(
                        $"  📋 计划项: {tool} {TruncateArgs(args)}",
                        foreground: BlockColors.Thinking)));
            };
            return;
        }

        // Default / AcceptEdits：设置确认回调
        context.Callback = (toolName, args) =>
        {
            if (TuiDiag.Enabled) Logger.Warn($"[TuiDiag] confirm enter: {toolName}");

            // 同步确认：用 ManualResetEventSlim 阻塞 agent 线程，
            // 同时在 UI 线程显示 InlineChoiceBlock
            using var done = new ManualResetEventSlim(false);
            var result = false;

            // 注册取消令牌回调：ESC 时 Set 信号以提前解除阻塞
            var ct = _currentCts?.Token ?? default;
            CancellationTokenRegistration ctr = default;
            if (ct.CanBeCanceled)
            {
                ctr = ct.Register(() => done.Set());
            }

            _dispatcher.Invoke(() =>
            {
                var confirmBlock = ChoiceBlocks.Confirm(toolName, args, cr =>
                {
                    result = cr == ConfirmResult.Allow || cr == ConfirmResult.AllowAll;
                    if (cr == ConfirmResult.AllowAll)
                    {
                        context.AllowedThisTurn.Add(toolName);
                    }
                    done.Set();
                });
                _doc.AppendBlock(confirmBlock);
            });

            // 等待用户选择或取消令牌触发（最长 2 分钟超时兜底）
            done.Wait(TimeSpan.FromMinutes(2));
            ctr.Dispose();

            if (TuiDiag.Enabled) Logger.Warn($"[TuiDiag] confirm exit: {toolName} -> {result}");
            return result;
        };
    }

    /// <summary>
    /// 清理确认上下文（每轮对话结束后调用）。
    /// </summary>
    private void ResetConfirmationContext()
    {
        _services.GetRequiredService<ToolConfirmationContext>().Reset();
    }

    private static string TruncateArgs(IReadOnlyDictionary<string, object?> args)
    {
        if (args.Count == 0) return string.Empty;
        var first = args.First();
        var val = first.Value?.ToString() ?? "null";
        if (val.Length > 40) val = val[..37] + "...";
        return $"{first.Key}={val}";
    }

    /// <summary>
    /// 运行流式对话循环——将 agent 输出内容转换为 Block 追加。
    /// 流式文本 token 先入缓冲并按 50ms 节流合批编组到 UI 线程，
    /// 避免逐 token Invoke 洪峰压垮主循环；工具调用/结果先冲刷缓冲再追加，保证文档顺序。
    /// </summary>
    private async Task RunStreamingAsync(string input, CancellationToken ct)
    {
        if (_agent is null) return;

        // 重置当前会话的流式状态（字段级，供跨会话复用的节流回调使用）
        _thinkingBlock = null;
        _thinkingCompleted = false;

        _streamThrottle ??= new FlushThrottle(FlushPendingTokens, TimeSpan.FromMilliseconds(50));

        try
        {
            await foreach (var update in _agent.RunStreamingAsync(input, ct))
            {
                if (update.Contents is null) continue;

                // 边界取证：记录框架每次 yield 的内容类型（定位"只产出 reasoning 就结束"类问题）
                if (TuiDiag.Enabled)
                {
                    Logger.Warn($"[TuiDiag] update: {string.Join(",", update.Contents.Select(c => c.GetType().Name))}");
                }

                foreach (var content in update.Contents)
                {
                    // ─── 思考过程（仅过滤 null/空串，保留换行等空白 token）───
                    if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    {
                        lock (_streamLock) _pendingThinking.Append(reasoning.Text);
                        _streamThrottle.Schedule();
                        continue;
                    }

                    // ─── 工具调用 ───
                    if (content is FunctionCallContent functionCall)
                    {
                        FlushPendingTokens();
                        var toolBlock = new ToolCallBlock(functionCall.Name, functionCall.CallId);

                        _dispatcher.Invoke(() => _doc.AppendBlock(toolBlock));
                        continue;
                    }

                    // ─── 工具结果：不显示返回内容（参考 Claude Code），仅失败时提示一行 ───
                    if (content is FunctionResultContent functionResult)
                    {
                        if (functionResult.Exception is not null)
                        {
                            FlushPendingTokens();
                            _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                                $"❌ 工具执行失败: {functionResult.Exception.Message}",
                                foreground: BlockColors.Failure)));
                        }
                        continue;
                    }

                    // ─── 正文回复（仅过滤 null/空串，保留换行等空白 token）───
                    if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    {
                        lock (_streamLock) _pendingAnswer.Append(text.Text);
                        _streamThrottle.Schedule();
                        continue;
                    }

                    // ─── 流内错误（provider 返回的错误内容，不能静默丢弃）───
                    if (content is ErrorContent error)
                    {
                        FlushPendingTokens();
                        Logger.Warn($"[TuiDiag] ErrorContent: {error.Message}");
                        _dispatcher.Invoke(() => _doc.AppendBlock(
                            new SystemBlock($"错误: {error.Message}", foreground: BlockColors.Failure)));
                        continue;
                    }

                    // ─── 其余内容类型（UsageContent 等）：仅诊断模式下记录 ───
                    if (TuiDiag.Enabled)
                    {
                        Logger.Warn($"[TuiDiag] unhandled content: {content.GetType().Name}");
                    }
                }
            }

            if (TuiDiag.Enabled)
            {
                Logger.Warn("[TuiDiag] stream completed normally");
            }
        }
        finally
        {
            // 取消/异常路径也冲刷剩余 token，保证已产出内容不丢失
            FlushPendingTokens();
        }

        // 流式结束——对最后一个 Block 做最终布局并标记完成。
        // 收尾 Invoke 排在冲刷 Invoke 之后（TimedEvents FIFO），顺序安全。
        _dispatcher.Invoke(() =>
        {
            // 补一次最终布局：合批期间追加的尾部 token 需要进入 LineCount/TotalLines 账本
            _doc.RelayoutLastBlock();
            _doc.MarkLastComplete();
        });
    }

    /// <summary>
    /// 将缓冲的思考/正文 token 一次性编组到 UI 线程追加。
    /// 实例方法（非闭包）：节流器跨会话复用，会话状态通过字段访问，避免上一会话的状态泄漏。
    /// </summary>
    private void FlushPendingTokens()
    {
        string thinking;
        string answer;

        lock (_streamLock)
        {
            thinking = _pendingThinking.ToString();
            _pendingThinking.Clear();
            answer = _pendingAnswer.ToString();
            _pendingAnswer.Clear();
        }

        if (thinking.Length == 0 && answer.Length == 0) return;

        TuiDiag.Record("StreamFlush.chars", thinking.Length + answer.Length, thresholdMs: 0);

        _dispatcher.Invoke(() =>
        {
            if (thinking.Length > 0)
            {
                if (_thinkingBlock is null)
                {
                    _thinkingBlock = new ThinkingBlock();
                    _doc.AppendBlock(_thinkingBlock);
                }

                _thinkingBlock.AppendContent(thinking);
                _doc.NotifyBlockChanged(_thinkingBlock);
            }

            if (answer.Length > 0)
            {
                // 正文开始时标记思考过程完成
                if (_thinkingBlock is not null && !_thinkingCompleted)
                {
                    _thinkingCompleted = true;
                    _thinkingBlock.MarkComplete();
                }

                _doc.AppendToAnswerBlock(answer);
                _doc.RelayoutLastBlock();
            }
        });
    }
}
