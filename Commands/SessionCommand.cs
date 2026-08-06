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
    private readonly SessionMessageRepository _messageRepo;

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
    public SessionCommand(ConfigManager configManager, IConfiguration configuration, ISessionManager sessionManager, SessionRepository sessionRepo, SessionMessageRepository messageRepo)
        : base(configManager, configuration)
    {
        _sessionManager = sessionManager;
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
    }

    /// <summary>
    /// 执行命令（无参数时显示帮助）
    /// </summary>
    public override Task ExecuteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("会话管理用法：");
        Console.WriteLine("  /session -list           - 列出全部会话（更新时间倒序）");
        Console.WriteLine("  /session -new <标题>     - 创建新会话并切换（标题必填）");
        Console.WriteLine("  /session -switch <编号|标题|会话ID>  - 切换到指定会话");
        Console.WriteLine("  /session -clear          - 物理删除全部会话及消息（需确认）");
        Console.WriteLine("  简写: /se -l, /se -n 标题, /se -s 编号/标题/ID, /se -c");
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
            Console.WriteLine("历史会话（更新时间倒序）：");

            if (sessions.Count == 0)
            {
                Console.WriteLine("  （无历史会话）");
                return;
            }

            // 获取每个会话的第一条用户消息预览
            var sessionIds = sessions.Select(s => s.SessionId);
            var previews = await _messageRepo.GetFirstUserMessagePreviewAsync(sessionIds);

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                var isCurrent = _sessionManager.CurrentSession?.SessionId == session.SessionId;
                var marker = isCurrent ? " (当前)" : "";
                
                Console.WriteLine($"  {i + 1}. {session.Title ?? "未命名"}{marker}");
                Console.WriteLine($"     更新: {session.UpdateTime:yyyy-MM-dd HH:mm} | 消息: {session.MessageCount} | Token: {session.TotalTokens}");
                
                if (previews.TryGetValue(session.SessionId, out var preview))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"     💬 {preview}");
                    Console.ForegroundColor = ConsoleColor.Green;
                }
            }
            Console.WriteLine();
            Console.WriteLine("提示: 使用 /se -s <编号> 切换会话");
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

    private async Task SwitchSessionAsync(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            WriteError("用法: /session switch <编号|标题|会话ID>");
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
        if (sessions.Count == 0)
        {
            WriteError("当前工作区没有会话");
            return;
        }

        DbSession? matched = null;

        // 1. 尝试按编号匹配（从1开始的序号）
        if (int.TryParse(identifier, out var index) && index >= 1 && index <= sessions.Count)
        {
            matched = sessions[index - 1]; // sessions 已按 UpdateTime 降序排序
        }
        // 2. 尝试按 SessionId 匹配
        else if (identifier.Length == 32 && Guid.TryParseExact(identifier, "N", out _))
        {
            matched = sessions.FirstOrDefault(s => s.SessionId == identifier);
        }
        // 3. 尝试按标题匹配
        else
        {
            matched = sessions.FirstOrDefault(s => 
                string.Equals(s.Title, identifier, StringComparison.OrdinalIgnoreCase));
        }

        if (matched == null)
        {
            WriteError($"找不到会话: {identifier}");
            Console.WriteLine("可用会话：");
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                var isCurrent = _sessionManager.CurrentSession?.SessionId == s.SessionId;
                var marker = isCurrent ? " (当前)" : "";
                Console.WriteLine($"  {i + 1}. {s.Title ?? "未命名"}{marker}");
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
