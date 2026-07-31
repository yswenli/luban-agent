/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： OrchestrateCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：复合任务编排命令，通过主 Agent 拆解 DAG 并调度 SubAgent 执行
*
*****************************************************************************/
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;

namespace LubanAgent.Commands;

/// <summary>
/// 复合任务编排命令，通过主 Agent 拆解 DAG 并调度 SubAgent 执行。
/// </summary>
public class OrchestrateCommand : CommandBase
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 命令名称。
    /// </summary>
    public override string Name => "orchestrate";

    /// <summary>
    /// 命令描述。
    /// </summary>
    public override string Description => "复合任务编排（DAG 拆解 + SubAgent 调度）";

    /// <summary>
    /// 创建命令实例。
    /// </summary>
    /// <param name="configManager">配置管理器。</param>
    /// <param name="configuration">应用配置。</param>
    /// <param name="serviceProvider">服务提供者。</param>
    public OrchestrateCommand(
        ConfigManager configManager,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
        : base(configManager, configuration)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 执行命令，进入编排交互循环。
    /// </summary>
    public override async Task ExecuteAsync()
    {
        if (!ConfigManager.HasSelectedModel)
        {
            WriteError("请先使用 /model -switch 选择模型");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("复合任务编排模式");
        Console.WriteLine("输入复合任务，AI 将拆解为 DAG 并调度 SubAgent 执行");
        Console.WriteLine("输入 'exit' 返回主菜单");
        Console.WriteLine();

        var orchestrator = _serviceProvider.GetRequiredService<IOrchestrator>();

        ToolConfirmationService.ConfirmationCallback = (toolName, args) =>
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️  SubAgent 危险操作: {Markup.Escape(toolName)}[/]");
            AnsiConsole.Markup("[yellow]是否执行？(y/N): [/]");
            var input = Console.ReadLine()?.Trim().ToLower();
            return input == "y" || input == "yes";
        };

        try
        {
            while (true)
            {
                Console.Write("📝 ");
                var input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input)) continue;
                if (input.ToLower() == "exit") break;

                try
                {
                    Console.WriteLine();
                    await foreach (var progress in orchestrator.RunStreamingAsync(input))
                    {
                        RenderProgress(progress);
                    }
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    WriteError($"编排失败: {ex.Message}");
                }
            }
        }
        finally
        {
            ToolConfirmationService.ConfirmationCallback = null;
        }
    }

    /// <summary>
    /// 渲染编排进度事件到控制台。
    /// </summary>
    /// <param name="progress">编排进度事件。</param>
    private static void RenderProgress(OrchestrationProgress progress)
    {
        switch (progress.EventType)
        {
            case ProgressEventType.PlanningStarted:
                AnsiConsole.MarkupLine("[cyan]🔄 开始规划任务图谱...[/]");
                break;

            case ProgressEventType.PlanningCompleted:
                AnsiConsole.MarkupLine($"[green]✓ 规划完成: {Markup.Escape(progress.Message ?? "")}[/]");
                break;

            case ProgressEventType.NodeStarted:
                AnsiConsole.MarkupLine($"[blue]▶ 开始执行节点: {Markup.Escape(progress.NodeId ?? "")}[/]");
                break;

            case ProgressEventType.NodeCompleted:
                AnsiConsole.MarkupLine($"[green]✓ 节点完成: {Markup.Escape(progress.NodeId ?? "")}[/]");
                break;

            case ProgressEventType.NodeFailed:
                AnsiConsole.MarkupLine($"[red]✗ 节点失败: {Markup.Escape(progress.NodeId ?? "")} - {Markup.Escape(progress.Message ?? "")}[/]");
                break;

            case ProgressEventType.LayerCompleted:
                AnsiConsole.MarkupLine($"[grey]── {Markup.Escape(progress.Message ?? "")} ──[/]");
                break;

            case ProgressEventType.OrchestratingCompleted:
                AnsiConsole.MarkupLine($"[yellow]🎯 {Markup.Escape(progress.Message ?? "")}[/]");
                break;
        }
    }
}
