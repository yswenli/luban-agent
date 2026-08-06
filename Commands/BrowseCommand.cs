/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： BrowseCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：浏览网站命令
*
*****************************************************************************/
using LuBan.AIAgent.Skills;

namespace LubanAgent.Commands;

/// <summary>
/// 浏览网站命令，用自然语言操作网站
/// </summary>
public class BrowseCommand : CommandBase
{
    private readonly Func<string, Task<bool>>? _executeCommandAsync;
    private readonly IWorkspaceManager? _workspaceManager;
    private readonly SkillRegistry _skillRegistry;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "browse";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "用自然语言操作网站";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    public BrowseCommand(ConfigManager configManager, IConfiguration configuration, Func<string, Task<bool>>? executeCommandAsync = null, IWorkspaceManager? workspaceManager = null, SkillRegistry? skillRegistry = null)
        : base(configManager, configuration)
    {
        _executeCommandAsync = executeCommandAsync;
        _workspaceManager = workspaceManager;
        _skillRegistry = skillRegistry!;
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

        // 1. 获取当前工作区并检查授权（与 AgiCommand 保持一致）
        var workspace = _workspaceManager?.CurrentWorkspace;
        if (workspace == null)
        {
            WriteError("请先使用 /work -switch 切换到工作区");
            return;
        }

        // 2. RAG 工作区禁用 /browse（仅支持知识库问答，不支持浏览器操作）
        if (workspace.Type == "Rag")
        {
            WriteError("RAG 知识库工作区不支持浏览器操作，请使用 /work -switch 切换到普通工作区");
            return;
        }

        // 3. 检查授权状态
        if (!workspace.IsAuthorized)
        {
            var authorized = await _workspaceManager!.EnsureAuthorizedAsync(workspace);
            if (!authorized) return;
        }

        Console.WriteLine();
        Console.Write("请输入目标网站 URL: ");
        var url = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(url))
        {
            WriteError("URL 不能为空");
            return;
        }

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            WriteError("无效的 URL，仅支持 http:// 和 https://");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("输入自然语言指令来操作网站 (输入 'done' 结束):");
        Console.WriteLine("例如: '导航到登录页面', '点击提交按钮', '在搜索框输入关键词'");
        Console.WriteLine("按 ESC 可暂停当前操作，输入 'c' 继续，输入 'q' 终止");
        Console.WriteLine("输入 /skill -switch 选择 Skill，/skill -off 取消 Skill");
        Console.WriteLine();

        // 加载文件级 Skill
        var workspaceSkillsDir = workspace.ConfigPath != null
            ? Path.Combine(workspace.RootPath, workspace.ConfigPath, "skills")
            : null;
        if (workspaceSkillsDir != null)
        {
            _skillRegistry.LoadFromWorkspace(workspace.RootPath);
        }

        var baseSystemPrompt = BuildSystemPrompt(url);
        ISkill? activeSkill = null;
        using var serviceProvider = BuildServiceProvider();

        try
        {
            var agentFactory = serviceProvider.GetRequiredService<ILuBanAgentFactory>();
            var agent = await agentFactory.CreateAsync(
                modelName: ConfigManager.SelectedModel,
                systemPrompt: baseSystemPrompt,
                toolGroups: new[] { "browser" });

            Console.WriteLine($"正在连接 {url}...");
            var result = await RunInteractionLoop(agent, agentFactory, url, activeSkill, serviceProvider);
            agent = result.agent;
            activeSkill = result.activeSkill;
        }
        catch (Exception ex)
        {
            Logger.Error("BrowseCommand 初始化失败", ex);
            WriteError(ex.Message);
        }
    }

    /// <summary>
    /// 构建系统提示词
    /// </summary>
    private static string BuildSystemPrompt(string url, ISkill? activeSkill = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($@"你是一个浏览器自动化助手。用户会用自然语言描述他们想要在网站上执行的操作。
            当前目标网站: {url}

            你可以使用以下工具来操作浏览器:
            - NavigateAsync: 导航到指定 URL
            - ClickAsync: 点击页面元素
            - TypeTextAsync: 在输入框中输入文本
            - ScreenshotAsync: 截取页面截图
            - GetContentAsync: 获取页面内容
            - WaitForSelectorAsync: 等待元素出现
            - GetCurrentUrlAsync: 获取当前页面 URL

            请根据用户的自然语言描述，使用合适的工具来完成任务。

            【错误处理与重试策略】
            当工具返回失败结果时，你必须遵循以下策略，绝不能直接放弃或静默结束：
            1. **重试**: 同一操作最多重试 2 次（共 3 次尝试）。重试时可调整策略（如更换选择器、缩短等待时间）。
            2. **换方法**: 若重试仍失败，尝试替代方案。例如：
               - 导航失败 → 尝试搜索引擎获取信息（如 https://www.bing.com/search?q=关键词）
               - 点击失败 → 尝试用 JavaScript 执行或更换选择器
               - 获取内容失败 → 尝试截图让用户查看，或缩小 CSS 选择器范围
            3. **降级**: 若所有技术手段均失败，用已获取的部分信息给出力所能及的结果。
            4. **告知用户**: 无论成功与否，最终必须向用户清晰说明：
               - 成功时：汇报任务完成情况与关键结果
               - 失败时：说明失败原因、已尝试的方法，并给出下一步建议

            绝不允许在工具失败后直接结束对话而不给出任何说明。");

        if (activeSkill != null && !string.IsNullOrEmpty(activeSkill.PromptTemplate))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"## 当前激活的 Skill: {activeSkill.Name}");
            sb.AppendLine(activeSkill.PromptTemplate);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 运行交互循环，支持 ESC 取消、实时状态显示和 / 命令
    /// </summary>
    private async Task<(LuBanAgent agent, ISkill? activeSkill)> RunInteractionLoop(LuBanAgent agent, ILuBanAgentFactory agentFactory, string url, ISkill? activeSkill, IServiceProvider serviceProvider)
    {
        bool autoActivationAttempted = false;
        while (true)
        {
            // 显示当前激活的 Skill
            if (activeSkill != null)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($"[{activeSkill.Name}] ");
                Console.ResetColor();
            }
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("指令 (或输入 'done' 结束，/ 命令可用): ");
            Console.ResetColor();
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input) || input.ToLower() == "done")
                return (agent, activeSkill);

            // 拦截 /skill -switch 和 /skill -off 命令
            if (input.StartsWith("/skill ", StringComparison.OrdinalIgnoreCase) || input.Equals("/skill", StringComparison.OrdinalIgnoreCase))
            {
                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var subCmd = parts[1].ToLower();
                    if (subCmd == "-switch" || subCmd == "-s")
                    {
                        var result = await HandleSkillSwitchAsync(agent, agentFactory, url, activeSkill);
                        agent = result.agent;
                        activeSkill = result.activeSkill;
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
            if (!autoActivationAttempted && activeSkill == null)
            {
                autoActivationAttempted = true;
                var detected = _skillRegistry.DetectSkills(input, 1).FirstOrDefault();
                if (detected != null)
                {
                    activeSkill = detected;
                    var newPrompt = BuildSystemPrompt(url, activeSkill);
                    agent = await agentFactory.CreateAsync(
                        modelName: ConfigManager.SelectedModel!,
                        systemPrompt: newPrompt,
                        toolGroups: new[] { "browser" });
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ 已自动激活 Skill: {detected.Name}（仅本次生效）");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();

            // ESC 键监听器：任务执行期间按 ESC 暂停
            using var escListener = new EscKeyListener();
            // 设置取消令牌，使工具确认流程能响应 ESC
            var confirmContext = serviceProvider.GetRequiredService<ToolConfirmationContext>();
            confirmContext.CancellationToken = escListener.Token;
            // 即时反馈：回车后立即显示 spinner
            using var spinner = new ResponseSpinner("正在处理浏览器指令...");

            try
            {
                var finalResponseBuilder = new System.Text.StringBuilder();
                var hasContent = false;

                escListener.Start();
                spinner.Start();

                try
                {
                    await foreach (var update in agent.RunStreamingAsync(input, escListener.Token))
                    {
                        if (update.Contents != null && update.Contents.Any())
                        {
                            spinner.Stop();
                        }

                        if (update.Contents == null) continue;

                        foreach (var content in update.Contents)
                        {
                            if (content is Microsoft.Extensions.AI.FunctionCallContent functionCall)
                            {
                                spinner.UpdateStatus($"正在执行: {functionCall.Name}");
                            }
                            else if (content is Microsoft.Extensions.AI.TextContent text && !string.IsNullOrWhiteSpace(text.Text))
                            {
                                if (!hasContent)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.Write($"{DateTime.Now:HH:mm:ss} 🤖 ");
                                    Console.ResetColor();
                                    hasContent = true;
                                }
                                Console.Write(text.Text);
                                finalResponseBuilder.Append(text.Text);
                            }
                        }
                    }

                    if (hasContent)
                    {
                        Console.WriteLine();
                    }
                }
                catch (OperationCanceledException) when (escListener.IsPaused)
                {
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

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("⚠️  流式响应已中断，请重新输入您的指令");
                    Console.ResetColor();
                    continue;
                }

                escListener.Stop();

                var finalResponse = finalResponseBuilder.ToString();
                if (!hasContent && string.IsNullOrEmpty(finalResponse))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("（无响应）");
                    Console.ResetColor();
                }

                if (activeSkill != null)
                {
                    var skillName = activeSkill.Name;
                    activeSkill = null;
                    var newPrompt = BuildSystemPrompt(url, null);
                    agent = await agentFactory.CreateAsync(
                        modelName: ConfigManager.SelectedModel!,
                        systemPrompt: newPrompt,
                        toolGroups: new[] { "browser" });
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"✓ Skill '{skillName}' 已自动取消（一次性生效）");
                    Console.ResetColor();
                }
            }
            catch (OperationCanceledException)
            {
                spinner.Stop();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("操作已取消");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                spinner.Stop();
                Logger.Error("BrowseCommand 对话循环异常", ex);
                Console.WriteLine();
                WriteError(GetFriendlyApiErrorMessage(ex));
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
    private async Task<(LuBanAgent agent, ISkill? activeSkill)> HandleSkillSwitchAsync(
        LuBanAgent currentAgent, ILuBanAgentFactory agentFactory, string url, ISkill? currentSkill)
    {
        var skills = _skillRegistry.GetAll();
        if (skills.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("暂无可用 Skill");
            Console.ResetColor();
            return (currentAgent, currentSkill);
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
            var active = currentSkill?.Id == s.Id ? " ✓" : "";
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
            return (currentAgent, currentSkill);
        }

        if (index == 0)
        {
            if (currentSkill != null)
            {
                currentSkill = null;
                var newPrompt = BuildSystemPrompt(url, null);
                var newAgent = await agentFactory.CreateAsync(
                    modelName: ConfigManager.SelectedModel!,
                    systemPrompt: newPrompt,
                    toolGroups: new[] { "browser" });
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ 已取消 Skill");
                Console.ResetColor();
                return (newAgent, null);
            }
            Console.WriteLine("当前没有激活的 Skill");
            return (currentAgent, currentSkill);
        }

        var selected = skills[index - 1];
        var prompt = BuildSystemPrompt(url, selected);
        var agent = await agentFactory.CreateAsync(
            modelName: ConfigManager.SelectedModel!,
            systemPrompt: prompt,
            toolGroups: new[] { "browser" });

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ 已激活 Skill: {selected.Name}（仅本次生效）");
        Console.ResetColor();

        return (agent, selected);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }
}