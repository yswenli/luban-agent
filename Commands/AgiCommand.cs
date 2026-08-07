/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： AgiCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：通用 Agent 对话命令，支持工具调用和 thinking 显示
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Skills;

namespace LubanAgent.Commands;

/// <summary>
/// 通用 Agent 对话命令，支持工具调用和 thinking 显示
/// </summary>
public class AgiCommand : CommandBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly SkillRegistry _skillRegistry;
    private readonly Func<string, Task<bool>>? _executeCommandAsync;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "agi";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "通用 Agent 对话";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    public AgiCommand(ConfigManager configManager, IConfiguration configuration, ISessionManager sessionManager, IServiceProvider serviceProvider, IWorkspaceManager workspaceManager, SkillRegistry skillRegistry, Func<string, Task<bool>>? executeCommandAsync = null)
        : base(configManager, configuration)
    {
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;
        _workspaceManager = workspaceManager;
        _skillRegistry = skillRegistry;
        _executeCommandAsync = executeCommandAsync;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public override async Task ExecuteAsync()
    {
        if (!ConfigManager.HasSelectedModel)
        {
            WriteError("请先使用 model switch 命令选择模型");
            return;
        }

        // 1. 获取当前工作区
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace == null)
        {
            WriteError("请先使用 /work -switch 切换到工作区");
            return;
        }

        // 2. 检查授权状态
        if (!workspace.IsAuthorized)
        {
            var authorized = await _workspaceManager.EnsureAuthorizedAsync(workspace);
            if (!authorized) return;
        }

        // 3. 创建或获取当前会话
        var currentSession = _sessionManager.CurrentSession;
        if (currentSession == null)
        {
            currentSession = await _sessionManager.CreateSessionAsync(userId: "default", title: "新对话");
            Console.WriteLine($"已创建新会话: {currentSession.SessionId}");
        }

        // 使用注入的 ServiceProvider，而不是创建新的
        // 这样可以确保所有服务（包括 IRetrievalService）都可用
        var serviceProvider = _serviceProvider;

        // 4. 按工作区类型选择 Profile
        AgentProfile profile = workspace.Type == "Rag"
            ? new RagAgentProfile(workspace)
            : new NormalAgentProfile();

        // 5. 确保工作区配置目录存在（配置由 AgentProfile 按需加载）
        await _workspaceManager.EnsureConfigDirectoryAsync(workspace);

        // 6. 加载文件级 Skill（项目级 + 用户级）
        var workspaceSkillsDir = workspace.ConfigPath != null
            ? Path.Combine(workspace.RootPath, workspace.ConfigPath, "skills")
            : null;
        if (workspaceSkillsDir != null)
        {
            _skillRegistry.LoadFromWorkspace(workspace.RootPath);
        }

        // 加载工作区编排配置：任务模板（.luban-agent/plans）与自定义角色（.luban-agent/roles）
        try
        {
            var templatePlanner = serviceProvider.GetService<LuBan.AIAgent.Orchestration.Planner.TemplateTaskPlanner>();
            var templatesLoaded = templatePlanner?.LoadFromWorkspace(workspace.RootPath) ?? 0;
            var roleRegistry = serviceProvider.GetService<LuBan.AIAgent.Orchestration.SubAgentRoleRegistry>();
            var rolesLoaded = roleRegistry?.LoadFromWorkspace(workspace.RootPath) ?? 0;
            if (templatesLoaded > 0 || rolesLoaded > 0)
                Console.WriteLine($"已加载工作区编排配置: {templatesLoaded} 个任务模板, {rolesLoaded} 个自定义角色");
        }
        catch (Exception ex)
        {
            Logger.Warn("加载工作区编排配置失败", ex);
        }

        Console.WriteLine();
        Console.WriteLine($"工作区: {workspace.Name} ({workspace.RootPath})");
        if (workspace.Type == "Rag")
        {
            Console.WriteLine("模式: 知识库问答（自动检索增强）");
        }
        else
        {
            Console.WriteLine("可用工具: 文件系统、脚本执行、浏览器、数据库、Redis、Web请求");
            Console.WriteLine("复合任务: AI 会自动拆解为 DAG 并调度 SubAgent 并行执行");
        }
        Console.WriteLine("提示: AI 会自动判断是否需要使用工具来回答你的问题");
        Console.WriteLine("      危险操作（写入、删除、执行脚本）需要用户确认");
        Console.WriteLine("      按 ESC 可暂停当前操作，输入 'c' 继续，输入 'q' 终止");
        Console.WriteLine("      输入 / 命令可执行操作，如 /session switch 1");
        Console.WriteLine("示例: 帮我查一下D盘下面有哪些目录");
        Console.WriteLine($"当前会话: {currentSession.Title ?? "未命名"}");

        // 显示会话历史摘要（最近 5 条消息）
        await DisplaySessionHistoryAsync(currentSession.SessionId);

        Console.WriteLine("开始对话 (输入 'exit' 返回主菜单)");
        Console.WriteLine();

        try
        {
            var confirmContext = serviceProvider.GetRequiredService<ToolConfirmationContext>();
            var confirmService = serviceProvider.GetRequiredService<IToolConfirmationService>();

            // 设置工具确认回调（加锁串行化，防止并发子代理同时读写控制台导致输出交错）
            confirmContext.Callback = (toolName, args) =>
            {
                lock (EscKeyListener.ConsoleReadLock)
                {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[yellow]⚠️  [bold]危险操作请求: {Markup.Escape(toolName)}[/][/]");
                AnsiConsole.MarkupLine("[yellow]参数:[/]");
                var formattedArgs = confirmService.FormatArguments(args, 500);
                foreach (var line in formattedArgs.Split('\n'))
                {
                    AnsiConsole.WriteLine(line);
                }
                AnsiConsole.WriteLine();

                AnsiConsole.Markup("[yellow]是否执行此操作？(y/N): [/]");
                string? input;
                EscKeyListener.BeginConsoleRead();
                try
                {
                    input = Console.ReadLine()?.Trim().ToLower();
                }
                finally
                {
                    EscKeyListener.EndConsoleRead();
                }
                var confirmed = input == "y" || input == "yes";

                if (confirmed)
                {
                    AnsiConsole.MarkupLine("[green]✓ 已确认执行[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]✗ 已取消执行[/]");
                }

                return confirmed;
                }
            };

            // 注册工作区路径检查回调，用于判断文件操作是否在工作区内
            confirmContext.WorkspacePathChecker = (path) =>
                WorkspaceManager.IsWithinWorkspace(path);

            var agentFactory = serviceProvider.GetRequiredService<ILuBanAgentFactory>();
            var ruleEngine = serviceProvider.GetRequiredService<RuleEngine>();
            var pluginRegistry = serviceProvider.GetRequiredService<ToolPluginRegistry>();
            var skillRegistry = serviceProvider.GetRequiredService<SkillRegistry>();
            var mcpRegistry = serviceProvider.GetRequiredService<MCPRegistry>();

            // 创建 Agent（Profile 内部加载工作区组件后调用工厂）
            var modelName = ConfigManager.SelectedModel ?? throw new InvalidOperationException("未选择模型（SelectedModel 为 null）");
            var agent = await profile.CreateAgentAsync(agentFactory, modelName, workspace, ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);

        Console.WriteLine();

            // 运行聊天循环（RAG 工作区带自动检索注入）
            await RunChatLoop(agent, profile, workspace, serviceProvider, agentFactory, ruleEngine, pluginRegistry, skillRegistry, mcpRegistry, modelName);
        }
        catch (Exception ex)
        {
            Logger.Error("AgiCommand 初始化失败", ex);
            WriteError(ex.Message);
        }
        finally
        {
            // 清理确认上下文
            serviceProvider.GetService<ToolConfirmationContext>()?.Reset();
        }
    }

    /// <summary>
    /// 运行对话循环，支持工具调用显示、ESC 取消和 / 命令
    /// </summary>
    private async Task RunChatLoop(LuBanAgent agent, AgentProfile profile, WorkspaceInfo workspace, IServiceProvider serviceProvider, ILuBanAgentFactory agentFactory, RuleEngine ruleEngine, ToolPluginRegistry pluginRegistry, SkillRegistry skillRegistry, MCPRegistry mcpRegistry, string modelName)
    {
        bool autoActivationAttempted = false;
        while (true)
        {
            // 显示当前激活的 Skill
            if (profile.ActiveSkill != null)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($"[{profile.ActiveSkill.Name}] ");
                Console.ResetColor();
            }
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("👶 ");
            Console.ResetColor();
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} 👶 {input}");

            // 检查模型是否切换
            var currentModel = ConfigManager.SelectedModel;
            if (currentModel != modelName)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"检测到模型切换: {modelName} -> {currentModel}，正在重新初始化...");
                Console.ResetColor();
                modelName = currentModel ?? throw new InvalidOperationException("未选择模型");
                agent = await profile.CreateAgentAsync(agentFactory, modelName, workspace, ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ 已切换到模型: {modelName}");
                Console.ResetColor();
            }

            if (input.ToLower() == "exit")
                break;

            // 拦截 /skill -switch 和 /skill -off 命令（对话内切换 Skill）
            if (input.StartsWith("/skill ", StringComparison.OrdinalIgnoreCase) || input.Equals("/skill", StringComparison.OrdinalIgnoreCase))
            {
                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var subCmd = parts[1].ToLower();
                    if (subCmd == "-switch" || subCmd == "-s")
                    {
                        agent = await HandleSkillSwitchAsync(agent, profile, workspace, agentFactory, ruleEngine, pluginRegistry, skillRegistry, mcpRegistry, modelName);
                        continue;
                    }
                }
            }

            // 处理其他 / 命令
            if (input.StartsWith('/') && _executeCommandAsync != null)
            {
                var handled = await _executeCommandAsync(input);
                if (handled)
                    continue;
            }

            // 首次实际输入自动检测并激活 Skill
            if (!autoActivationAttempted && profile.ActiveSkill == null)
            {
                autoActivationAttempted = true;
                var detected = _skillRegistry.DetectSkills(input, 1).FirstOrDefault();
                if (detected != null)
                {
                    profile.ActiveSkill = detected;
                    agent = await profile.CreateAgentAsync(agentFactory, modelName, workspace, ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ 已自动激活 Skill: {detected.Name}（仅本次生效）");
                    Console.ResetColor();
                }
            }

            // RAG 自动检索注入：将检索结果拼接到用户输入前
            string finalInput = input;
            if (profile.RetrievalMode == "auto")
            {
                finalInput = await InjectRetrievalContextAsync(input, workspace, serviceProvider);
            }

            // 自动编排判定（仅普通工作区且 AutoDetect 启用时）
            var orchestrationOptions = serviceProvider.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.Orchestration;
            var autoDetectEnabled = orchestrationOptions?.AutoDetect ?? false;
            var isRagWorkspace = workspace.Type == "Rag";
            // 启发式预过滤：短输入且无复合关键词时跳过 planner，节省一次 LLM 调用
            var skipByHeuristic = orchestrationOptions?.HeuristicFilter?.ShouldSkipPlanning(input) ?? false;

            // ESC 键监听器：任务执行期间按 ESC 暂停（提前创建，覆盖 planner 和主对话）
            using var escListener = new EscKeyListener();
            // 设置取消令牌，使工具确认流程能响应 ESC
            var confirmContext = serviceProvider.GetRequiredService<ToolConfirmationContext>();
            confirmContext.CancellationToken = escListener.Token;

            if (autoDetectEnabled && !isRagWorkspace && !skipByHeuristic)
            {
                try
                {
                    escListener.Start();
                    using var plannerSpinner = new ResponseSpinner("正在分析任务...");

                    var planner = serviceProvider.GetRequiredService<LuBan.AIAgent.Orchestration.Planner.ITaskPlanner>();
                    var graph = await planner.PlanAsync(finalInput, escListener.Token);

                    plannerSpinner.Stop();

                    // 至少 2 个节点才视为复合任务，单节点直接由主 Agent 处理
                    if (graph != null && graph.Nodes.Count >= 2)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"检测到复合任务，已拆解为 {graph.Nodes.Count} 个子任务...");
                        Console.ResetColor();

                        var orchestrator = serviceProvider.GetRequiredService<LuBan.AIAgent.Orchestration.IOrchestrator>();
                        var orchestrationResult = await orchestrator.RunAsync(graph, escListener.Token);

                        if (orchestrationResult.OverallStatus == "completed" || orchestrationResult.OverallStatus == "partial")
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write($"{DateTime.Now:HH:mm:ss} 🤖 ");
                            Console.ResetColor();
                            Console.WriteLine(orchestrationResult.FinalOutput);
                            escListener.Stop();
                            continue;
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"编排失败（{orchestrationResult.OverallStatus}），回退到普通对话...");
                            Console.ResetColor();
                        }
                    }

                    escListener.Stop();
                }
                catch (OperationCanceledException)
                {
                    escListener.Stop();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("任务规划已取消");
                    Console.ResetColor();
                    continue;
                }
                catch (Exception ex)
                {
                    escListener.Stop();
                    Logger.Warn("Planner 决策失败，回退到普通对话", ex);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("任务规划失败，使用普通模式处理...");
                    Console.ResetColor();
                }
            }

            var hasToolCalls = false;

            // 即时反馈：回车后立即显示 spinner，首个 chunk 到达后停止
            using var spinner = new ResponseSpinner("正在思考...");

            try
            {
                var finalResponseBuilder = new System.Text.StringBuilder();
                var toolCalls = new System.Collections.Generic.List<string>();
                var hasThinkingContent = false;
                var hasAnswerContent = false;

                escListener.Start();
                spinner.Start();

                try
                {
                    await foreach (var update in agent.RunStreamingAsync(finalInput, escListener.Token))
                    {
                        // 首个 chunk 到达，停止 spinner
                        if (update.Contents != null && update.Contents.Any())
                        {
                            spinner.Stop();
                        }

                        if (update.Contents == null) continue;

                        foreach (var content in update.Contents)
                        {
                            if (content is TextReasoningContent reasoning)
                            {
                                if (!string.IsNullOrWhiteSpace(reasoning.Text))
                                {
                                    if (!hasThinkingContent)
                                    {
                                        Console.ForegroundColor = ConsoleColor.DarkGray;
                                        Console.WriteLine("💭 思考过程:");
                                        hasThinkingContent = true;
                                    }
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.Write(reasoning.Text);
                                }
                            }
                            else if (content is Microsoft.Extensions.AI.FunctionCallContent functionCall)
                            {
                                if (!hasToolCalls)
                                {
                                    if (hasThinkingContent)
                                    {
                                        Console.WriteLine();
                                    }
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine("工具调用过程:");
                                    hasToolCalls = true;
                                }
                                var toolInfo = $"调用工具: {functionCall.Name}";
                                if (functionCall.Arguments != null && functionCall.Arguments.Count > 0)
                                {
                                    var args = string.Join(", ", functionCall.Arguments.Take(3).Select(a => $"{a.Key}={TruncateValue(a.Value)}"));
                                    if (functionCall.Arguments.Count > 3) args += ", ...";
                                    toolInfo += $"({args})";
                                }
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {toolInfo}");
                                toolCalls.Add(toolInfo);
                            }
                            else if (content is Microsoft.Extensions.AI.FunctionResultContent functionResult)
                            {
                                // 显示工具执行结果摘要，避免用户看不到工具输出
                                var resultObj = functionResult.Result;
                                string resultSummary;
                                if (resultObj is Exception ex)
                                {
                                    resultSummary = $"异常: {ex.Message}";
                                }
                                else
                                {
                                    resultSummary = resultObj?.ToString() ?? "(空结果)";
                                }
                                // 替换换行符避免破坏控制台格式
                                resultSummary = resultSummary.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                                if (resultSummary.Length > 200)
                                {
                                    resultSummary = resultSummary.Substring(0, 200) + "...";
                                }
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  → 结果: {resultSummary}");
                            }
                            else if (content is TextContent text && !string.IsNullOrWhiteSpace(text.Text))
                            {
                                if (!hasAnswerContent)
                                {
                                    if (hasThinkingContent || hasToolCalls)
                                    {
                                        Console.WriteLine();
                                        Console.ResetColor();
                                    }
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.Write($"{DateTime.Now:HH:mm:ss} 🤖 ");
                                    hasAnswerContent = true;
                                }
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.Write(text.Text);
                                finalResponseBuilder.Append(text.Text);
                            }
                        }
                    }

                    if (hasThinkingContent || hasToolCalls || hasAnswerContent)
                    {
                        Console.WriteLine();
                        Console.ResetColor();
                    }
                }
                catch (OperationCanceledException) when (escListener.IsPaused)
                {
                    // ESC 触发的暂停
                    spinner.Stop();
                    escListener.Stop();

                    var resumed = escListener.WaitForResumeOrCancel();
                    if (!resumed)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("已终止当前操作");
                        Console.ResetColor();
                        continue;
                    }

                    // 用户选择继续：由于流式已中断，提示用户重新发送
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("⚠️  流式响应已中断，请重新输入您的问题");
                    Console.ResetColor();
                    continue;
                }

                escListener.Stop();

                var finalResponse = finalResponseBuilder.ToString();

                if (!hasAnswerContent && string.IsNullOrEmpty(finalResponse))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    if (hasToolCalls)
                    {
                        Console.WriteLine($"⚠️  模型进行了 {toolCalls.Count} 次工具调用后未生成最终回复（可能已达 MaxToolLoopIterations 上限）。");
                        Console.WriteLine("   可尝试：1) 提高 appsettings.json 中的 MaxToolLoopIterations；2) 简化任务或拆分为多轮对话。");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("（无响应）");
                    }
                    Console.ResetColor();
                }

                Console.WriteLine();

                if (profile.ActiveSkill != null)
                {
                    var skillName = profile.ActiveSkill.Name;
                    profile.ActiveSkill = null;
                    agent = await profile.CreateAgentAsync(agentFactory, modelName, workspace, ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"✓ Skill '{skillName}' 已自动取消（一次性生效）");
                    Console.ResetColor();
                }
            }
            catch (OperationCanceledException)
            {
                spinner.Stop();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("操作已取消");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                spinner.Stop();
                Logger.Error("AgiCommand 对话循环异常", ex, input);
                var errorType = hasToolCalls ? "工具执行后模型处理" : "模型调用";
                WriteError($"[{errorType}失败] {GetFriendlyApiErrorMessage(ex)}");
            }
            finally
            {
                // 清理取消令牌，避免影响下一次对话
                confirmContext.CancellationToken = default;
            }
        }
    }

    /// <summary>
    /// 处理 /skill -switch 命令：列出可用 Skill 并让用户选择，切换后重建 Agent。
    /// </summary>
    private async Task<LuBanAgent> HandleSkillSwitchAsync(
        LuBanAgent currentAgent, AgentProfile profile, WorkspaceInfo workspace,
        ILuBanAgentFactory agentFactory, RuleEngine ruleEngine, ToolPluginRegistry pluginRegistry,
        SkillRegistry skillRegistry, MCPRegistry mcpRegistry, string modelName)
    {
        var skills = _skillRegistry.GetAll();
        if (skills.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("暂无可用 Skill");
            Console.ResetColor();
            return currentAgent;
        }

        Console.WriteLine();
        Console.WriteLine("可用 Skills:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"{"#",-4} {"类别",-12} {"名称",-20} {"描述",-40} {"来源"}");
        Console.ResetColor();

        for (int i = 0; i < skills.Count; i++)
        {
            var s = skills[i];
            var source = s is FileSkill ? "文件" : (s is CustomSkill ? "自定义" : "内置");
            var active = profile.ActiveSkill?.Id == s.Id ? " ✓" : "";
            Console.WriteLine($"{i + 1,-4} {s.Category,-12} {s.Name,-20} {Truncate(s.Description, 38),-40} {source}{active}");
        }

        Console.WriteLine();
        Console.Write("请选择编号 (1-{0}), 或 0 取消: ", skills.Count);
        var choice = Console.ReadLine()?.Trim();

        if (!int.TryParse(choice, out var index) || index < 0 || index > skills.Count)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("无效选择，已取消");
            Console.ResetColor();
            return currentAgent;
        }

        if (index == 0)
        {
            if (profile.ActiveSkill != null)
            {
                profile.ActiveSkill = null;
                var newAgent = await profile.CreateAgentAsync(agentFactory, modelName, workspace, ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ 已取消 Skill");
                Console.ResetColor();
                return newAgent;
            }
            Console.WriteLine("当前没有激活的 Skill");
            return currentAgent;
        }

        var selected = skills[index - 1];
        profile.ActiveSkill = selected;
        var agent = await profile.CreateAgentAsync(agentFactory, modelName, workspace, ruleEngine, pluginRegistry, skillRegistry, mcpRegistry);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ 已激活 Skill: {selected.Name}（仅本次生效）");
        Console.ResetColor();
        if (!string.IsNullOrEmpty(selected.PromptTemplate))
        {
            var preview = selected.PromptTemplate.Length > 100
                ? selected.PromptTemplate.Substring(0, 100) + "..."
                : selected.PromptTemplate;
            Console.WriteLine($"📄 指令预览: {preview.Replace("\n", " ")}");
        }
        Console.ResetColor();

        return agent;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }

    /// <summary>
    /// RAG 自动检索注入：将检索结果拼接到用户输入前
    /// </summary>
    /// <param name="query">用户原始输入</param>
    /// <param name="workspace">当前工作区</param>
    /// <param name="serviceProvider">服务提供者</param>
    /// <returns>拼接检索上下文后的输入；检索失败时返回原始输入</returns>
    private async Task<string> InjectRetrievalContextAsync(string query, WorkspaceInfo workspace, IServiceProvider serviceProvider)
    {
        try
        {
            var retrievalService = serviceProvider.GetService<IRetrievalService>();
            if (retrievalService == null) return query;

            var results = await retrievalService.SearchAsync(query);
            if (results == null || results.Count == 0) return query;

            // 去重：相同 SymbolName（或 FilePath）的检索结果按 StartLine 降序取第一条
            // 注：RetrievalResult 无 IndexedTime 字段，StartLine 作为内容位置的近似排序依据
            var deduped = results
                .GroupBy(r => r.SymbolName ?? r.FilePath)
                .Select(g => g.OrderByDescending(r => r.StartLine).First())
                .Take(5)
                .ToList();

            var context = new StringBuilder();
            context.AppendLine("以下是从知识库检索到的相关文档片段：");
            context.AppendLine("---");
            foreach (var r in deduped)
            {
                context.AppendLine($"文件: {r.FilePath}");
                context.AppendLine($"内容: {r.Content}");
                context.AppendLine("---");
            }
            context.AppendLine();
            context.AppendLine("请基于以上文档片段回答用户问题：");
            context.AppendLine(query);

            return context.ToString();
        }
        catch
        {
            return query; // 检索失败降级为原始输入
        }
    }

    private static string TruncateValue(object? value, int maxLength = 50)
    {
        var str = value?.ToString() ?? "null";
        if (str.Length > maxLength)
            return str.Substring(0, maxLength) + "...";
        return str;
    }

    /// <summary>
    /// 显示会话历史摘要（最近 5 条消息）
    /// </summary>
    private async Task DisplaySessionHistoryAsync(string sessionId)
    {
        try
        {
            // 获取活跃消息（排除已压缩的），按时间倒序取最新 5 条
            var messages = (await _sessionManager.GetActiveMessagesAsync(sessionId))
                .Where(m => m.Role != "summary") // 排除摘要消息
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .ToList();

            if (messages.Count == 0)
                return;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("━━━ 最近对话 ━━━");
            foreach (var msg in messages)
            {
                var role = msg.Role switch
                {
                    "user" => "👶 用户",
                    "assistant" => "🤖 助手",
                    "system" => "⚙️ 系统",
                    _ => "💬 消息"
                };
                var preview = msg.Content.Length > 100 
                    ? msg.Content.Substring(0, 100) + "..." 
                    : msg.Content;
                Console.WriteLine($"{role}: {preview.Replace("\n", " ").Replace("\r", "")}");
            }
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Logger.Error("显示会话历史失败", ex, sessionId);
        }
    }
}