/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels
*文件名： MainWindowViewModel
*版本号： V1.0.0.0
*唯一标识：主窗口 ViewModel
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：主窗口 ViewModel，管理消息流、会话和 Agent 交互
*
*****************************************************************************/
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LubanAgentCore.Commands;
using LubanAgentCore.Services;
using LubanAgentCore.Utils;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Errors;
using LuBan.AIAgent.Orchestration;
using Microsoft.Extensions.Options;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCodex.Views;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.A2A;
using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.Sessions;
using LubanAgentA2A.Configuration;
using LubanAgentA2A.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.ViewModels;

/// <summary>
/// 主窗口 ViewModel
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly AgentHostService _agentHost;

    /// <summary>
    /// 编排进度出口（DI 单例）。编排由工具调用触发、执行在工具内部，进度不经对话流，
    /// 由各 <see cref="SessionViewModel"/> 在运行期间自行订阅/退订。
    /// </summary>
    private readonly HostOrchestrationProgressSink? _orchestrationSink;

    // 每会话运行态：键为 sessionId，值为该会话独立的 ViewModel（消息流 + 运行态）
    private readonly Dictionary<string, SessionViewModel> _sessions = new(StringComparer.Ordinal);
    private SessionViewModel? _currentSession;
    private static readonly ObservableCollection<MessageItemBase> EmptyMessages = new();

    private readonly List<string> _pendingAttachmentPaths = new();
    private int _attachmentVersion;
    private Task _attachmentRefreshTask = Task.CompletedTask;

    /// <summary>当前待发送附件。</summary>
    public IReadOnlyList<AttachmentDisplayInfo> PendingAttachments { get; private set; } = Array.Empty<AttachmentDisplayInfo>();

    /// <summary>附件列表变化通知。</summary>
    public event EventHandler? AttachmentsChanged;

    /// <summary>添加附件路径（去重、上限 5、仅受支持且存在的文件）。返回实际加入的路径数。</summary>
    /// <remarks>校验同步完成；解码/缩略图等重活异步刷新，避免阻塞 UI 线程。</remarks>
    public int AddAttachments(IEnumerable<string> paths)
    {
        var service = Services.GetRequiredService<AttachmentService>();
        var added = 0;
        foreach (var path in paths)
        {
            if (_pendingAttachmentPaths.Count >= AttachmentService.MaxAttachmentsPerMessage)
                break;

            string full;
            try
            {
                full = System.IO.Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(full) || !service.IsSupported(full))
                continue;
            if (_pendingAttachmentPaths.Contains(full, StringComparer.OrdinalIgnoreCase))
                continue;

            _pendingAttachmentPaths.Add(full);
            added++;
        }

        if (added > 0)
        {
            _attachmentVersion++;
            _attachmentRefreshTask = RefreshPendingAttachmentsAsync(service);
        }
        return added;
    }

    /// <summary>移除指定附件（参数为完整路径；兼容按文件名移除的旧调用）。</summary>
    public void RemoveAttachment(string fileNameOrPath)
    {
        var target = _pendingAttachmentPaths.FirstOrDefault(p =>
            string.Equals(p, fileNameOrPath, StringComparison.OrdinalIgnoreCase))
            ?? _pendingAttachmentPaths.FirstOrDefault(p =>
                string.Equals(System.IO.Path.GetFileName(p), fileNameOrPath, StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        _pendingAttachmentPaths.Remove(target);
        _attachmentVersion++;
        _attachmentRefreshTask = RefreshPendingAttachmentsAsync(Services.GetRequiredService<AttachmentService>());
    }

    /// <summary>
    /// 异步处理待发附件：逐文件解码/生成缩略图并更新预览；个别文件失败只丢弃该文件。
    /// 处理期间不阻塞 UI 线程；处理结果回投 UI 线程后再更新集合（避免跨线程访问）。
    /// </summary>
    private async Task RefreshPendingAttachmentsAsync(AttachmentService service)
    {
        var version = _attachmentVersion;
        var paths = _pendingAttachmentPaths.ToList();

        var modelId = Services.GetService<ConfigManager>()?.SelectedModel ?? "";
        var supportsVision = ModelCapability.SupportsVision(modelId);

        var infos = new List<AttachmentDisplayInfo>(paths.Count);
        var valid = new List<string>(paths.Count);
        var imagesRejected = new List<string>();
        var failures = new List<string>();

        if (paths.Count > 0)
        {
            await Task.Run(async () =>
            {
                foreach (var path in paths)
                {
                    try
                    {
                        var processed = await service.ProcessAsync(new[] { path }, generateThumbnails: true)
                            .ConfigureAwait(false);
                        var info = AttachmentService.ToDisplayInfos(processed)[0];

                        if (info.IsImage && !supportsVision)
                        {
                            imagesRejected.Add(info.FileName);
                            continue;
                        }

                        infos.Add(info);
                        valid.Add(path);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{System.IO.Path.GetFileName(path)} - {ex.Message}");
                    }
                }
            });
        }

        // 刷新期间列表又被改动（新增/移除/清空/发送）：放弃本次过期结果，由最新一次刷新收尾
        if (version != _attachmentVersion)
            return;

        _pendingAttachmentPaths.Clear();
        _pendingAttachmentPaths.AddRange(valid);
        PendingAttachments = infos;
        AttachmentsChanged?.Invoke(this, EventArgs.Empty);

        foreach (var failure in failures)
        {
            Messages.Add(new SystemMessageItem
            {
                Content = $"附件不可用：{failure}",
                IsError = true
            });
        }

        if (imagesRejected.Count > 0)
        {
            Messages.Add(new SystemMessageItem
            {
                Content = $"当前模型 {modelId} 不支持图片，已忽略：{string.Join("、", imagesRejected)}。请切换支持 vision 的模型。",
                IsError = true
            });
        }
    }

    /// <summary>等待进行中的附件处理完成（发送前调用，确保待发路径已校验）。</summary>
    private async Task WaitForAttachmentsAsync()
    {
        try
        {
            await _attachmentRefreshTask;
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindowViewModel.WaitForAttachmentsAsync 附件处理失败", ex);
        }
    }

    /// <summary>清空待发送附件（切换/清空会话时调用）。</summary>
    public void ClearPendingAttachments()
    {
        if (_pendingAttachmentPaths.Count == 0 && PendingAttachments.Count == 0)
            return;
        _attachmentVersion++;
        _attachmentRefreshTask = Task.CompletedTask;
        _pendingAttachmentPaths.Clear();
        PendingAttachments = Array.Empty<AttachmentDisplayInfo>();
        AttachmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 服务提供者
    /// </summary>
    public IServiceProvider Services => _agentHost.Services;

    /// <summary>
    /// 命令清单（用于 InputBox 补全菜单过滤）
    /// </summary>
    public ICommandCatalog CommandCatalog { get; }

    /// <summary>
    /// 请求退出应用时触发（由 MainWindow 订阅调用 Close）
    /// </summary>
    public event Action? ExitRequested;

    /// <summary>
    /// 输入文本
    /// </summary>
    [ObservableProperty]
    private string _inputText = "";

    /// <summary>
    /// 当前会话是否正在运行（转发自 <see cref="CurrentSession"/>；切换会话时同步通知）
    /// </summary>
    public bool IsRunning => _currentSession?.IsRunning ?? false;

    /// <summary>
    /// 当前选中会话（其消息流即 <see cref="Messages"/>）
    /// </summary>
    public SessionViewModel? CurrentSession => _currentSession;

    /// <summary>
    /// 是否正在切换会话（用于显示中央加载提示）
    /// </summary>
    [ObservableProperty]
    private bool _isSwitchingSession;

    /// <summary>
    /// 是否显示忙碌遮罩（知识库索引/重建等长耗时任务，与切换会话共用同一遮罩）
    /// </summary>
    [ObservableProperty]
    private bool _busyOverlayVisible;

    /// <summary>
    /// 忙碌遮罩状态文本
    /// </summary>
    [ObservableProperty]
    private string _busyOverlayText = "正在处理…";

    /// <summary>
    /// 权限模式
    /// </summary>
    [ObservableProperty]
    private ToolPermissionMode _permissionMode = ToolPermissionMode.Default;

    /// <summary>
    /// 当前会话的消息集合（转发；切换会话时触发 <c>PropertyChanged</c> 供视图重新绑定）
    /// </summary>
    public ObservableCollection<MessageItemBase> Messages => _currentSession?.Messages ?? EmptyMessages;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="services">服务提供者</param>
    public MainWindowViewModel(IServiceProvider services)
    {
        _agentHost = services.GetRequiredService<AgentHostService>();
        _orchestrationSink = services.GetService<IOrchestrationProgressSink>() as HostOrchestrationProgressSink;
        CommandCatalog = services.GetRequiredService<ICommandCatalog>();

        // 自动重试进度提示：框架回调运行在调用线程，编组到 UI 线程追加系统消息
        // （显式使用 Normal 优先级，避免低于 await 续体优先级导致通知显示在终结消息之后）
        services.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.OnApiRetry = notice =>
            Dispatcher.UIThread.Post(() => Messages.Add(new SystemMessageItem
            {
                Content = $"⟳ {ApiErrorClassifier.GetCategoryLabel(notice.Category)}：{notice.Delay.TotalSeconds:F0}s 后重试（第 {notice.Attempt} 次，最多 {notice.MaxAttempts - 1} 次）"
            }), DispatcherPriority.Normal);
    }

    /// <summary>
    /// 发送消息命令
    /// </summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        var session = EnsureCurrentSession();
        if (session is null)
        {
            Messages.Add(new SystemMessageItem { Content = "未设置当前会话", IsError = true });
            return;
        }

        if (session.IsRunning || (string.IsNullOrWhiteSpace(InputText) && _pendingAttachmentPaths.Count == 0))
            return;

        var input = InputText.Trim();

        // 处理 / 命令
        if (input.StartsWith('/'))
        {
            InputText = "";
            await ExecuteCommandAsync(input);
            return;
        }

        // 图片附件 + 非 vision 模型：提示并中止（拖拽/粘贴可能绕过按钮禁用）
        // 先等待附件处理完成，确保 PendingAttachments 反映最新状态
        await WaitForAttachmentsAsync();

        if (PendingAttachments.Any(a => a.IsImage))
        {
            var modelId = Services.GetRequiredService<ConfigManager>().SelectedModel ?? "";
            if (!ModelCapability.SupportsVision(modelId))
            {
                Messages.Add(new SystemMessageItem
                {
                    Content = $"当前模型 {modelId} 不支持图片，请切换支持 vision 的模型或移除图片附件。",
                    IsError = true
                });
                return;
            }
        }

        if (!_agentHost.IsInitialized)
        {
            try
            {
                await _agentHost.InitializeAsync();
            }
            catch (Exception ex)
            {
                var message = ex is AgentApiException apiEx ? apiEx.Error.FriendlyMessage : ex.Message;
                Messages.Add(new SystemMessageItem
                {
                    Content = $"初始化失败: {message}",
                    IsError = true
                });
                return;
            }
        }

        var displayInfos = PendingAttachments;
        var sendingPaths = _pendingAttachmentPaths.ToList();
        _pendingAttachmentPaths.Clear();
        PendingAttachments = Array.Empty<AttachmentDisplayInfo>();
        AttachmentsChanged?.Invoke(this, EventArgs.Empty);

        InputText = "";

        // 交由该会话独立运行：切换显示不影响其后台继续推进
        await session.SendAsync(input, sendingPaths, displayInfos);
    }

    /// <summary>
    /// 初始化工作区 AGENTS.md：把生成提示词投入聊天流，落盘后追加校验提示。
    /// 复用会话 ViewModel 的运行态装配（IsRunning / 取消 / 节流）。
    /// </summary>
    /// <param name="options">表单收集的生成选项</param>
    public async Task RunInitAgentsMdAsync(AgentsMdOptions options)
    {
        var initializer = Services.GetRequiredService<IAgentsMdInitializer>();
        var workspaceManager = Services.GetRequiredService<IWorkspaceManager>();
        var workspace = workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            Messages.Add(new SystemMessageItem { Content = "未设置当前工作区", IsError = true });
            return;
        }

        var session = EnsureCurrentSession();
        if (session is null || session.IsRunning) return;

        if (!_agentHost.IsInitialized)
        {
            try
            {
                await _agentHost.InitializeAsync();
            }
            catch (Exception ex)
            {
                Messages.Add(new SystemMessageItem { Content = $"初始化失败: {ex.Message}", IsError = true });
                return;
            }
        }

        string prompt;
        try
        {
            prompt = initializer.BuildPrompt(options);
        }
        catch (Exception ex)
        {
            Messages.Add(new SystemMessageItem { Content = $"构建提示词失败: {ex.Message}", IsError = true });
            return;
        }

        Messages.Add(new UserMessageItem
        {
            Content = $"初始化工作区 AGENTS.md：{options.AgentName}（{options.AgentScene}，{options.OutputMode}）"
        });

        try
        {
            var text = await session.RunPromptAsync(prompt);

            var markdown = initializer.Normalize(text);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                Messages.Add(new SystemMessageItem { Content = "生成结果为空，AGENTS.md 未写入", IsError = true });
                return;
            }

            var targetPath = initializer.GetTargetPath(workspace);
            await initializer.WriteAsync(workspace, markdown, CancellationToken.None);

            Messages.Add(new SystemMessageItem { Content = $"已生成 {targetPath}" });
            Messages.Add(new SystemMessageItem { Content = initializer.BuildVerificationPrompt(targetPath) });

            // 让下一回合的 Agent 重建，从而读到新的 AGENTS.md
            _agentHost.Reset();
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new SystemMessageItem { Content = "已取消" });
        }
        catch (Exception ex)
        {
            var message = ex is AgentApiException apiEx ? apiEx.Error.FriendlyMessage : ex.Message;
            Messages.Add(new SystemMessageItem { Content = $"错误: {message}", IsError = true });
        }
    }

    /// <summary>
    /// 任意会话运行状态变化（含后台非当前会话），供侧栏更新运行徽标。
    /// </summary>
    public event EventHandler<SessionRunningChangedEventArgs>? SessionRunningChanged;

    /// <summary>
    /// 取当前会话对应的会话 ViewModel 并设为当前选中；无当前会话时返回 null。
    /// </summary>
    private SessionViewModel? EnsureCurrentSession()
    {
        var sessionId = Services.GetService<ISessionManager>()?.CurrentSession?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var session = GetOrCreateSession(sessionId);
        SetCurrentSession(session);
        return session;
    }

    /// <summary>
    /// 按 sessionId 获取或创建会话 ViewModel。
    /// </summary>
    private SessionViewModel GetOrCreateSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            session = new SessionViewModel(sessionId, _agentHost, _orchestrationSink, () => PermissionMode);
            session.PropertyChanged += OnSessionPropertyChanged;
            _sessions[sessionId] = session;
        }
        return session;
    }

    /// <summary>
    /// 查询指定会话是否正在运行（供侧栏重建列表时恢复徽标）。
    /// </summary>
    public bool IsSessionRunning(string sessionId)
        => _sessions.TryGetValue(sessionId, out var session) && session.IsRunning;

    /// <summary>
    /// 切换当前选中会话：转发消息集合与运行状态，并通知视图重新绑定消息流。
    /// </summary>
    private void SetCurrentSession(SessionViewModel session)
    {
        if (ReferenceEquals(_currentSession, session))
            return;

        _currentSession = session;

        OnPropertyChanged(nameof(CurrentSession));
        OnPropertyChanged(nameof(Messages));
        OnPropertyChanged(nameof(IsRunning));
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionViewModel.IsRunning) || sender is not SessionViewModel session)
            return;

        if (ReferenceEquals(session, _currentSession))
            OnPropertyChanged(nameof(IsRunning));

        SessionRunningChanged?.Invoke(this, new SessionRunningChangedEventArgs(session.SessionId, session.IsRunning));
    }

    /// <summary>
    /// 取消命令
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // 仅取消当前会话；其他会话若在后台运行则继续
        _currentSession?.Cancel();
    }

    /// <summary>
    /// 重置 Agent，下次发送消息时按最新配置（如切换后的模型）重建
    /// </summary>
    public void ResetAgent() => _agentHost.Reset();

    /// <summary>
    /// 执行 / 命令
    /// </summary>
    private async Task ExecuteCommandAsync(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmd = parts[0].ToLowerInvariant();
        var args = ExpandSubCommandAliases(cmd, parts.Skip(1).ToArray());

        switch (cmd)
        {
            case "/help":
                ShowHelp();
                break;

            case "/exit":
            case "/quit":
                ExitRequested?.Invoke();
                break;

            case "/clear":
                ClearMessages();
                Messages.Add(new SystemMessageItem { Content = "会话已清空" });
                break;

            case "/mode":
                if (args.Length > 0)
                {
                    var mode = args[0].ToLowerInvariant() switch
                    {
                        "default" => ToolPermissionMode.Default,
                        "plan" => ToolPermissionMode.Plan,
                        "accept-edits" or "acceptedits" => ToolPermissionMode.AcceptEdits,
                        "bypass" or "bypasspermissions" => ToolPermissionMode.BypassPermissions,
                        _ => (ToolPermissionMode?)null
                    };

                    if (mode.HasValue)
                    {
                        PermissionMode = mode.Value;
                        Messages.Add(new SystemMessageItem
                        {
                            Content = $"权限模式已切换: {GetPermissionModeDisplay(PermissionMode)}"
                        });
                    }
                    else
                    {
                        Messages.Add(new SystemMessageItem
                        {
                            Content = $"无效模式: {args[0]}. 可用: default, plan, accept-edits, bypass",
                            IsError = true
                        });
                    }
                }
                else
                {
                    Messages.Add(new SystemMessageItem
                    {
                        Content = $"当前权限模式: {GetPermissionModeDisplay(PermissionMode)}\n可用模式: default / plan / accept-edits / bypass"
                    });
                }
                break;

            case "/model":
            case "/m":
                await ExecuteModelCommandAsync(args);
                break;

            case "/session":
            case "/se":
                await ExecuteSessionCommandAsync(args);
                break;

            case "/stats":
            case "/st":
                ShowStats();
                break;

            case "/provider":
            case "/p":
                ShowProviderManager(args);
                break;

            case "/skill":
            case "/sk":
                ShowSkillManager(args);
                break;

            case "/rule":
            case "/r":
                ShowRuleManager(args);
                break;

            case "/mcp":
            case "/mp":
                ShowMcpManager(args);
                break;

            case "/work":
            case "/w":
                ShowWorkManager(args);
                break;

            case "/llmwiki":
            case "/lw":
            case "/rag":
            case "/rg":
            case "/wiki":
            case "/wk":
                ShowLlmWikiManager(args);
                break;

            case "/a2a":
                await ExecuteA2ACommandAsync(args);
                break;

            default:
                Messages.Add(new SystemMessageItem
                {
                    Content = $"未知命令: {cmd}，输入 /help 查看可用命令",
                    IsError = true
                });
                break;
        }
    }

    /// <summary>
    /// 子命令简写展开（-s 按命令区分：/work 为 -switch，/llmwiki 为 -search）
    /// </summary>
    private static string[] ExpandSubCommandAliases(string cmd, string[] parts)
    {
        var isLlmWiki = cmd is "/llmwiki" or "/lw" or "/rag" or "/rg" or "/wiki" or "/wk";
        var isA2A = cmd == "/a2a";
        var result = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            result[i] = parts[i] switch
            {
                "-l" => "-list",
                "-a" => "-add",
                "-u" => "-update",
                "-d" => "-delete",
                "-s" => isA2A ? "-server" : (isLlmWiki ? "-search" : "-switch"),
                "-n" => "-new",
                "-c" => isA2A ? "-client" : "-clear",
                "-t" => "-tools",
                "-i" => isLlmWiki ? "-index" : parts[i],
                _ => parts[i]
            };
        }
        return result;
    }

    /// <summary>
    /// 打开统一设置中心并定位到「供应商」页签（替代旧的 ProviderManageWindow）
    /// </summary>
    private void ShowProviderManager(string[] args)
    {
        OpenSettings(SettingsTabKind.Provider);
    }

    /// <summary>
    /// 打开工作区设置中心并定位到「技能」页签（替代旧的 SkillManageWindow）
    /// </summary>
    private void ShowSkillManager(string[] args)
    {
        OpenSettings(SettingsTabKind.Skill);
    }

    /// <summary>
    /// 打开工作区设置中心并定位到「规则」页签（替代旧的 RuleManageWindow）
    /// </summary>
    private void ShowRuleManager(string[] args)
    {
        OpenSettings(SettingsTabKind.Rule);
    }

    /// <summary>
    /// 打开工作区设置中心并定位到「MCP」页签（替代旧的 MCPManageWindow）
    /// </summary>
    private void ShowMcpManager(string[] args)
    {
        OpenSettings(SettingsTabKind.Mcp);
    }

    /// <summary>
    /// 打开工作区设置中心并预选当前工作区（非 LLM Wiki）与指定页签。
    /// 命令面板路径无 Window owner，故用非模态 Show（侧边栏「⚙ 设置」按钮走 MainWindow 的模态 ShowDialog）。
    /// </summary>
    private void OpenSettings(SettingsTabKind kind)
    {
        var workspaceManager = Services.GetService<IWorkspaceManager>();
        var current = workspaceManager?.CurrentWorkspace;
        var preselect = (current != null && current.Type != WorkspaceTypes.LlmWiki) ? current : null;

        var window = new SettingsWindow(Services, preselect);
        window.PreselectTab(kind);
        window.Show();
    }

    /// <summary>
    /// 显示工作区管理窗口
    /// </summary>
    private void ShowWorkManager(string[] args)
    {
        var window = new WorkManageWindow(Services);
        window.Show();
    }

    /// <summary>
    /// 显示 LLM Wiki 知识库管理窗口
    /// </summary>
    private void ShowLlmWikiManager(string[] args)
    {
        var window = new LlmWikiManageWindow(Services);
        window.Show();
    }

    /// <summary>
    /// 执行 /a2a 命令（A2A 协作管理：状态/服务端/客户端/启停）。
    /// </summary>
    private async Task ExecuteA2ACommandAsync(string[] args)
    {
        var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "-list";

        switch (sub)
        {
            case "-list":
            case "-l":
                ShowA2AStatus();
                break;

            case "-server":
            case "-s":
                ShowA2AServer();
                break;

            case "-client":
            case "-c":
                HandleA2AClient(args);
                break;

            case "-start":
                await StartA2AAsync();
                break;

            case "-stop":
                await StopA2AAsync();
                break;

            default:
                Messages.Add(new SystemMessageItem
                {
                    Content = $"未知子命令: {sub}\n用法: /a2a [-list | -server | -client | -start | -stop]",
                    IsError = true
                });
                break;
        }
    }

    /// <summary>当前工作区根路径。</summary>
    private string? A2AWorkspaceRoot
        => Services.GetService<IWorkspaceManager>()?.CurrentWorkspace?.RootPath;

    /// <summary>追加系统消息。</summary>
    private void AddA2AMessage(string content, bool isError = false)
        => Messages.Add(new SystemMessageItem { Content = content, IsError = isError });

    /// <summary>显示 A2A 状态与服务端/客户端概览。</summary>
    private void ShowA2AStatus()
    {
        var enabled = Services.GetService<IOptions<A2AOptions>>()?.Value.Enabled ?? false;
        var lifecycle = Services.GetService<IA2ALifecycle>();
        var state = Services.GetService<A2ARuntimeState>();
        var serverHost = Services.GetService<IA2AServerHost>();
        var root = A2AWorkspaceRoot;

        var sb = new StringBuilder();
        sb.AppendLine("A2A 协作状态");
        sb.AppendLine($"  总开关(Enabled): {(enabled ? "已启用" : "已禁用")}");
        sb.AppendLine($"  运行状态: {(lifecycle?.IsRunning == true ? "运行中" : "未启动")}");
        sb.AppendLine($"  本地 Agent 名: {state?.LocalAgentName ?? "luban-agent"}");
        sb.AppendLine($"  服务端端点: {(string.IsNullOrWhiteSpace(serverHost?.Endpoint) ? "-" : serverHost!.Endpoint)}");
        sb.AppendLine($"  工作区: {(string.IsNullOrWhiteSpace(root) ? "(未选择)" : root)}");

        if (string.IsNullOrWhiteSpace(root))
        {
            AddA2AMessage(sb.ToString().TrimEnd());
            return;
        }

        if (!enabled)
        {
            sb.AppendLine("提示: A2A 未启用，需在 appsettings.json 的 LuBanAgent:Agent2Agent:Enabled 设为 true");
        }

        sb.AppendLine();
        sb.AppendLine("服务端 (server.json)");
        var server = A2AWorkspaceConfigStore.LoadServer(root);
        if (server is null)
        {
            sb.AppendLine("  未配置（可执行 /a2a -server 创建）");
        }
        else
        {
            sb.AppendLine($"  Host: {server.Host ?? "-"}  Port: {server.Port?.ToString() ?? "-"}  Path: {server.Path ?? "-"}");
            sb.AppendLine($"  RequireApiKey: {server.RequireApiKey?.ToString() ?? "-"}  AgentName: {server.AgentName ?? "-"}");
            sb.AppendLine($"  ApiKeys: {(server.ApiKeys is { Count: > 0 } ? $"已配置 {server.ApiKeys.Count} 个" : "未配置")}");
        }

        sb.AppendLine();
        var clients = A2AWorkspaceConfigStore.LoadClients(root);
        sb.AppendLine($"客户端 ({clients.Count})");
        if (clients.Count == 0)
        {
            sb.AppendLine("  未配置（可执行 /a2a -client -add <name> <endpoint> [apiKey] 新增）");
        }
        else
        {
            foreach (var (file, cfg) in clients)
            {
                var name = string.IsNullOrWhiteSpace(cfg.Name) ? Path.GetFileNameWithoutExtension(file) : cfg.Name;
                var keyHint = string.IsNullOrEmpty(cfg.ApiKey) ? "" : " (apiKey=***)";
                var subAgentHint = cfg.RegisterAsSubAgent == true ? " [subagent]" : "";
                sb.AppendLine($"  - {name}: {cfg.Endpoint ?? "-"}{keyHint}{subAgentHint}");
            }
        }

        AddA2AMessage(sb.ToString().TrimEnd());
    }

    /// <summary>查看或首次创建服务端配置。</summary>
    private void ShowA2AServer()
    {
        var root = A2AWorkspaceRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            AddA2AMessage("当前未选择工作区", isError: true);
            return;
        }

        if (A2AWorkspaceConfigStore.ServerExists(root))
        {
            var server = A2AWorkspaceConfigStore.LoadServer(root);
            if (server is null)
            {
                AddA2AMessage("server.json 无法解析，请检查文件内容", isError: true);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("服务端配置 (server.json)");
            sb.AppendLine($"  Host: {server.Host ?? "-"}");
            sb.AppendLine($"  Port: {server.Port?.ToString() ?? "-"}");
            sb.AppendLine($"  Path: {server.Path ?? "-"}");
            sb.AppendLine($"  RequireApiKey: {server.RequireApiKey?.ToString() ?? "-"}");
            sb.AppendLine($"  AgentName: {server.AgentName ?? "-"}");
            sb.AppendLine($"  ApiKeys: {(server.ApiKeys is { Count: > 0 } ? $"已配置 {server.ApiKeys.Count} 个" : "未配置")}");
            AddA2AMessage(sb.ToString().TrimEnd());
            return;
        }

        var apiKey = Guid.NewGuid().ToString("N");
        A2AWorkspaceConfigStore.SaveServer(root, new A2AWorkspaceFile
        {
            Host = "127.0.0.1",
            Port = 0,
            Path = "/a2a",
            RequireApiKey = true,
            ApiKeys = new List<string> { apiKey },
            AgentName = "luban-agent",
            AgentVersion = "1.0.0",
        });

        AddA2AMessage(
            $"已创建服务端配置: {Path.Combine(A2AWorkspaceConfigStore.ResolveDir(root), A2AWorkspaceConfigStore.ServerFileName)}\n" +
            $"  API Key: {apiKey}\n" +
            "API Key 为明文，请勿提交到版本库；建议将 .luban-agent/a2a/ 加入 .gitignore\n" +
            "执行 /a2a -stop 再 /a2a -start（或重启）后生效");
    }

    /// <summary>处理客户端子命令。</summary>
    private void HandleA2AClient(string[] args)
    {
        var root = A2AWorkspaceRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            AddA2AMessage("当前未选择工作区", isError: true);
            return;
        }

        if (args.Length < 2)
        {
            OpenSettings(SettingsTabKind.A2A);
            return;
        }

        var action = args[1].ToLowerInvariant();
        switch (action)
        {
            case "-add":
            case "-a":
                AddA2AClient(root, args);
                break;
            case "-delete":
            case "-del":
            case "-d":
                DeleteA2AClient(root, args);
                break;
            case "-list":
            case "-l":
                ListA2AClients(root);
                break;
            default:
                OpenSettings(SettingsTabKind.A2A);
                break;
        }
    }

    /// <summary>列出客户端配置。</summary>
    private void ListA2AClients(string root)
    {
        var clients = A2AWorkspaceConfigStore.LoadClients(root);
        var sb = new StringBuilder();
        sb.AppendLine($"客户端 ({clients.Count})");
        if (clients.Count == 0)
        {
            sb.AppendLine("  未配置（可执行 /a2a -client -add <name> <endpoint> [apiKey] 新增）");
        }
        else
        {
            foreach (var (file, cfg) in clients)
            {
                var name = string.IsNullOrWhiteSpace(cfg.Name) ? Path.GetFileNameWithoutExtension(file) : cfg.Name;
                var keyHint = string.IsNullOrEmpty(cfg.ApiKey) ? "" : " (apiKey=***)";
                var subAgentHint = cfg.RegisterAsSubAgent == true ? " [subagent]" : "";
                sb.AppendLine($"  - {name}: {cfg.Endpoint ?? "-"}{keyHint}{subAgentHint}");
            }
        }
        AddA2AMessage(sb.ToString().TrimEnd());
    }

    /// <summary>新增客户端配置；缺参时打开 A2A 设置页。</summary>
    private void AddA2AClient(string root, string[] args)
    {
        if (args.Length < 4)
        {
            OpenSettings(SettingsTabKind.A2A);
            return;
        }

        var name = args[2];
        var endpoint = args[3];
        var apiKey = args.Length >= 5 ? args[4] : "";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(endpoint))
        {
            AddA2AMessage("名称与端点不能为空", isError: true);
            return;
        }

        var fileName = A2AWorkspaceConfigStore.BuildClientFileName(name);
        var filePath = Path.Combine(A2AWorkspaceConfigStore.ResolveDir(root), fileName);
        if (File.Exists(filePath))
        {
            AddA2AMessage($"客户端 '{name}' 已存在，请先在设置页或删除后重试", isError: true);
            return;
        }

        A2AWorkspaceConfigStore.SaveClient(root, fileName, new A2AWorkspaceFile
        {
            Name = name,
            Endpoint = endpoint,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            RegisterAsSubAgent = false,
        });
        AddA2AMessage($"已保存客户端配置: {fileName}\n执行 /a2a -stop 再 /a2a -start（或重启）后生效");
    }

    /// <summary>删除客户端配置；缺参时打开 A2A 设置页。</summary>
    private void DeleteA2AClient(string root, string[] args)
    {
        if (args.Length < 3)
        {
            OpenSettings(SettingsTabKind.A2A);
            return;
        }

        var keyword = args[2];
        var clients = A2AWorkspaceConfigStore.LoadClients(root);
        var match = clients.FirstOrDefault(c =>
            string.Equals(c.Config.Name, keyword, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.FileName, keyword, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.FileName, A2AWorkspaceConfigStore.BuildClientFileName(keyword), StringComparison.OrdinalIgnoreCase));
        if (match.FileName is null)
        {
            AddA2AMessage($"找不到客户端: {keyword}", isError: true);
            return;
        }

        A2AWorkspaceConfigStore.DeleteFile(root, match.FileName);
        AddA2AMessage($"已删除客户端配置: {match.FileName}");
    }

    /// <summary>启动 A2A 运行时。</summary>
    private async Task StartA2AAsync()
    {
        var root = A2AWorkspaceRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            AddA2AMessage("当前未选择工作区", isError: true);
            return;
        }

        var lifecycle = Services.GetService<IA2ALifecycle>();
        if (lifecycle is null)
        {
            AddA2AMessage("A2A 未注册", isError: true);
            return;
        }

        try
        {
            await lifecycle.StartAsync(root);
            AddA2AMessage(lifecycle.IsRunning
                ? "A2A 已启动"
                : "A2A 启动完成（未启用或未配置角色，当前未运行）");
        }
        catch (Exception ex)
        {
            AddA2AMessage($"启动 A2A 失败: {ex.Message}", isError: true);
        }
    }

    /// <summary>停止 A2A 运行时。</summary>
    private async Task StopA2AAsync()
    {
        var lifecycle = Services.GetService<IA2ALifecycle>();
        if (lifecycle is null)
        {
            AddA2AMessage("A2A 未注册", isError: true);
            return;
        }

        try
        {
            await lifecycle.StopAsync();
            AddA2AMessage("A2A 已停止");
        }
        catch (Exception ex)
        {
            AddA2AMessage($"停止 A2A 失败: {ex.Message}", isError: true);
        }
    }

    private void ShowHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("可用命令:");

        // 按 Category 分组遍历 catalog，自动同步新增命令
        var codexCmds = CommandCatalog.All.Where(c => c.Supports(SlashCommandClient.Codex)).ToList();
        var groups = codexCmds.GroupBy(c => c.Category).OrderBy(g => g.Key);
        foreach (var grp in groups)
        {
            sb.AppendLine($"  ── {grp.Key} ──");
            foreach (var cmd in grp)
            {
                var aliases = cmd.Aliases.Count > 0
                    ? ", " + string.Join(", ", cmd.Aliases)
                    : "";
                sb.AppendLine($"  {cmd.Name}{aliases}    {cmd.Description}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("快捷键:");
        sb.AppendLine("  Enter               发送消息");
        sb.AppendLine("  Ctrl+Enter          换行");
        sb.AppendLine("  Shift+Tab           切换权限模式");
        sb.AppendLine("  Esc                 取消当前任务");

        Messages.Add(new SystemMessageItem { Content = sb.ToString() });
    }

    private string GetPermissionModeDisplay(ToolPermissionMode mode) => mode switch
    {
        ToolPermissionMode.Default => "默认 (Default)",
        ToolPermissionMode.Plan => "计划 (Plan)",
        ToolPermissionMode.AcceptEdits => "接受编辑 (AcceptEdits)",
        ToolPermissionMode.BypassPermissions => "跳过权限 (Bypass)",
        _ => mode.ToString()
    };

    private async Task ExecuteModelCommandAsync(string[] args)
    {
        var configManager = Services.GetService<ConfigManager>();
        if (configManager == null)
        {
            Messages.Add(new SystemMessageItem { Content = "配置管理器未初始化", IsError = true });
            return;
        }

        if (args.Length == 0 || args[0] == "-list" || args[0] == "list")
        {
            // 列出所有模型
            var sb = new StringBuilder();
            sb.AppendLine("可用模型:");

            foreach (var provider in configManager.Providers)
            {
                var models = configManager.GetAllModels(provider.Name);
                foreach (var model in models)
                {
                    var fullName = $"{provider.Name}:{model}";
                    var selected = fullName == configManager.SelectedModel ? " (当前)" : "";
                    sb.AppendLine($"  {fullName}{selected}");
                }
            }

            if (!string.IsNullOrEmpty(configManager.SelectedModel))
            {
                sb.AppendLine($"\n当前选择: {configManager.SelectedModel}");
            }

            Messages.Add(new SystemMessageItem { Content = sb.ToString() });
        }
        else if (args[0] == "-switch" || args[0] == "switch")
        {
            if (args.Length < 2)
            {
                Messages.Add(new SystemMessageItem
                {
                    Content = "用法: /model -switch <provider:model>",
                    IsError = true
                });
                return;
            }

            var modelId = string.Join(' ', args.Skip(1));
            try
            {
                configManager.SetSelectedModel(modelId);
                Messages.Add(new SystemMessageItem { Content = $"已切换模型: {modelId}" });
            }
            catch (Exception ex)
            {
                Messages.Add(new SystemMessageItem
                {
                    Content = $"切换模型失败: {ex.Message}",
                    IsError = true
                });
            }
        }
        else
        {
            Messages.Add(new SystemMessageItem
            {
                Content = "用法: /model [-list] [-switch <provider:model>]"
            });
        }
    }

    private async Task ExecuteSessionCommandAsync(string[] args)
    {
        var sessionManager = Services.GetService<ISessionManager>();
        if (sessionManager == null)
        {
            Messages.Add(new SystemMessageItem { Content = "会话管理器未初始化", IsError = true });
            return;
        }

        if (args.Length == 0 || args[0] == "-list" || args[0] == "list")
        {
            // 列出所有会话
            var sessions = await sessionManager.GetUserSessionsAsync("");
            var sb = new StringBuilder();
            sb.AppendLine("会话列表:");

            int index = 1;
            foreach (var session in sessions)
            {
                var current = session.SessionId == sessionManager.CurrentSession?.SessionId ? " (当前)" : "";
                sb.AppendLine($"  {index}. {session.Title}{current}");
                index++;
            }

            if (!sessions.Any())
            {
                sb.AppendLine("  (无会话)");
            }

            Messages.Add(new SystemMessageItem { Content = sb.ToString() });
        }
        else if (args[0] == "-new" || args[0] == "new")
        {
            var title = args.Length > 1 ? string.Join(' ', args.Skip(1)) : "新会话";
            var session = await sessionManager.CreateSessionAsync(null, title);
            await sessionManager.SetCurrentSessionAsync(session.SessionId);
            Messages.Add(new SystemMessageItem { Content = $"已创建并切换到会话: {title}" });
        }
        else if (args[0] == "-clear" || args[0] == "clear")
        {
            sessionManager.ClearCurrentSession();
            ClearMessages();
            Messages.Add(new SystemMessageItem { Content = "会话已清空" });
        }
        else
        {
            Messages.Add(new SystemMessageItem
            {
                Content = "用法: /session [-list] [-new <标题>] [-clear]"
            });
        }
    }

    private void ShowStats()
    {
        var sessionManager = Services.GetService<ISessionManager>();
        var sb = new StringBuilder();
        sb.AppendLine("统计信息:");
        sb.AppendLine($"  消息数量: {Messages.Count}");
        sb.AppendLine($"  权限模式: {GetPermissionModeDisplay(PermissionMode)}");

        if (sessionManager?.CurrentSession != null)
        {
            sb.AppendLine($"  当前会话: {sessionManager.CurrentSession.SessionId}");
        }

        Messages.Add(new SystemMessageItem { Content = sb.ToString() });
    }

    /// <summary>
    /// 加载会话历史：切换当前会话并展示其消息流。
    /// 已存在的会话 ViewModel 保留其内存消息（避免清空后台运行中的会话），仅在首次创建时从存储回填。
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public async Task LoadSessionHistoryAsync(string sessionId)
    {
        IsSwitchingSession = true;
        try
        {
            var sessionManager = _agentHost.Services.GetRequiredService<LuBan.AIAgent.Sessions.ISessionManager>();
            await sessionManager.SetCurrentSessionAsync(sessionId);

            // 切换会话：丢弃上一会话遗留的待发附件
            ClearPendingAttachments();

            var isNew = !_sessions.ContainsKey(sessionId);
            var session = GetOrCreateSession(sessionId);
            SetCurrentSession(session);

            if (!isNew)
                return;

            var messages = await sessionManager.GetLatestMessagesAsync(sessionId, 50);
            foreach (var msg in messages.OrderBy(m => m.CreatedAt))
            {
                if (msg.Role == "user")
                {
                    session.Messages.Add(new UserMessageItem
                    {
                        Content = msg.Content,
                        Attachments = AttachmentService.DisplayInfosFromJson(msg.Attachments)
                    });
                }
                else if (msg.Role == "assistant")
                {
                    session.Messages.Add(new AssistantMessageItem
                    {
                        Content = msg.Content,
                        Thinking = msg.Thinking ?? "",
                        IsComplete = true
                    });
                }
            }
        }
        finally
        {
            IsSwitchingSession = false;
        }
    }

    /// <summary>
    /// 清空当前会话消息流
    /// </summary>
    public void ClearMessages()
    {
        _currentSession?.Clear();
        ClearPendingAttachments();
    }
}

/// <summary>
/// 会话运行状态变化事件参数。
/// </summary>
public sealed class SessionRunningChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建事件参数。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="isRunning">是否正在运行。</param>
    public SessionRunningChangedEventArgs(string sessionId, bool isRunning)
    {
        SessionId = sessionId;
        IsRunning = isRunning;
    }

    /// <summary>
    /// 会话标识。
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// 是否正在运行。
    /// </summary>
    public bool IsRunning { get; }
}
