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
namespace LubanAgent.Commands;

/// <summary>
/// 浏览网站命令，用自然语言操作网站
/// </summary>
public class BrowseCommand : CommandBase
{
    private readonly Func<string, Task<bool>>? _executeCommandAsync;

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
    public BrowseCommand(ConfigManager configManager, IConfiguration configuration, Func<string, Task<bool>>? executeCommandAsync = null)
        : base(configManager, configuration)
    {
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
        Console.WriteLine();

        var systemPrompt = BuildSystemPrompt(url);
        using var serviceProvider = BuildServiceProvider();

        // 任务根目录确认（浏览器场景主要用于下载/截图保存路径限制）
        var pathGuard = serviceProvider.GetRequiredService<PathGuard>();
        var options = serviceProvider.GetRequiredService<IOptions<LuBanAgentOptions>>().Value;
        var taskScope = TaskSessionScope.CreateInteractive(pathGuard, options, "/browse");
        if (taskScope == null) return;

        try
        {
            var agentFactory = serviceProvider.GetRequiredService<ILuBanAgentFactory>();
            var agent = await agentFactory.CreateAsync(
                modelName: ConfigManager.SelectedModel,
                systemPrompt: systemPrompt,
                toolGroups: new[] { "browser" });

            Console.WriteLine($"正在连接 {url}...");
            await RunInteractionLoop(agent);
        }
        catch (Exception ex)
        {
            Logger.Error("BrowseCommand 初始化失败", ex);
            WriteError(ex.Message);
        }
        finally
        {
            taskScope?.Dispose();
        }
    }

    /// <summary>
    /// 构建系统提示词
    /// </summary>
    private static string BuildSystemPrompt(string url)
    {
        return $@"你是一个浏览器自动化助手。用户会用自然语言描述他们想要在网站上执行的操作。
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

            绝不允许在工具失败后直接结束对话而不给出任何说明。";
    }

    /// <summary>
    /// 运行交互循环，支持 ESC 取消、实时状态显示和 / 命令
    /// </summary>
    private async Task RunInteractionLoop(LuBanAgent agent)
    {
        while (true)
        {
            Console.WriteLine();
            Console.Write("指令 (或输入 'done' 结束，/ 命令可用): ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input) || input.ToLower() == "done")
                break;

            // 处理 / 命令
            if (input.StartsWith('/') && _executeCommandAsync != null)
            {
                var handled = await _executeCommandAsync(input);
                if (handled)
                    continue;
            }

            Console.WriteLine();

            // ESC 键监听器：任务执行期间按 ESC 暂停
            using var escListener = new EscKeyListener();
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
                                    Console.Write("🤖 ");
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
        }
    }
}