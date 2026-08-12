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
using LubanAgent.App;

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
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="sessionManager">会话管理器</param>
    /// <param name="sessionRepo">会话仓储</param>
    /// <param name="messageRepo">会话消息仓储</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public SessionCommand(ConfigManager configManager, IConfiguration configuration, ISessionManager sessionManager, SessionRepository sessionRepo, SessionMessageRepository messageRepo,
        ITuiOutputWriter writer, ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
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
        Writer.WriteLine();
        Writer.WriteHeader("会话管理用法：");
        Writer.WriteLine("  /session -list           - 列出全部会话（更新时间倒序）");
        Writer.WriteLine("  /session -new <标题>     - 创建新会话并切换（标题必填）");
        Writer.WriteLine("  /session -switch <编号|标题|会话ID>  - 切换到指定会话");
        Writer.WriteLine("  /session -clear          - 物理删除全部会话及消息（需确认）");
        Writer.WriteLine("  简写: /se -l, /se -n 标题, /se -s 编号/标题/ID, /se -c");
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
                Writer.WriteLine($"未知子命令: {subCommand}");
                await ExecuteAsync();
                break;
        }
        return true;
    }

    /// <summary>
    /// 列出当前工作区的全部会话
    /// </summary>
    private async Task ListSessionsAsync()
    {
        var wsId = WorkspaceManager.Current?.WorkspaceId;
        if (string.IsNullOrEmpty(wsId))
        {
            Writer.WriteError("请先使用 /work -switch 切换到工作区");
            return;
        }

        var sessions = await _sessionRepo.GetByWorkspaceAsync(wsId);

        if (sessions.Count == 0)
        {
            Writer.WriteInfo("暂无历史会话");
            return;
        }

        var sessionIds = sessions.Select(s => s.SessionId);
        var previews = await _messageRepo.GetFirstUserMessagePreviewAsync(sessionIds);

        var rows = new List<IReadOnlyList<string>>();

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            var isCurrent = _sessionManager.CurrentSession?.SessionId == session.SessionId;
            var marker = isCurrent ? " (当前)" : "";
            var title = (session.Title ?? "未命名") + marker;
            var updateTime = session.UpdateTime.ToString("yyyy-MM-dd HH:mm");
            var messageCount = session.MessageCount.ToString();
            var tokens = session.TotalTokens.ToString();
            var preview = previews.TryGetValue(session.SessionId, out var p) ? p : "";

            rows.Add(new[] { $"{i + 1}. {session.SessionId}", title, updateTime, messageCount, tokens, preview });
        }

        Ui.ShowTable("历史会话（更新时间倒序）", new[] { "会话 ID", "名称", "更新时间", "消息数", "Token", "预览" }, rows);

        Writer.WriteLine();
        Writer.WriteInfo("提示: 使用 /se -s <编号> 切换会话");
    }

    /// <summary>
    /// 创建新会话并切换
    /// </summary>
    /// <param name="title">会话标题</param>
    private async Task CreateNewSessionAsync(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Writer.WriteError("用法: /session new <标题>");
            return;
        }

        var session = await _sessionManager.CreateSessionAsync(userId: "default", title: title);
        Writer.WriteSuccess($"已创建并切换到新会话: {session.Title}");
    }

    /// <summary>
    /// 切换到指定会话（支持编号、SessionId、标题匹配）
    /// </summary>
    /// <param name="identifier">会话标识</param>
    private async Task SwitchSessionAsync(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            Writer.WriteError("用法: /session switch <编号|标题|会话ID>");
            return;
        }

        var wsId = WorkspaceManager.Current?.WorkspaceId;
        if (string.IsNullOrEmpty(wsId))
        {
            Writer.WriteError("请先使用 /work -switch 切换到工作区");
            return;
        }

        var sessions = await _sessionRepo.GetByWorkspaceAsync(wsId);
        if (sessions.Count == 0)
        {
            Writer.WriteError("当前工作区没有会话");
            return;
        }

        DbSession? matched = null;

        if (int.TryParse(identifier, out var index) && index >= 1 && index <= sessions.Count)
        {
            matched = sessions[index - 1];
        }
        else if (identifier.Length == 32 && Guid.TryParseExact(identifier, "N", out _))
        {
            matched = sessions.FirstOrDefault(s => s.SessionId == identifier);
        }
        else
        {
            matched = sessions.FirstOrDefault(s => 
                string.Equals(s.Title, identifier, StringComparison.OrdinalIgnoreCase));
        }

        if (matched == null)
        {
            Writer.WriteError($"找不到会话: {identifier}");
            Writer.WriteLine("可用会话：");
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                var isCurrent = _sessionManager.CurrentSession?.SessionId == s.SessionId;
                var marker = isCurrent ? " (当前)" : "";
                Writer.WriteLine($"  {i + 1}. {s.Title ?? "未命名"}{marker}");
            }
            return;
        }

        await _sessionManager.SetCurrentSessionAsync(matched.SessionId);
        Writer.WriteSuccess($"已切换到会话: {matched.Title}（下一轮对话自动加载该会话历史）");
    }

    /// <summary>
    /// 物理删除全部会话及消息
    /// </summary>
    private async Task ClearAllSessionsAsync()
    {
        if (!Ui.Confirm("删除全部会话", "确认物理删除全部会话及消息数据？此操作不可恢复", defaultValue: false))
        {
            Writer.WriteInfo("已取消");
            return;
        }

        await _sessionManager.ClearAllSessionsAsync();
        Writer.WriteSuccess("已删除全部会话数据");
    }
}
