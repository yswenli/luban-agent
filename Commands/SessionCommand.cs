/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： SessionCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：Session 命令 - 管理会话
*
*****************************************************************************/
namespace LubanAgent.Commands;

/// <summary>
/// Session 命令 - 管理会话（按当前工作区过滤）
/// </summary>
public class SessionCommand : CommandBase
{
    private readonly ISessionManager _sessionManager;
    private readonly SessionRepository _sessionRepo;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "session";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "管理对话会话（-list/-new/-clear/-switch）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    public SessionCommand(ConfigManager configManager, IConfiguration configuration, ISessionManager sessionManager, SessionRepository sessionRepo)
        : base(configManager, configuration)
    {
        _sessionManager = sessionManager;
        _sessionRepo = sessionRepo;
    }

    /// <summary>
    /// 执行命令（无参数时显示帮助）
    /// </summary>
    public override Task ExecuteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("会话管理用法：");
        Console.WriteLine("  /session -list           - 列出全部会话（创建时间倒序）");
        Console.WriteLine("  /session -new <标题>     - 创建新会话并切换（标题必填）");
        Console.WriteLine("  /session -switch <标题>  - 按标题切换到会话");
        Console.WriteLine("  /session -clear          - 物理删除全部会话及消息（需确认）");
        Console.WriteLine("  简写: /se -l, /se -n 标题, /se -s 标题, /se -c");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行带子命令的命令
    /// </summary>
    public override async Task<bool> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
            return false;

        var subCommand = args[0].ToLower();
        var rest = args.Length > 1 ? string.Join(' ', args[1..]).Trim() : null;

        switch (subCommand)
        {
            case "-list":
            case "list":
                await ListSessionsAsync();
                break;
            case "-new":
            case "new":
                await CreateNewSessionAsync(rest);
                break;
            case "-switch":
            case "switch":
                await SwitchSessionAsync(rest);
                break;
            case "-clear":
            case "clear":
                await ClearAllSessionsAsync();
                break;
            default:
                Console.WriteLine($"未知子命令: {subCommand}");
                await ExecuteAsync();
                break;
        }
        return true;
    }

    private async Task ListSessionsAsync()
    {
        // 按当前工作区过滤会话
        var wsId = WorkspaceManager.Current?.WorkspaceId;
        if (string.IsNullOrEmpty(wsId))
        {
            WriteError("请先使用 /work -switch 切换到工作区");
            return;
        }

        var sessions = await _sessionRepo.GetByWorkspaceAsync(wsId);

        try
        {
            Console.ForegroundColor = ConsoleColor.Green;

            Console.WriteLine();
            Console.WriteLine("历史会话（创建时间倒序）：");

            if (sessions.Count == 0)
            {
                Console.WriteLine("  （无历史会话）");
                return;
            }

            foreach (var session in sessions)
            {
                var isCurrent = _sessionManager.CurrentSession?.SessionId == session.SessionId;
                var marker = isCurrent ? " (当前)" : "";
                Console.WriteLine($"  {session.CreateTime:yyyy-MM-dd HH:mm}  {session.Title ?? "未命名"}{marker}");
                Console.WriteLine($"     消息: {session.MessageCount} | Token: {session.TotalTokens}");
            }
        }
        finally
        {
            Console.ResetColor();
        }
    }

    private async Task CreateNewSessionAsync(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            WriteError("用法: /session new <标题>");
            return;
        }

        var session = await _sessionManager.CreateSessionAsync(userId: "default", title: title);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ 已创建并切换到新会话: {session.Title}");
        Console.ResetColor();
    }

    private async Task SwitchSessionAsync(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            WriteError("用法: /session switch <标题>");
            return;
        }

        // 按当前工作区过滤会话
        var wsId = WorkspaceManager.Current?.WorkspaceId;
        if (string.IsNullOrEmpty(wsId))
        {
            WriteError("请先使用 /work -switch 切换到工作区");
            return;
        }

        var sessions = await _sessionRepo.GetByWorkspaceAsync(wsId);
        var matched = sessions
            .Where(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreateTime)
            .FirstOrDefault();

        if (matched == null)
        {
            WriteError($"找不到标题为 \"{title}\" 的会话");
            Console.WriteLine("可用会话：");
            foreach (var s in sessions)
            {
                Console.WriteLine($"  - {s.Title ?? "未命名"}");
            }
            return;
        }

        await _sessionManager.SetCurrentSessionAsync(matched.SessionId);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ 已切换到会话: {matched.Title}（下一轮对话自动加载该会话历史）");
        Console.ResetColor();
    }

    private async Task ClearAllSessionsAsync()
    {
        Console.Write("确认物理删除全部会话及消息数据？此操作不可恢复 (y/N): ");
        var confirm = Console.ReadLine()?.Trim().ToLower();

        if (confirm == "y" || confirm == "yes")
        {
            await _sessionManager.ClearAllSessionsAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ 已删除全部会话数据");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("已取消");
        }
    }
}
