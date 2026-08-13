/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.ViewModels
*文件名： CommandViewModel
*版本号： V1.0.0.0
*唯一标识：命令 ViewModel
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：内联命令路由，解析 / 输入为命令执行，结果通过 TuiOutputWriter 输出到会话文档。
*统一 /help /clear /mode 和 /provider /model /session 等所有命令。
*
*****************************************************************************/
using LubanAgentCli.App.Models;

namespace LubanAgentCli.App.ViewModels;

/// <summary>
/// 命令 ViewModel。解析 / 输入、匹配命令、执行并将结果以 SystemBlock 追加到文档。
/// 统一 /help /clear /mode /provider /model /session /stats /work /rag 等所有命令。
/// </summary>
internal sealed class CommandViewModel
{
    private readonly ConversationDocument _doc;
    private readonly ConversationViewModel? _conversationVm;
    private readonly IServiceProvider _services;
    private readonly ITuiUiService _ui;
    private readonly TuiOutputWriter _writer;

    /// <summary>
    /// 请求退出应用时触发。
    /// </summary>
    public event Action? ExitRequested;

    /// <summary>
    /// 初始化命令 ViewModel。
    /// </summary>
    /// <param name="doc">会话文档模型。</param>
    /// <param name="conversationVm">会话 ViewModel（可为 null）。</param>
    /// <param name="services">根级 DI 容器。</param>
    /// <param name="dispatcher">UI 线程调度器。</param>
    /// <param name="ui">TUI 模态交互服务。</param>
    public CommandViewModel(
        ConversationDocument doc,
        ConversationViewModel? conversationVm,
        IServiceProvider services,
        IUiDispatcher dispatcher,
        ITuiUiService ui)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _conversationVm = conversationVm;
        _services = services;
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _writer = new TuiOutputWriter(_doc, dispatcher);
    }

    /// <summary>
    /// 处理以 / 开头的命令行输入。返回 true 表示已处理。
    /// </summary>
    public bool TryExecute(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
            return false;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/exit": case "/quit":
                ExitRequested?.Invoke();
                return true;

            // ── 已统一迁移的命令 ──
            case "/help":     ExecuteHelp(); return true;
            case "/clear":    ExecuteClear(); return true;
            case "/mode":     ExecuteMode(parts.Length > 1 ? parts[1] : null); return true;

            // ── 管理型命令（后台异步执行）──
            case "/provider": case "/p":         ExecuteManagementCommand<ProviderCommand>(parts); return true;
            case "/model":    case "/m":         ExecuteManagementCommand<ModelCommand>(parts); return true;
            case "/skill":    case "/sk":        ExecuteManagementCommand<SkillCommand>(parts); return true;
            case "/rule":     case "/r":         ExecuteManagementCommand<RuleCommand>(parts); return true;
            case "/mcp":      case "/mp":        ExecuteManagementCommand<MCPCommand>(parts); return true;
            case "/session":  case "/se":        ExecuteManagementCommand<SessionCommand>(parts); return true;
            case "/stats":    case "/st":        ExecuteManagementCommand<StatsCommand>(parts); return true;
            case "/work":     case "/w":         ExecuteManagementCommand<WorkCommand>(parts); return true;
            case "/rag":      case "/rg":        ExecuteManagementCommand<RagCommand>(parts); return true;

            // ── Agent 对话（默认激活）──
            case "/agi": case "/a": case "/browse": case "/b":
                _writer.WriteSuccess("Agent 已就绪，直接输入内容即可开始对话。（或输入 /help 查看帮助）");
                return true;

            default:
                _writer.WriteError($"未知命令: {cmd}，输入 /help 查看可用命令");
                return true;
        }
    }

    // ═══ Agent 对话命令 ═══

    private void ExecuteHelp()
    {
        _writer.WriteHeader("LuBan Agent CLI 帮助");
        _writer.WriteLine();
        _writer.WriteLine("直接输入文本即可与 Agent 对话（无需 /agi 前缀）。");
        _writer.WriteLine();
        _writer.WriteLine("可用命令:");
        _writer.WriteLine("  /help               显示此帮助");
        _writer.WriteLine("  /clear              清空会话历史");
        _writer.WriteLine("  /mode [name]        查看或切换权限模式");
        _writer.WriteLine("  /provider, /p       管理 AI Provider");
        _writer.WriteLine("  /model, /m          管理模型");
        _writer.WriteLine("  /skill, /sk         查看和執行 Skill");
        _writer.WriteLine("  /rule, /r           管理规则");
        _writer.WriteLine("  /mcp, /mp           管理 MCP 客户端");
        _writer.WriteLine("  /session, /se       管理对话会话");
        _writer.WriteLine("  /stats, /st         会话与 Token 统计");
        _writer.WriteLine("  /work, /w           工作区管理");
        _writer.WriteLine("  /rag, /rg           知识库管理");
        _writer.WriteLine("  /exit, /quit        退出程序");
        _writer.WriteLine();
        _writer.WriteLine("快捷键:");
        _writer.WriteLine("  Enter               提交输入");
        _writer.WriteLine("  Ctrl+Q              退出（需确认）");
        _writer.WriteLine("  Esc                 取消当前 Agent 任务；空闲时退出（需确认）");
        _writer.WriteLine("  Ctrl+L              重绘屏幕");
        _writer.WriteLine("  Shift+Tab           循环切换权限模式");
        _writer.WriteLine("  Tab                 切换对话/任务视图");
    }

    private void ExecuteClear()
    {
        _doc.Clear();
        _writer.WriteSuccess("会话历史已清空");
    }

    private void ExecuteMode(string? arg)
    {
        if (_conversationVm is null)
        {
            _writer.WriteInfo("Agent 尚未初始化，请先输入内容启动 Agent");
            return;
        }

        if (string.IsNullOrWhiteSpace(arg))
        {
            _writer.WriteInfo($"当前权限模式: {_conversationVm.PermissionModeDisplay}");
            _writer.WriteInfo("可用模式: default / plan / accept-edits / bypass（使用 Shift+Tab 切换）");
            return;
        }

        var mode = arg.ToLowerInvariant() switch
        {
            "default" => ToolPermissionMode.Default,
            "plan" => ToolPermissionMode.Plan,
            "accept-edits" or "acceptedits" => ToolPermissionMode.AcceptEdits,
            "bypass" or "bypasspermissions" => ToolPermissionMode.BypassPermissions,
            _ => (ToolPermissionMode?)null
        };

        if (mode is null)
        {
            _writer.WriteError($"无效模式: {arg}. 可用: default, plan, accept-edits, bypass");
            return;
        }

        _conversationVm.SetPermissionMode(mode.Value);
        _writer.WriteInfo($"权限模式已切换: {_conversationVm.PermissionModeDisplay}");
    }

    // ═══ 管理型命令（后台异步执行）═══

    private void ExecuteManagementCommand<TCommand>(string[] parts) where TCommand : CommandBase
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var command = ResolveCommand<TCommand>();
                if (command is null)
                {
                    _writer.WriteError($"命令 {typeof(TCommand).Name} 初始化失败");
                    return;
                }

                var expandedArgs = ExpandSubCommandAliases(parts);
                if (expandedArgs.Length > 1)
                {
                    var handled = await command.ExecuteAsync(expandedArgs.Skip(1).ToArray());
                    if (!handled)
                    {
                        await command.ExecuteAsync();
                    }
                }
                else
                {
                    await command.ExecuteAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("管理命令执行异常", ex);
                _writer.WriteError($"命令执行异常: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 从 DI 容器解析命令实例。复用 ConsoleAppService 中的构造参数。
    /// </summary>
    private TCommand? ResolveCommand<TCommand>() where TCommand : CommandBase
    {
        var configManager = _services.GetRequiredService<ConfigManager>();
        var configuration = _services.GetRequiredService<IConfiguration>();

        return typeof(TCommand).Name switch
        {
            nameof(ProviderCommand) => new ProviderCommand(configManager, configuration, _writer, _ui) as TCommand,
            nameof(ModelCommand) => new ModelCommand(configManager, configuration, _writer, _ui) as TCommand,
            nameof(SkillCommand) => (TCommand)(object)new SkillCommand(configManager, configuration,
                _services.GetRequiredService<SkillRegistry>(), _writer, _ui),
            nameof(RuleCommand) => (TCommand)(object)new RuleCommand(configManager, configuration,
                _services.GetRequiredService<RuleEngine>(), _writer, _ui),
            nameof(MCPCommand) => (TCommand)(object)new MCPCommand(configManager, configuration,
                _services.GetRequiredService<MCPRegistry>(), _writer, _ui),
            nameof(SessionCommand) => (TCommand)(object)new SessionCommand(configManager, configuration,
                _services.GetRequiredService<ISessionManager>(),
                _services.GetRequiredService<SessionRepository>(),
                _services.GetRequiredService<SessionMessageRepository>(), _writer, _ui),
            nameof(StatsCommand) => (TCommand)(object)new StatsCommand(configManager, configuration,
                _services.GetRequiredService<ISessionManager>(),
                _services.GetRequiredService<SessionRepository>(), _writer, _ui),
            nameof(WorkCommand) => (TCommand)(object)new WorkCommand(configManager, configuration,
                _services.GetRequiredService<IWorkspaceManager>(),
                _services.GetRequiredService<WorkspaceRepository>(),
                _services.GetRequiredService<SessionRepository>(), _writer, _ui),
            nameof(RagCommand) => (TCommand)(object)new RagCommand(configManager, configuration,
                _services.GetRequiredService<IWorkspaceManager>(),
                _services.GetRequiredService<WorkspaceRepository>(),
                _services.GetRequiredService<IRetrievalService>(),
                _services.GetRequiredService<RagFileRepository>(),
                _services.GetRequiredService<RagChunkRepository>(),
                _services.GetRequiredService<SessionRepository>(), _writer, _ui),
            _ => null
        };
    }

    /// <summary>子命令缩写展开（与 ConsoleAppService 一致）。</summary>
    private static string[] ExpandSubCommandAliases(string[] parts)
    {
        if (parts.Length < 2) return parts;

        var result = new string[parts.Length];
        result[0] = parts[0];

        for (var i = 1; i < parts.Length; i++)
        {
            result[i] = parts[i] switch
            {
                "-l" => "-list", "-a" => "-add", "-u" => "-update", "-d" => "-delete",
                "-s" => "-switch", "-n" => "-new", "-c" => "-clear", "-t" => "-tools",
                _ => parts[i]
            };
        }

        return result;
    }

}
