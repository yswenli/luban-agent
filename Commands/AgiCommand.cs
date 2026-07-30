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
namespace LubanAgent.Commands;

/// <summary>
/// 通用 Agent 对话命令，支持工具调用和 thinking 显示
/// </summary>
public class AgiCommand : CommandBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IServiceProvider _serviceProvider;
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
    public AgiCommand(ConfigManager configManager, IConfiguration configuration, ISessionManager sessionManager, IServiceProvider serviceProvider, Func<string, Task<bool>>? executeCommandAsync = null)
        : base(configManager, configuration)
    {
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;
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

        // 创建或获取当前会话
        var currentSession = _sessionManager.CurrentSession;
        if (currentSession == null)
        {
            currentSession = await _sessionManager.CreateSessionAsync(userId: "default", title: "新对话");
            Console.WriteLine($"已创建新会话: {currentSession.SessionId}");
        }

        // 使用注入的 ServiceProvider，而不是创建新的
        // 这样可以确保所有服务（包括 IRetrievalService）都可用
        var serviceProvider = _serviceProvider;

        Console.WriteLine();
        Console.WriteLine("可用工具: 文件系统、脚本执行、浏览器、数据库、Redis、Web请求");
        Console.WriteLine("提示: AI 会自动判断是否需要使用工具来回答你的问题");
        Console.WriteLine("      危险操作（写入、删除、执行脚本）需要用户确认");
        Console.WriteLine("      输入 / 命令可执行操作，如 /session switch 1");
        Console.WriteLine("示例: 帮我查一下D盘下面有哪些目录");
        Console.WriteLine($"当前会话: {currentSession.Title ?? "未命名"}");
        Console.WriteLine("开始对话 (输入 'exit' 返回主菜单)");
        Console.WriteLine();

        try
        {
            // 设置工具确认回调
            ToolConfirmationService.ConfirmationCallback = (toolName, args) =>
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[yellow]⚠️  [bold]危险操作请求: {Markup.Escape(toolName)}[/][/]");
                AnsiConsole.MarkupLine("[yellow]参数:[/]");
                var formattedArgs = ToolConfirmationService.FormatArguments(args, 500);
                foreach (var line in formattedArgs.Split('\n'))
                {
                    AnsiConsole.WriteLine(line);
                }
                AnsiConsole.WriteLine();

                AnsiConsole.Markup("[yellow]是否执行此操作？(y/N): [/]");
                var input = Console.ReadLine()?.Trim().ToLower();
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
            };

            var agentFactory = serviceProvider.GetRequiredService<ILuBanAgentFactory>();
            
            // 创建 Agent 时不指定 toolGroups，启用所有工具
            var agent = await agentFactory.CreateAsync(
                modelName: ConfigManager.SelectedModel,
                useSessionHistory: true,
                systemPrompt: @"你是一个智能助手，拥有以下工具能力：

1. **文件系统操作**：可以读取文件、写入文件、列出目录内容
2. **脚本执行**：可以执行 Shell、Python、Lua 等脚本
3. **浏览器自动化**：可以打开网页、点击元素、输入文本、截图
4. **数据库操作**：可以执行 SQL 查询
5. **Redis 操作**：可以执行 Redis 命令
6. **Web 请求**：可以发送 HTTP 请求

当用户的请求涉及上述操作时，**必须使用相应的工具**来完成，不要说'我无法访问'或'我没有这个能力'。

例如：
- 用户说'帮我看看 D 盘有什么文件' -> 使用 list_directory 工具
- 用户说'读取某个文件的内容' -> 使用 read_file 工具
- 用户说'执行这个命令' -> 使用 run_shell 工具
- 用户说'打开某个网页' -> 使用浏览器工具

请立即使用工具来帮助用户完成任务。");

            Console.WriteLine("✓ 工具插件已加载（根据 appsettings.json 配置启用）");
            Console.WriteLine();

            await RunChatLoop(agent);
        }
        catch (Exception ex)
        {
            Logger.Error("AgiCommand 初始化失败", ex);
            WriteError(ex.Message);
        }
        finally
        {
            // 清理确认回调
            ToolConfirmationService.ConfirmationCallback = null;
        }
    }

    /// <summary>
    /// 运行对话循环，支持工具调用显示、ESC 取消和 / 命令
    /// </summary>
    private async Task RunChatLoop(LuBanAgent agent)
    {
        while (true)
        {
            Console.Write("👶 ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;

            if (input.ToLower() == "exit")
                break;

            // 处理 / 命令
            if (input.StartsWith('/') && _executeCommandAsync != null)
            {
                var handled = await _executeCommandAsync(input);
                if (handled)
                    continue;
            }

            var hasToolCalls = false;

            try
            {
                var finalResponseBuilder = new System.Text.StringBuilder();
                var cancelled = false;
                var toolCalls = new System.Collections.Generic.List<string>();
                var hasThinkingContent = false;
                // var hasToolCalls = false; // moved outside
                var hasAnswerContent = false;

                using var cts = new CancellationTokenSource();
                var cancellationToken = cts.Token;

                await Console.Out.WriteLineAsync();

                try
                {
                    await foreach (var update in agent.RunStreamingAsync(input, cancellationToken))
                    {
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
                                        Console.ResetColor();
                                    }
                                    Console.ForegroundColor = ConsoleColor.Cyan;
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
                                Console.WriteLine($"  {toolInfo}");
                                toolCalls.Add(toolInfo);
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
                                    Console.Write("🤖 ");
                                    Console.ResetColor();
                                    hasAnswerContent = true;
                                }
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
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                
                if (cancelled)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("已取消当前操作");
                    Console.ResetColor();
                    continue;
                }
                
                var finalResponse = finalResponseBuilder.ToString();
                
                if (!hasAnswerContent && string.IsNullOrEmpty(finalResponse))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("（无响应）");
                    Console.ResetColor();
                }
                
                Console.WriteLine();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("操作已取消");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Logger.Error("AgiCommand 对话循环异常", ex, input);
                var errorType = hasToolCalls ? "工具执行后模型处理" : "模型调用";
                WriteError($"[{errorType}失败] {GetFriendlyApiErrorMessage(ex)}");
            }
        }
    }

    private static string TruncateValue(object? value, int maxLength = 50)
    {
        var str = value?.ToString() ?? "null";
        if (str.Length > maxLength)
            return str.Substring(0, maxLength) + "...";
        return str;
    }

}