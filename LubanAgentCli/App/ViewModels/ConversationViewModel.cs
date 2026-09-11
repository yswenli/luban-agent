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
using LubanAgentCli.App.Services;
using LubanAgentCli.App.Views;
using LubanAgentCore.Services;

namespace LubanAgentCli.App.ViewModels;

/// <summary>
/// 会话 ViewModel。负责创建 Agent、运行流式对话循环、将 AI 更新编排为 Block 追加。
/// 所有 UI 更新通过 <see cref="IUiDispatcher.Invoke"/> 编组到 UI 线程，
/// 本类不直接持有 View 引用。
/// </summary>
internal sealed class ConversationViewModel : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IUiDispatcher _dispatcher;
    private readonly ConversationDocument _doc;
    private readonly ConfigManager _configManager;
    private readonly TitleService _titleService;
    private readonly SessionManager? _sessionManager;
    private readonly ISessionManager _iSessionManager;

    private LuBanAgent? _agent;
    private ILuBanAgentFactory? _agentFactory;
    private AgentProfile? _profile;
    private RuleEngine? _ruleEngine;
    private ToolPluginRegistry? _pluginRegistry;
    private SkillRegistry? _skillRegistry;
    private MCPRegistry? _mcpRegistry;
    private WorkspaceInfo? _workspace;
    private string? _modelName;

    // 上下文同步：防重入信号量 + 运行中延迟同步标志 + 突发合并调度标志
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private volatile bool _pendingSync;
    // 突发合并：避免同一同步突发（如切工作区先后触发工作区变更+会话变更两次事件）重复清文档/重载历史
    private int _syncScheduled;

    // 已加载到文档的会话标识：用于避免会话历史被重复加载（如初始化前已切换过工作区/会话）
    private string? _loadedSessionId;

    // 由 DI 解析并缓存的引用，用于事件订阅/退订与页脚刷新
    private readonly WorkspaceManager? _workspaceManager;
    private FooterView? _footerView;
    private FooterDataProvider? _footerProvider;

    private CancellationTokenSource? _currentCts;

    // Plan 模式下本轮收集的计划项数量（回合结束时汇总提示，工具均不执行）
    private int _plannedCount;

    // 流式 token 合批缓冲：agent 线程追加、节流冲刷时取出，替代逐 token Invoke 洪峰
    private readonly object _streamLock = new();
    private readonly StringBuilder _pendingThinking = new();
    private readonly StringBuilder _pendingAnswer = new();
    private FlushThrottle? _streamThrottle;

    // 当前会话的流式状态（每次 RunStreamingAsync 开始重置；
    // 必须是字段而非闭包局部变量——节流器跨会话复用，闭包会把上一会话的状态泄漏到下一会话）
    private ThinkingBlock? _thinkingBlock;
    private bool _thinkingCompleted;
    private ActionSpinnerBlock? _currentSpinner;

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

    /// <summary>历史回放的消息条数限制。</summary>
    private const int HistoryLoadLimit = 20;

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
        _titleService = services.GetRequiredService<TitleService>();

        _iSessionManager = services.GetRequiredService<ISessionManager>();
        if (_iSessionManager is SessionManager sm)
        {
            _sessionManager = sm;
            sm.CurrentSessionChanged += OnCurrentSessionChanged;
        }

        _workspaceManager = services.GetRequiredService<IWorkspaceManager>() as WorkspaceManager;
        if (_workspaceManager is not null)
        {
            _workspaceManager.CurrentWorkspaceChanged += OnCurrentWorkspaceChanged;
        }
        _configManager.SelectedModelChanged += OnSelectedModelChanged;
    }

    private void OnCurrentSessionChanged(string sessionId)
    {
        RequestSync();
    }

    private void OnCurrentWorkspaceChanged(WorkspaceInfo ws)
    {
        RequestSync();
    }

    private void OnSelectedModelChanged(string? model)
    {
        RequestSync();
    }

    /// <summary>
    /// 注入页脚视图与数据提供者，使上下文切换能刷新状态栏。
    /// </summary>
    public void SetFooter(FooterView footer, FooterDataProvider provider)
    {
        _footerView = footer;
        _footerProvider = provider;
    }

    /// <summary>
    /// 同步当前运行上下文（工作区/模型/会话）到 UI 与 Agent。
    /// 在以下场景调用：首次初始化、切换工作区、切换模型、切换会话。
    /// 会按需重建 Agent、清空并重建会话文档、加载当前会话历史、刷新状态栏。
    /// </summary>
    /// <param name="clearDoc">是否清空会话文档（切换上下文时应清空；首次初始化保留启动横幅）。</param>
    private async Task SyncContextAsync(bool clearDoc = true)
    {
        // Agent 运行中不可重建：标记待同步，待本轮结束后再执行，避免破坏运行中的 Agent 状态
        if (IsRunning)
        {
            _pendingSync = true;
            return;
        }

        // 调用方（InitializeAsync / RequestSync）负责持有 _syncGate；此处不再自行加锁，避免同一线程重入死锁
        {
            var wsMgr = _services.GetRequiredService<IWorkspaceManager>();
            var ws = wsMgr.CurrentWorkspace;
            var model = _configManager.SelectedModel;
            var session = _iSessionManager.CurrentSession;

            // 仅当工作区或模型发生变化时才重建 Agent，避免无谓重建
            var needRebuild = _agent is null
                || _workspace?.WorkspaceId != ws?.WorkspaceId
                || _modelName != model;

            _dispatcher.Invoke(() =>
            {
                if (clearDoc) _doc.Clear();
            });

            if (needRebuild)
            {
                if (ws is null)
                    throw new InvalidOperationException("未设置当前工作区");
                if (string.IsNullOrEmpty(model))
                    throw new InvalidOperationException("未选择模型（SelectedModel 为 null）");

                _agentFactory ??= _services.GetRequiredService<ILuBanAgentFactory>();
                _ruleEngine ??= _services.GetRequiredService<RuleEngine>();
                _pluginRegistry ??= _services.GetRequiredService<ToolPluginRegistry>();
                _skillRegistry ??= _services.GetRequiredService<SkillRegistry>();
                _mcpRegistry ??= _services.GetRequiredService<MCPRegistry>();

                _workspace = ws;
                _modelName = model;
                _profile = ws.Type == "Rag" ? new RagAgentProfile(ws) : new NormalAgentProfile();
                var newAgent = await _profile.CreateAgentAsync(
                    _agentFactory, _modelName, _workspace,
                    _ruleEngine, _pluginRegistry, _skillRegistry, _mcpRegistry);
                // 释放旧 Agent 实例，避免重建时连接/句柄等资源泄漏
                if (_agent is IDisposable oldAgent)
                {
                    try { oldAgent.Dispose(); }
                    catch (Exception ex) { Logger.Warn($"释放旧 Agent 失败: {ex.Message}"); }
                }
                _agent = newAgent;
            }

            _titleService.SetWorkspace(ws?.Name ?? "-");
            _titleService.SetModel(model ?? "-");
            _titleService.SetSessionTitle(session?.Title ?? "新会话");

            if (_footerProvider is not null)
            {
                _footerProvider.WorkspaceName = ws?.Name ?? "-";
                _footerProvider.ModelName = model ?? "-";
                _footerProvider.SessionTitle = session?.Title ?? "新会话";
            }

            if (session is not null)
            {
                // 仅在首次加载、会话切换或强制刷新(clearDoc)时重载历史，避免重复加载
                if (clearDoc || _loadedSessionId != session.SessionId)
                {
                    await LoadHistoryAsync(session.SessionId);
                    _loadedSessionId = session.SessionId;
                }
            }
            else
            {
                _loadedSessionId = null;
            }

            _dispatcher.Invoke(() =>
            {
                // 仅首次初始化（不清空文档）时追加一条状态行；切换场景由状态栏常驻显示
                if (!clearDoc)
                {
                    _doc.AppendBlock(new SystemBlock(
                        $"模型: {model ?? "-"}  |  工作区: {ws?.Name ?? "-"}  |  会话: {session?.Title ?? "新会话"}",
                        foreground: BlockColors.Success));
                }
                _footerView?.SetNeedsDraw();
            });
        }
    }

    /// <summary>
    /// 初始化 Agent（首次对话前调用一次）。
    /// 统一走 <see cref="SyncContextAsync"/> 构建 Agent 与加载会话，并保留启动横幅（不清空文档）。
    /// </summary>
    public async Task InitializeAsync()
    {
        await _syncGate.WaitAsync();
        try
        {
            await SyncContextAsync(clearDoc: false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    /// <summary>
    /// 请求一次上下文同步，并合并同一突发内的多次事件
    /// （例如切换工作区会依次触发“工作区变更”与“当前会话变更”两个事件）。
    /// 通过 20ms 去抖 + 单次调度，把多次事件合并为一次清文档/重载历史，
    /// 既避免重复渲染，也保证读取到突发结束后的最终状态。
    /// </summary>
    private void RequestSync()
    {
        // 已有同步在排队/执行中：本次事件的状态变更会在那一次执行时被读取，无需重复调度
        if (Interlocked.Exchange(ref _syncScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(20);
                await _syncGate.WaitAsync();
                try
                {
                    await SyncContextAsync();
                }
                finally
                {
                    _syncGate.Release();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("同步上下文失败", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _syncScheduled, 0);
            }
        });
    }

    /// <summary>
    /// 加载会话历史到显示区（最近 N 条 user/assistant 消息）。
    /// </summary>
    public async Task LoadHistoryAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        var messages = await _iSessionManager.GetLatestMessagesAsync(sessionId, HistoryLoadLimit + 1);

        var list = messages.ToList();
        var hasMore = list.Count > HistoryLoadLimit;
        if (hasMore)
        {
            list = list.Take(HistoryLoadLimit).ToList();
        }

        _dispatcher.Invoke(() =>
        {
            if (hasMore)
            {
                _doc.AppendBlock(new SystemBlock(
                    $"…更早消息未显示（已加载最近 {HistoryLoadLimit} 条）",
                    foreground: BlockColors.System));
            }

            foreach (var msg in list)
            {
                if (msg.Role == "user")
                {
                    _doc.AppendBlock(new UserMessageBlock(msg.Content));
                }
                else if (msg.Role == "assistant")
                {
                    if (!string.IsNullOrWhiteSpace(msg.Thinking))
                    {
                        var tBlock = new ThinkingBlock();
                        tBlock.AppendContent(msg.Thinking);
                        tBlock.MarkComplete();
                        _doc.AppendBlock(tBlock);
                    }
                    var block = new AssistantMessageBlock();
                    block.AppendContent(msg.Content);
                    block.MarkComplete();
                    _doc.AppendBlock(block);
                }
            }
        });
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
            ReportPlannedActions();
            ResetConfirmationContext();

            // 运行期间发生的上下文切换（工作区/模型/会话）被延迟，
            // 本轮结束后再触发一次同步，避免破坏运行中的 Agent 状态
            if (_pendingSync)
            {
                _pendingSync = false;
                RequestSync();
            }
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
    /// 模式策略由框架统一分发，此处只装配回调：Bypass 全放行、
    /// Plan 仅记录计划项且不执行、AcceptEdits 放行编辑类、Default 逐项确认。
    /// </summary>
    private void SetupConfirmationContext()
    {
        var context = _services.GetRequiredService<ToolConfirmationContext>();
        Interlocked.Exchange(ref _plannedCount, 0);

        context.ConfigureForTurn(
            PermissionMode,
            _currentCts?.Token ?? default,
            confirmCallback: ConfirmTool,
            onPlannedAction: (tool, args) =>
            {
                Interlocked.Increment(ref _plannedCount);
                _dispatcher.Invoke(() =>
                    _doc.AppendBlock(new SystemBlock(
                        $"  📋 计划项（Plan 模式，未执行）: {tool} {ToolArgsFormatter.Summarize(args)}",
                        foreground: BlockColors.Thinking)));
            });
    }

    /// <summary>
    /// 人工确认回调：阻塞 agent 线程，等待用户在 TUI 内联选择块上作出决定。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="args">工具参数。</param>
    /// <returns>用户是否允许执行。</returns>
    private bool ConfirmTool(string toolName, IReadOnlyDictionary<string, object?> args)
    {
        if (TuiDiag.Enabled) Logger.Warn($"[TuiDiag] confirm enter: {toolName}");

        var context = _services.GetRequiredService<ToolConfirmationContext>();

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
    }

    /// <summary>
    /// Plan 模式回合结束时汇总：本轮计划项均未执行，提示切换模式后重新发起。
    /// </summary>
    private void ReportPlannedActions()
    {
        var count = Volatile.Read(ref _plannedCount);
        if (PermissionMode != ToolPermissionMode.Plan || count == 0)
        {
            return;
        }

        _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
            $"📋 Plan 模式：本轮记录 {count} 个计划项，均未执行。"
            + "按 Shift+Tab 切到 default / accept-edits 后重新发起即可执行。",
            foreground: BlockColors.Accent)));
    }

    /// <summary>
    /// 清理确认上下文（每轮对话结束后调用）。
    /// </summary>
    private void ResetConfirmationContext()
    {
        _services.GetRequiredService<ToolConfirmationContext>().Reset();
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
        _currentSpinner = null;

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
                        // 首次思考 → 插入 spinner
                        _dispatcher.Invoke(() =>
                        {
                            if (_currentSpinner is null)
                            {
                                _currentSpinner = new ActionSpinnerBlock("AI 正在思考…", _doc, _dispatcher);
                                _doc.AppendBlock(_currentSpinner);
                            }
                        });

                        lock (_streamLock) _pendingThinking.Append(reasoning.Text);
                        _streamThrottle.Schedule();
                        continue;
                    }

                    // ─── 工具调用 ───
                    if (content is FunctionCallContent functionCall)
                    {
                        FlushPendingTokens();

                        // 完成上一个 spinner 并插入新 spinner（全部在 UI 线程）
                        _dispatcher.Invoke(() =>
                        {
                            if (_currentSpinner is not null)
                            {
                                _currentSpinner.MarkComplete();
                                _currentSpinner = null;
                            }

                            _currentSpinner = new ActionSpinnerBlock($"正在调用工具 {functionCall.Name}…", _doc, _dispatcher);
                            _doc.AppendBlock(_currentSpinner);
                        });

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
                        // 首次正文 → 插入 spinner
                        _dispatcher.Invoke(() =>
                        {
                            if (_currentSpinner is null)
                            {
                                _currentSpinner = new ActionSpinnerBlock("正在生成回复…", _doc, _dispatcher);
                                _doc.AppendBlock(_currentSpinner);
                            }
                        });

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

                    // ─── UsageContent：token 使用统计，无需渲染，静默跳过 ───
                    if (content is UsageContent)
                    {
                        continue;
                    }

                    // ─── 其余内容类型：诊断模式下记录 ───
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

            // 完成最后一个 spinner（无论正常完成还是异常/取消）
            _dispatcher.Invoke(() =>
            {
                if (_currentSpinner is not null)
                {
                    _currentSpinner.MarkComplete();
                    _currentSpinner = null;
                }

                // 补一次最终布局：合批期间追加的尾部 token 需要进入 LineCount/TotalLines 账本
                _doc.RelayoutLastBlock();
                _doc.MarkLastComplete();
            });
        }
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

        using var flushScope = TuiDiag.Measure("StreamFlush.total", thresholdMs: 0);

        _dispatcher.Invoke(() =>
        {
            using var invokeScope = TuiDiag.Measure("StreamFlush.Invoke", thresholdMs: 0);
            
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
                if (_thinkingBlock is not null && !_thinkingCompleted)
                {
                    _thinkingCompleted = true;
                    _thinkingBlock.MarkComplete();
                    _doc.NotifyBlockChanged(_thinkingBlock);
                }

                _doc.AppendToAnswerBlock(answer);
                _doc.RelayoutLastBlock();
            }
        });
    }

    /// <summary>
    /// 取消事件订阅，释放资源。
    /// </summary>
    public void Dispose()
    {
        if (_sessionManager is not null)
        {
            _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
        if (_workspaceManager is not null)
        {
            _workspaceManager.CurrentWorkspaceChanged -= OnCurrentWorkspaceChanged;
        }
        _configManager.SelectedModelChanged -= OnSelectedModelChanged;
        _syncGate.Dispose();
    }
}
