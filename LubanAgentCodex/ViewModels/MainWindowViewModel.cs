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
using LubanAgentCore.Services;
using LubanAgentCore.Utils;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Errors;
using Microsoft.Extensions.Options;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCodex.Views;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.ViewModels;

/// <summary>
/// 主窗口 ViewModel
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly AgentHostService _agentHost;
    private CancellationTokenSource? _cts;
    private int _activeRunSeq; // 运行序号：每次发起对话自增，用于丢弃取消/新对话后迟到的旧事件

    // 本轮是否因用户拒绝工具确认而终止：用于取消收尾时区分「已拒绝，本轮终止」与「已取消」
    private volatile bool _deniedByUser;
    private readonly StringBuilder _pendingText = new();
    private readonly StringBuilder _pendingThinking = new();
    private FlushThrottle? _throttle;
    private AssistantMessageItem? _currentAssistant;
    private ThinkingMessageItem? _currentThinking;
    private readonly List<OrchestrationNodeItem> _runningNodes = new();
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
    /// 输入文本
    /// </summary>
    [ObservableProperty]
    private string _inputText = "";

    /// <summary>
    /// 是否正在运行
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// 是否正在切换会话（用于显示中央加载提示）
    /// </summary>
    [ObservableProperty]
    private bool _isSwitchingSession;

    /// <summary>
    /// 权限模式
    /// </summary>
    [ObservableProperty]
    private ToolPermissionMode _permissionMode = ToolPermissionMode.Default;

    /// <summary>
    /// 消息集合
    /// </summary>
    public ObservableCollection<MessageItemBase> Messages { get; } = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="services">服务提供者</param>
    public MainWindowViewModel(IServiceProvider services)
    {
        _agentHost = new AgentHostService(services);

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
        if (IsRunning || (string.IsNullOrWhiteSpace(InputText) && _pendingAttachmentPaths.Count == 0))
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

        IsRunning = true;
        InputText = "";

        // 添加用户消息
        Messages.Add(new UserMessageItem { Content = input, Attachments = displayInfos });

        // 注意：不预创建 AssistantMessageItem，正文项在首个 TextDeltaEvent 到达时懒创建，
        // 确保它排在思考内容/工具卡片之后（修复显示顺序错乱）

        // 新一轮开始：清除上一轮的拒绝终止标志
        _deniedByUser = false;

        _cts = new CancellationTokenSource();
        var runSeq = Interlocked.Increment(ref _activeRunSeq); // 本次对话运行序号；取消/新对话将使其失效

        // 新回合开始：清空上一回合遗留的运行中节点（节点卡片保留在消息流中作为历史）
        _runningNodes.Clear();

        // 初始化节流器：回调投递到 UI 线程执行，与事件处理天然串行
        _throttle ??= new FlushThrottle(
            () => Dispatcher.UIThread.Post(FlushPending),
            TimeSpan.FromMilliseconds(50));

        try
        {
            // 关键：消费循环放后台线程，避免确认回调阻塞 UI 线程导致死锁；
            // 事件本身全部投递到 UI 线程按序处理（Dispatcher 队列 FIFO 保序）
            await Task.Run(() => ConsumeStreamAsync(input, sendingPaths, _cts.Token, runSeq));
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new SystemMessageItem { Content = _deniedByUser ? "已拒绝，本轮终止" : "已取消" });
        }
        catch (AgentApiException apiEx)
        {
            // 框架已完成错误分类：只渲染友好文案，原始异常进日志
            Messages.Add(new SystemMessageItem
            {
                Content = apiEx.Error.FriendlyMessage,
                IsError = true
            });
        }
        catch (Exception ex)
        {
            Messages.Add(new SystemMessageItem
            {
                Content = $"错误: {ex.Message}",
                IsError = true
            });
        }
        finally
        {
            // 收尾同样投递到 UI 线程，排在所有已投递事件之后执行
            Dispatcher.UIThread.Post(() =>
            {
                FlushPending();
                CompleteCurrentItems();
                // 回合收尾：把仍标记为运行中的节点落定为结束态，避免卡片一直显示"运行中"
                foreach (var node in _runningNodes)
                {
                    node.IsComplete = true;
                }
                _runningNodes.Clear();
                IsRunning = false;
            });
            var cts = _cts;
            _cts = null;
            cts?.Dispose();
        }
    }

    /// <summary>
    /// 初始化工作区 AGENTS.md：把生成提示词投入聊天流，落盘后追加校验提示。
    /// 复用与 <see cref="SendAsync"/> 相同的运行态装配（IsRunning / 取消 / 节流）。
    /// </summary>
    /// <param name="options">表单收集的生成选项</param>
    public async Task RunInitAgentsMdAsync(AgentsMdOptions options)
    {
        if (IsRunning) return;

        var initializer = Services.GetRequiredService<IAgentsMdInitializer>();
        var workspaceManager = Services.GetRequiredService<IWorkspaceManager>();
        var workspace = workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            Messages.Add(new SystemMessageItem { Content = "未设置当前工作区", IsError = true });
            return;
        }

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

        IsRunning = true;
        _cts = new CancellationTokenSource();
        var runSeq = Interlocked.Increment(ref _activeRunSeq);
        _runningNodes.Clear();
        _throttle ??= new FlushThrottle(
            () => Dispatcher.UIThread.Post(FlushPending),
            TimeSpan.FromMilliseconds(50));

        try
        {
            var text = await Task.Run(() => ConsumeStreamAsync(prompt, Array.Empty<string>(), _cts.Token, runSeq));

            var markdown = initializer.Normalize(text);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                Messages.Add(new SystemMessageItem { Content = "生成结果为空，AGENTS.md 未写入", IsError = true });
                return;
            }

            var targetPath = initializer.GetTargetPath(workspace);
            await initializer.WriteAsync(workspace, markdown, _cts.Token);

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
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                FlushPending();
                CompleteCurrentItems();
                foreach (var node in _runningNodes)
                {
                    node.IsComplete = true;
                }
                _runningNodes.Clear();
                IsRunning = false;
            });
            var cts = _cts;
            _cts = null;
            cts?.Dispose();
        }
    }

    /// <summary>
    /// 取消命令
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (_cts == null)
            return;
        // 失效当前运行序号：丢弃取消后迟到的旧事件（避免下次输入仍执行上次对话）
        Interlocked.Increment(ref _activeRunSeq);
        _cts.Cancel();
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
        var args = ExpandSubCommandAliases(parts.Skip(1).ToArray());

        switch (cmd)
        {
            case "/help":
                ShowHelp();
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

            case "/rag":
            case "/rg":
                ShowRagManager(args);
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
    /// 子命令简写展开
    /// </summary>
    private static string[] ExpandSubCommandAliases(string[] parts)
    {
        var result = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            result[i] = parts[i] switch
            {
                "-l" => "-list",
                "-a" => "-add",
                "-u" => "-update",
                "-d" => "-delete",
                "-s" => "-switch",
                "-n" => "-new",
                "-c" => "-clear",
                "-t" => "-tools",
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
    /// 打开工作区设置中心并预选当前工作区（非 RAG）与指定页签。
    /// 命令面板路径无 Window owner，故用非模态 Show（侧边栏「⚙ 设置」按钮走 MainWindow 的模态 ShowDialog）。
    /// </summary>
    private void OpenSettings(SettingsTabKind kind)
    {
        var workspaceManager = Services.GetService<IWorkspaceManager>();
        var current = workspaceManager?.CurrentWorkspace;
        var preselect = (current != null && current.Type != "Rag") ? current : null;

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
    /// 显示 RAG 知识库管理窗口
    /// </summary>
    private void ShowRagManager(string[] args)
    {
        var window = new RagManageWindow(Services);
        window.Show();
    }

    private void ShowHelp()
    {
        var helpText = @"可用命令:
  /help               显示此帮助
  /clear              清空会话历史
  /mode [name]        查看或切换权限模式
  /model, /m          管理模型
  /provider, /p       管理 AI Provider
  /skill, /sk         管理技能
  /rule, /r           管理规则
  /mcp, /mp           管理 MCP 服务
  /session, /se       管理对话会话
  /stats, /st         显示统计信息
  /work, /w           管理工作区
  /rag, /rg           管理 RAG 知识库

快捷键:
  Enter               发送消息
  Ctrl+Enter          换行
  Shift+Tab           切换权限模式
  Esc                 取消当前任务";

        Messages.Add(new SystemMessageItem { Content = helpText });
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
    /// 后台消费流式事件：只负责转发，全部事件按到达顺序投递到 UI 线程
    /// </summary>
    /// <returns>本回合累积的正文文本（供 AGENTS.md 等需要拿到完整输出的场景复用）</returns>
    private async Task<string> ConsumeStreamAsync(
        string input, IReadOnlyList<string> attachmentPaths, CancellationToken ct, int runSeq)
    {
        var plannedCount = 0;
        var sb = new StringBuilder();

        await foreach (var evt in _agentHost.RunStreamingAsync(
            input,
            attachmentPaths,
            ConfirmCallback,
            PermissionMode,
            onPlannedAction: (tool, args) =>
            {
                if (runSeq != _activeRunSeq) return; // 运行序号失效（取消/新对话）则丢弃
                Interlocked.Increment(ref plannedCount);
                var content = $"📋 计划项（Plan 模式，未执行）: {tool} {ToolArgsFormatter.Summarize(args)}";
                Dispatcher.UIThread.Post(() => Messages.Add(new SystemMessageItem { Content = content }));
            },
            onUserDenied: (tool, args) =>
            {
                // 用户明确拒绝：标记并立即终止本轮（框架保证 token 未取消，即非 Esc）
                if (runSeq != _activeRunSeq) return;
                _deniedByUser = true;
                _cts?.Cancel();
            },
            ct: ct))
        {
            var e = evt;
            if (e is TextDeltaEvent delta)
                sb.Append(delta.Delta);
            Dispatcher.UIThread.Post(() => HandleStreamEvent(e, runSeq));
        }

        if (runSeq == _activeRunSeq)
            ReportPlannedActions(Volatile.Read(ref plannedCount));

        return sb.ToString();
    }

    /// <summary>
    /// Plan 模式回合结束汇总：本轮计划项均未执行，提示切换模式后重新发起。
    /// </summary>
    /// <param name="plannedCount">本轮收集到的计划项数量。</param>
    private void ReportPlannedActions(int plannedCount)
    {
        if (PermissionMode != ToolPermissionMode.Plan || plannedCount <= 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => Messages.Add(new SystemMessageItem
        {
            Content = $"📋 Plan 模式：本轮记录 {plannedCount} 个计划项，均未执行。"
                + "切换到 default / accept-edits 后重新发送即可执行。"
        }));
    }

    /// <summary>
    /// UI 线程上串行处理流式事件，保证消息流显示顺序：
    /// 思考 → 工具调用 → 工具结果 → ... → 最终正文
    /// </summary>
    private void HandleStreamEvent(StreamEvent evt, int runSeq)
    {
        // 运行序号防护：取消或新对话发起后，迟到的旧事件一律丢弃，
        // 避免"按 Esc 取消后，下次输入仍继续执行上次对话"。
        if (runSeq != _activeRunSeq)
            return;

        switch (evt)
        {
            case TextDeltaEvent t:
                // 正文项懒创建：首个文本到达时才插入，确保排在思考/工具之后
                if (_currentAssistant == null || _currentAssistant.IsComplete)
                {
                    FlushPending();
                    _currentAssistant = new AssistantMessageItem();
                    Messages.Add(_currentAssistant);
                }
                _pendingText.Append(t.Delta);
                _throttle?.Schedule();
                break;

            case ThinkingDeltaEvent t:
                // 思考内容作为独立消息项显示。
                // 若已输出过正文、当前又产生新的思考，则收尾旧思考块并另起一块，
                // 使"思考→输出→再思考"按发生顺序交错显示，而非堆积在顶部单块。
                if (_currentThinking == null || _currentThinking.IsComplete || _currentAssistant != null)
                {
                    FlushPending();
                    if (_currentThinking != null)
                        _currentThinking.IsComplete = true;
                    _currentThinking = new ThinkingMessageItem();
                    Messages.Add(_currentThinking);
                }
                _pendingThinking.Append(t.Delta);
                _throttle?.Schedule();
                break;

            case ToolCallStartedEvent tc:
                FlushPending();
                CompleteCurrentItems();
                Messages.Add(new ToolCallItem
                {
                    ToolName = tc.Name,
                    CallId = tc.CallId,
                    Arguments = tc.Arguments,
                    State = ToolCallState.Running
                });
                break;

            case ToolCallCompletedEvent tcc:
                UpdateToolCallState(tcc.CallId, ToolCallState.Done, null);
                break;

            case ToolCallFailedEvent tcf:
                UpdateToolCallState(tcf.CallId, ToolCallState.Failed, tcf.Error);
                break;

            case ErrorEvent e:
                Messages.Add(new SystemMessageItem { Content = e.Message, IsError = true });
                break;

            case OrchestrationProgressEvent p:
                HandleOrchestrationProgress(p);
                break;
        }
    }

    /// <summary>
    /// 处理编排进度事件：节点级事件渲染为可点击的节点卡片（点击查看子代理详情），
    /// 其余事件追加一行系统提示，让规划/反思等长耗时阶段有实时反馈。
    /// （渲染口径与 CLI 的 HandleOrchestrationProgress 对齐）
    /// </summary>
    /// <param name="progress">编排进度事件。</param>
    private void HandleOrchestrationProgress(OrchestrationProgressEvent progress)
    {
        // 节点开始：插入节点卡片，后续活动/完成事件按 NodeId 回填
        if (progress.EventType == ProgressEventType.NodeStarted)
        {
            var nodeBlock = new OrchestrationNodeItem
            {
                NodeId = progress.NodeId ?? "",
                Description = progress.Message ?? progress.NodeId ?? "子任务"
            };
            _runningNodes.Add(nodeBlock);
            Messages.Add(nodeBlock);
            return;
        }

        // 节点活动：把子代理想法/工具调用/结果追加到对应节点卡片
        if (progress.EventType == ProgressEventType.NodeActivity)
        {
            if (progress.Activity is null)
                return;
            FindNode(progress.NodeId)?.AppendActivity(progress.Activity);
            return;
        }

        // 节点结束：收尾节点卡片（命中则不再追加系统提示行）
        if (progress.EventType is ProgressEventType.NodeCompleted or ProgressEventType.NodeFailed or ProgressEventType.NodeSkipped)
        {
            var nodeBlock = FindNode(progress.NodeId);
            if (nodeBlock != null)
            {
                nodeBlock.ElapsedSeconds = progress.ElapsedSeconds.GetValueOrDefault();
                if (progress.EventType == ProgressEventType.NodeFailed)
                {
                    nodeBlock.IsFailed = true;
                    nodeBlock.Error = progress.Error;
                }
                else if (progress.EventType == ProgressEventType.NodeSkipped)
                {
                    nodeBlock.IsSkipped = true;
                }
                else
                {
                    nodeBlock.IsComplete = true;
                }
                _runningNodes.Remove(nodeBlock);
                return;
            }
        }

        var text = progress.EventType switch
        {
            ProgressEventType.PlanningStarted => $"⏳ {progress.Message ?? "正在规划任务…"}",
            ProgressEventType.PlanningCompleted => $"▸ {progress.Message ?? "任务图谱已生成"}",
            ProgressEventType.NodeStarted => $"▶ 开始执行: {progress.Message ?? progress.NodeId}",
            ProgressEventType.NodeCompleted => $"✓ {progress.Message ?? progress.NodeId} · {progress.ElapsedSeconds.GetValueOrDefault():F1}s",
            ProgressEventType.NodeFailed => $"✗ {progress.Message ?? progress.NodeId} · {progress.Error ?? "失败"}",
            ProgressEventType.NodeSkipped => $"− {progress.Message ?? progress.NodeId} · 已跳过（前驱失败）",
            ProgressEventType.ReflectionStarted => $"↻ {progress.Message ?? "反思重规划"}",
            _ => null
        };

        if (text is null)
            return;

        Messages.Add(new SystemMessageItem
        {
            Content = text,
            IsError = progress.EventType == ProgressEventType.NodeFailed
        });
    }

    /// <summary>
    /// 按节点标识查找运行中的节点卡片；未提供 NodeId 时退化为最近一个未结束的节点
    /// </summary>
    /// <param name="nodeId">节点标识。</param>
    /// <returns>节点卡片，未命中返回 null。</returns>
    private OrchestrationNodeItem? FindNode(string? nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
            return _runningNodes.FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));

        return _runningNodes.LastOrDefault(n => !n.IsComplete && !n.IsFailed);
    }

    /// <summary>
    /// 结束当前思考/正文消息项（仅 UI 线程调用）
    /// </summary>
    private void CompleteCurrentItems()
    {
        if (_currentThinking != null)
        {
            _currentThinking.IsComplete = true;
            _currentThinking = null;
        }
        if (_currentAssistant != null)
        {
            _currentAssistant.IsComplete = true;
            _currentAssistant = null;
        }
    }

    /// <summary>
    /// 更新工具调用卡片状态（仅 UI 线程调用）
    /// </summary>
    private void UpdateToolCallState(string callId, ToolCallState state, string? error)
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i] is ToolCallItem tool && tool.CallId == callId)
            {
                // 必须先写错误信息再改状态：ToolCallCard 只订阅 State，
                // State 的 setter 会同步触发渲染，若此时 ErrorMessage 尚未赋值，
                // 卡片会永远显示兜底文案「工具执行失败」而丢掉真实原因。
                tool.ErrorMessage = error;
                tool.State = state;
                break;
            }
        }
    }

    /// <summary>
    /// 刷新待显示的增量（仅 UI 线程调用，直接同步追加，顺序确定）
    /// </summary>
    private void FlushPending()
    {
        if (_pendingText.Length > 0)
        {
            var text = _pendingText.ToString();
            _pendingText.Clear();
            _currentAssistant?.AppendDelta(text);
        }

        if (_pendingThinking.Length > 0)
        {
            var thinking = _pendingThinking.ToString();
            _pendingThinking.Clear();
            _currentThinking?.AppendDelta(thinking);
        }
    }

    /// <summary>
    /// 加载会话历史
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public async Task LoadSessionHistoryAsync(string sessionId)
    {
        if (IsRunning) return;

        IsSwitchingSession = true;
        try
        {
            var sessionManager = _agentHost.Services.GetRequiredService<LuBan.AIAgent.Sessions.ISessionManager>();
            await sessionManager.SetCurrentSessionAsync(sessionId);

            Messages.Clear();

            // 切换会话：丢弃上一会话遗留的待发附件
            ClearPendingAttachments();

            var messages = await sessionManager.GetLatestMessagesAsync(sessionId, 50);
            foreach (var msg in messages.OrderBy(m => m.CreatedAt))
            {
                if (msg.Role == "user")
                {
                    Messages.Add(new UserMessageItem
                    {
                        Content = msg.Content,
                        Attachments = AttachmentService.DisplayInfosFromJson(msg.Attachments)
                    });
                }
                else if (msg.Role == "assistant")
                {
                    Messages.Add(new AssistantMessageItem
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
    /// 清空消息流
    /// </summary>
    public void ClearMessages()
    {
        Messages.Clear();
        _runningNodes.Clear();
        ClearPendingAttachments();
    }

    private ConfirmResult ConfirmCallback(string toolName, IReadOnlyDictionary<string, object?> args)
    {
        using var done = new ManualResetEventSlim(false);
        var result = ConfirmResult.Deny;

        var ct = _cts?.Token ?? CancellationToken.None;
        using var ctr = ct.CanBeCanceled ? ct.Register(() => done.Set()) : default;

        Dispatcher.UIThread.Post(() =>
        {
            var confirmItem = new ToolConfirmItem
            {
                ToolName = toolName,
                Arguments = args,
                OnRespond = cr =>
                {
                    result = cr;
                    done.Set();
                }
            };
            Messages.Add(confirmItem);
        });

        // 无限期等待用户响应；Esc/终止时经 ct 注册提前唤醒（ct 可取消时抛 OCE，由 SendAsync 收尾）
        if (ct.CanBeCanceled)
            done.Wait(ct);
        else
            done.Wait();

        return result;
    }
}
